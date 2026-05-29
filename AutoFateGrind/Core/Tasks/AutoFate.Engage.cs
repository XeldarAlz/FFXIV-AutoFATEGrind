using AutoFateGrind.Core.External;
using AutoFateGrind.Core.Game;
using AutoFateGrind.Core.Ipc;
using AutoFateGrind.Core.Modes;
using AutoFateGrind.Core.Trading;
using AutoFateGrind.Core.Zones;
using clib.Extensions;
using clib.TaskSystem;
using clib.Utils;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using System.Numerics;
using System.Threading.Tasks;
using CSFateManager = FFXIVClientStructs.FFXIV.Client.Game.Fate.FateManager;

namespace AutoFateGrind.Core.Tasks;

public sealed partial class AutoFate
{
    private async Task<ExitReason> MoveAndArrive()
    {
        var player = Svc.Objects.LocalPlayer;
        if (player is null) { await NextFrame(); return ExitReason.Continue; }

        await EnsureConsumables();
        if (CancelToken.IsCancellationRequested) return ExitReason.Quit;

        var fate = FateScanner.PickNext(Plugin.Cfg, player.Position, sessionStuckFateIds, returnToFateId);
        if (fate is null) return ExitReason.Continue;

        idleScans = 0;
        Status = $"Moving to {fate.Name}";
        Diag($"Picked FATE {fate.Id} ({fate.Name}) at {fate.Position}");

        var moveResult = await MoveToFate(fate);
        if (CancelToken.IsCancellationRequested) return ExitReason.Quit;

        if (moveResult is MoveStopReason.HigherPriority or MoveStopReason.NpcSpawned)
            return ExitReason.Continue;

        // Teleport can't fire in combat, and the FATE is still reachable — fight free, don't blacklist.
        if (moveResult == MoveStopReason.StuckInCombat)
        {
            await ClearBlockingCombat();
            return ExitReason.Continue;
        }

        if (lastTeleportedFateId == fate.Id && moveResult != MoveStopReason.None)
        {
            Diag($"Still stuck after teleport recovery for FATE {fate.Id} ({fate.Name}); blacklisting for this session");
            sessionStuckFateIds.Add(fate.Id);
            lastTeleportedFateId = null;
            lastStuckFateId = null;
            consecutiveStuckRetries = 0;
            return ExitReason.Continue;
        }

        if (moveResult == MoveStopReason.StuckRetry)
        {
            if (lastStuckFateId == fate.Id) consecutiveStuckRetries++;
            else { lastStuckFateId = fate.Id; consecutiveStuckRetries = 1; }

            if (consecutiveStuckRetries >= 2)
            {
                Diag($"Repeated stuck on FATE {fate.Id} ({fate.Name}); escalating to teleport");
                moveResult = MoveStopReason.StuckTeleport;
            }
            else
            {
                Diag($"Stuck en route to FATE {fate.Id} ({fate.Name}); retrying from current position");
                return ExitReason.Continue;
            }
        }

        if (moveResult == MoveStopReason.StuckTeleport)
        {
            if (Svc.Condition[ConditionFlag.InCombat])
            {
                Diag($"Stuck-teleport for FATE {fate.Id} but in combat; clearing aggro before teleporting (teleport is blocked in combat)");
                await ClearBlockingCombat();
                return ExitReason.Continue;
            }
            if (await TryTeleportToFate(fate))
            {
                lastTeleportedFateId = fate.Id;
                lastStuckFateId = null;
                consecutiveStuckRetries = 0;
                return ExitReason.Continue;
            }
            sessionStuckFateIds.Add(fate.Id);
            lastTeleportedFateId = null;
            lastStuckFateId = null;
            consecutiveStuckRetries = 0;
            Diag($"Teleport recovery failed for FATE {fate.Id}; blacklisting for this session");
            return ExitReason.Continue;
        }

        if (lastStuckFateId == fate.Id) { lastStuckFateId = null; consecutiveStuckRetries = 0; }
        if (lastTeleportedFateId == fate.Id) lastTeleportedFateId = null;

        // clib's PublicEvent getters deref a freed FateContext* and throw NRE; re-resolve before reading
        // native fields. A null handle means the FATE finished/expired mid-move (incl. MoveStopReason.FateInvalid).
        var arrived = PublicEvent.GetFateById(fate.Id);
        if (arrived is null) return ExitReason.Continue;
        fate = arrived;

        // Boss/event FATEs must be activated via their NPC before they go Running. 0xE0000000 = no NPC.
        if (fate.State == FateState.Preparing && fate.MotivationNpcId != 0xE0000000)
            await ActivateFate(fate);

        if (returnToFateId == fate.Id && fate.State == FateState.Running)
            returnToFateId = null;

        return ExitReason.Continue;
    }

    private async Task<ExitReason> EngageCurrentFate()
    {
        var fate = PublicEvent.CurrentFate;
        if (fate is null) return ExitReason.Continue;
        var fateId = fate.Id;

        var preset = Plugin.Cfg.CombatPresetName;
        EnsureCombatPreset(preset);
        SyncToFate(fateId);
        AssertPresetActive(preset);

        await EnsureObstacleMapForEngage(fate);

        // The FATE can end during obstacle-map generation; re-resolve before reading native fields so a
        // freed FateContext* can't NRE (same hazard as the engage loop below, which re-resolves each tick).
        if (PublicEvent.GetFateById(fateId) is not { } live) return ExitReason.Continue;
        fate = live;
        Status = $"Engaging {fate.Name}";

        var lastProgress = fate.Progress;
        var lastProgressAtMs = Environment.TickCount64;
        var lastInCombatAtMs = Environment.TickCount64;
        var collectTextAdvanceArmed = false;
        // Only an entry that fought the fate while Running may book the completion — guards against
        // a re-entry during the lingering 100% frame double-counting.
        var sawRunning = false;

        try
        {
            while (!CancelToken.IsCancellationRequested)
            {
                var refreshed = PublicEvent.GetFateById(fateId);
                if (refreshed is null || refreshed.State != FateState.Running) break;
                if (IsPlayerKO()) break;
                fate = refreshed;
                sawRunning = true;

                if (Svc.Condition[ConditionFlag.InCombat])
                    lastInCombatAtMs = Environment.TickCount64;

                if (fate.Progress != lastProgress)
                {
                    lastProgress = fate.Progress;
                    lastProgressAtMs = Environment.TickCount64;
                }
                else if (Environment.TickCount64 - lastProgressAtMs > EngageStallTimeoutMs
                      && Environment.TickCount64 - lastInCombatAtMs > EngageOutOfCombatGraceMs)
                {
                    Diag($"EngageFate stalled: no progress in {EngageStallTimeoutMs/1000}s and out of combat {EngageOutOfCombatGraceMs/1000}s on FATE {fateId}; bailing");
                    break;
                }

                if (Svc.Condition[ConditionFlag.Mounted])
                {
                    BossModIPC.Instance.ClearActive();
                    await DismountViaOp($"dismount-engage-{fateId}");
                    AssertPresetActive(preset);
                }
                else
                {
                    AssertPresetActive(preset);
                }

                SyncToFate(fateId);

                if (fate.Rule == PublicEvent.FateRule.Collect && !collectTextAdvanceArmed)
                {
                    EnableTextAdvanceForCollect();
                    collectTextAdvanceArmed = true;
                }

                await NextFrame(30);
            }
        }
        finally
        {
            BossModIPC.Instance.ClearActive();
            if (collectTextAdvanceArmed) DisableTextAdvance();
        }

        var finalProgress = PublicEvent.GetFateById(fateId)?.Progress ?? lastProgress;
        var ended = sawRunning && (PublicEvent.GetFateById(fateId) is null || finalProgress >= 100);
        if (ended)
        {
            session.CompletedCount++;
            session.FatesSinceLastBreak++;
            zone.CompletedThisRun++;
            session.GemstoneCurrent = GemstoneCatalog.CurrentWalletCount();
            session.UpdateExp();
            Diag($"FATE {fateId} done (session total: {session.CompletedCount}, wallet {session.GemstoneCurrent}g)");
            StartFollowUpWatch(fateId);

            if (AdvanceClassQueueIfCapHit()) return ExitReason.Quit;

            if (Plugin.Cfg.AutoRepair
                && RepairOps.NeedsRepair(Plugin.Cfg.AutoRepairThresholdPct))
            {
                Diag($"Repair threshold tripped (lowest equipped at {RepairOps.LowestEquippedConditionPct():F0}% ≤ {Plugin.Cfg.AutoRepairThresholdPct}%); queueing repair hand-off.");
                session.PendingRepair = true;
                session.PendingRepairFromZone = zone;
                return ExitReason.Quit;
            }

            if (Plugin.Cfg.TradeOnCap && session.GemstoneCurrent >= Plugin.Cfg.TradeThreshold)
            {
                if (TryQueueTrade()) return ExitReason.Quit;
            }

            if (Plugin.Cfg.HumanizerEnabled
             && Plugin.Cfg.HumanizerCities.Count > 0
             && session.FatesSinceLastBreak >= Math.Max(1, Plugin.Cfg.HumanizerFatesBeforeBreak))
            {
                Diag($"Humanizer threshold {Plugin.Cfg.HumanizerFatesBeforeBreak} reached (counter {session.FatesSinceLastBreak}); queueing break hand-off.");
                session.PendingHumanize = true;
                session.PendingHumanizeFromZone = zone;
                return ExitReason.Quit;
            }
        }

        return ExitReason.Continue;
    }

    private bool TryQueueTrade()
    {
        var targetId = GemstoneCatalog.EnsurePersistedTarget();
        if (targetId == 0)
        {
            Diag("Trade-on-cap skipped: EnsurePersistedTarget returned 0 (no gem catalog item maps to a registered Bicolor trader).");
            return false;
        }

        var target = GemstoneCatalog.FindById(targetId);
        if (target is null)
        {
            Diag($"Trade-on-cap skipped: saved target id {targetId} is not in the gem catalog (was the item removed or renamed?).");
            return false;
        }

        var qty = GemstoneCatalog.ComputeBuyQuantity(session.GemstoneCurrent, target.CostPerOne);
        if (qty <= 0)
        {
            Diag($"Trade-on-cap skipped: spend mode {Plugin.Cfg.SpendMode} with {Plugin.Cfg.KeepGemstonesReserve}g reserve buys 0× {target.ItemName} ({target.CostPerOne}g each, wallet {session.GemstoneCurrent}g).");
            return false;
        }

        var trader = GemstoneTrader.PickForItem(targetId, zone.TerritoryId, zone.Expansion);
        if (trader is null)
        {
            Diag($"Trade-on-cap skipped: no registered Bicolor trader sells {target.ItemName}. Pick a different item in /afg config → Trader.");
            return false;
        }

        Diag($"Gemstone threshold {Plugin.Cfg.TradeThreshold}g reached: queueing auto-trade for {qty}× {target.ItemName} at {trader.Name} (territory {trader.TerritoryId}).");
        session.PendingTradeFromZone = zone;
        return true;
    }

}
