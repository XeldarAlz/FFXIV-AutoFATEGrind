using AutoFateGrind.Core.External;
using AutoFateGrind.Core.Game.Fates;
using AutoFateGrind.Core.Game.Ops;
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

        // Snapshot id/name while the handle is fresh: a LeftZone move ends in another territory where the
        // clib PublicEvent getters would NRE on the now-despawned handle, and the blacklist below must land.
        var pickedId = fate.Id;
        var pickedName = fate.Name;
        Status = $"Moving to {fate.Name}";
        Diag($"Picked FATE {fate.Id} ({fate.Name}) at {fate.Position}");

        var moveResult = await MoveToFate(fate);
        if (CancelToken.IsCancellationRequested) return ExitReason.Quit;

        if (moveResult is MoveStopReason.HigherPriority)
            return ExitReason.Continue;

        // Teleport can't fire in combat, and the FATE is still reachable — fight free, don't blacklist.
        if (moveResult == MoveStopReason.StuckInCombat)
        {
            await ClearBlockingCombat();
            return ExitReason.Continue;
        }

        if (moveResult is MoveStopReason.LeftZone)
        {
            lastTeleportedFateId = null;
            lastStuckFateId = null;
            consecutiveStuckRetries = 0;
            sessionStuckFateIds.Add(pickedId);
            Diag($"FATE {pickedId} ({pickedName}) left {zone.Name} despite an in-zone-only route; blacklisting for this session");
            return ExitReason.Continue;
        }

        if (lastTeleportedFateId == fate.Id && moveResult is not MoveStopReason.None and not MoveStopReason.NpcSpawned)
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

        // Boss/event FATEs must be activated via their NPC before they go Running.
        if (FateScanner.AwaitsNpcStart(fate))
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
        var lastBounceAtMs = Environment.TickCount64;
        var combatStallBounces = 0;
        var collectTextAdvanceArmed = false;
        // Only an entry that fought the fate while Running may book the completion — guards against
        // a re-entry during the lingering 100% frame double-counting.
        var sawRunning = false;
        var reach = new EngageReachTracker(EngageReachMeters());

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
                    combatStallBounces = 0;
                }
                else if (Environment.TickCount64 - lastProgressAtMs > EngageStallTimeoutMs
                      && Environment.TickCount64 - lastInCombatAtMs > EngageOutOfCombatGraceMs)
                {
                    Diag($"EngageFate stalled: no progress in {EngageStallTimeoutMs/1000}s and out of combat {EngageOutOfCombatGraceMs/1000}s on FATE {fateId}; bailing");
                    break;
                }
                else if (Environment.TickCount64 - lastProgressAtMs > EngageCombatStallMs
                      && Environment.TickCount64 - lastBounceAtMs > EngageCombatStallMs
                      && !(Svc.Condition[ConditionFlag.InCombat] && HasTargetInReach(fateId, reach.Meters)))
                {
                    lastBounceAtMs = Environment.TickCount64;
                    combatStallBounces++;
                    if (combatStallBounces > MaxCombatStallBounces)
                    {
                        Diag($"FATE {fateId} still not progressing after {MaxCombatStallBounces} preset bounces; re-entering engagement from scratch");
                        break;
                    }
                    Diag($"No progress in {EngageCombatStallMs/1000}s on FATE {fateId} (combat={Svc.Condition[ConditionFlag.InCombat]}); bouncing combat preset ({combatStallBounces}/{MaxCombatStallBounces})");
                    await BounceCombatPreset(preset);
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

                if (await TickEngagementWatchdog(fateId, fate, reach)) break;

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
            await SettleGemstoneReward();
            session.UpdateExp();
            Diag($"FATE {fateId} done (session total: {session.CompletedCount}, wallet {session.GemstoneCurrent}g)");
            StartFollowUpWatch(fateId);

            if (AdvanceClassQueueIfCapHit()) return ExitReason.Quit;

            if (QueueHandoffIfDue())
            {
                await ClearBlockingCombat();
                return ExitReason.Quit;
            }
        }

        return ExitReason.Continue;
    }

    // Hand-off tasks run with the rotation off, and their teleport is rejected for as long as a stray
    // add keeps the character in combat, so the grind fights free before it quits.
    private bool QueueHandoffIfDue()
    {
        if (Plugin.Cfg.AutoRepair && RepairOps.NeedsRepair(Plugin.Cfg.AutoRepairThresholdPct))
        {
            Diag($"Repair threshold tripped (lowest equipped at {RepairOps.LowestEquippedConditionPct():F0}% ≤ {Plugin.Cfg.AutoRepairThresholdPct}%); queueing repair hand-off.");
            session.PendingRepair = true;
            session.PendingRepairFromZone = zone;
            return true;
        }

        if (Plugin.Cfg.TradeOnCap && session.GemstoneCurrent >= Plugin.Cfg.TradeThreshold && TryQueueTrade())
            return true;

        if (Plugin.Cfg.HumanizerEnabled
         && Plugin.Cfg.HumanizerCities.Count > 0
         && session.FatesSinceLastBreak >= Math.Max(1, Plugin.Cfg.HumanizerFatesBeforeBreak))
        {
            Diag($"Humanizer threshold {Plugin.Cfg.HumanizerFatesBeforeBreak} reached (counter {session.FatesSinceLastBreak}); queueing break hand-off.");
            session.PendingHumanize = true;
            session.PendingHumanizeFromZone = zone;
            return true;
        }

        return false;
    }

    private static float EngageReachMeters()
    {
        var player = Svc.Objects.LocalPlayer;
        if (player is null) return EngageRangedReachMeters;

        var role = player.ClassJob.Value.Role;
        return role is RoleTank or RoleMelee ? EngageMeleeReachMeters : EngageRangedReachMeters;
    }

    private async Task BounceCombatPreset(string preset)
    {
        BossModIPC.Instance.ClearActive();
        await NextFrame(2);
        AssertPresetActive(preset);
    }

    private async Task<bool> TickEngagementWatchdog(uint fateId, PublicEvent fate, EngageReachTracker reach)
    {
        if (fate.Rule == PublicEvent.FateRule.Collect) return false;
        if (Svc.Condition[ConditionFlag.Mounted]) return false;
        if (StuckDetector.IsPositionFrozenLegit()) return false;
        if (Svc.Objects.LocalPlayer is not { } player) return false;

        if (HasTargetInReach(fateId, reach.Meters))
        {
            reach.MarkInReach();
            return false;
        }

        if (!FateMobScanner.TryFindNearestMob(fateId, player.Position, out var mobPos, out var mobHitbox, out var mobDistance))
        {
            reach.Restart();
            return false;
        }

        if (!reach.Stalled(mobDistance)) return false;

        var fateName = fate.Name;

        if (reach.Repositions >= MaxEngageRepositions)
        {
            Diag($"FATE {fateId} ({fateName}) unreachable: still {mobDistance:F0}m from the nearest mob's hitbox after {MaxEngageRepositions} repositions; abandoning and blacklisting for this session");
            abandonedFateId = fateId;
            sessionStuckFateIds.Add(fateId);
            return true;
        }

        await RepositionToFateMob(fateId, fateName, mobPos, mobHitbox, mobDistance, reach);
        reach.Restart();
        Status = $"Engaging {fateName}";
        return false;
    }

    private static bool HasTargetInReach(uint fateId, float reachMeters)
        => Svc.Objects.LocalPlayer is { } player
        && FateMobScanner.TryGetTargetedMob(fateId, player.Position, out var distance)
        && distance <= reachMeters;

    private async Task RepositionToFateMob(uint fateId, string fateName, Vector3 mobPos, float mobHitbox, float mobDistance, EngageReachTracker reach)
    {
        reach.CountReposition();
        Status = $"Closing on {fateName}";
        Diag($"Engagement stalled on FATE {fateId} ({fateName}): nearest mob {mobDistance:F0}m from its hitbox (reach {reach.Meters:F0}m) with no approach in {EngageReachStallMs / 1000}s; walking in with vnav (attempt {reach.Repositions}/{MaxEngageRepositions})");

        var dest = mobPos.OnMesh();
        var tolerance = mobHitbox + (reach.Meters <= EngageMeleeReachMeters
            ? EngageMeleeApproachToleranceMeters
            : EngageRangedApproachToleranceMeters);
        // On foot only: clib's Mount() has no in-combat guard and spins until the idle abort.
        var config = MovementConfig.Default.WithTolerance(tolerance);
        var reachMeters = reach.Meters;

        bool InRangeOrGone()
        {
            if (PublicEvent.GetFateById(fateId) is not { State: FateState.Running }) return true;
            if (Svc.Objects.LocalPlayer is not { } moving) return true;
            return FateMobScanner.TryFindNearestMob(fateId, moving.Position, out _, out _, out var live)
                && live <= reachMeters;
        }

        var preset = Plugin.Cfg.CombatPresetName;
        var parked = ParkBossModMovement(preset);
        try
        {
            var op = new MoveOp(o => o.MoveInZone(dest, config, InRangeOrGone));
            await RunCancellable(op, EngageRepositionWatchdogMs, $"engage-reposition-{fateId}",
                StuckDetector.MoveStallAbort($"engage-reposition-{fateId}"));

            if (op.Fault is { } fault)
                Diag($"Reposition for FATE {fateId} faulted: {fault.Message}");
        }
        finally
        {
            if (parked) ResumeBossModMovement(preset);
        }
    }

    private const string NormalMovementModule = "BossMod.Autorotation.MiscAI.NormalMovement";
    private const string NormalMovementDestinationTrack = "Destination";
    private const string NormalMovementParkedOption = "None";

    // Hand movement to vnav without dropping the preset so the rotation keeps attacking on the way.
    // Only park when the override can be cleared again; otherwise fall back to clearing the preset,
    // which the engage loop re-asserts on its next tick.
    private static bool ParkBossModMovement(string preset)
    {
        if (BossModIPC.Instance.CanClearTransientStrategy
         && BossModIPC.Instance.AddTransientStrategy(preset, NormalMovementModule, NormalMovementDestinationTrack, NormalMovementParkedOption))
            return true;

        BossModIPC.Instance.ClearActive();
        return false;
    }

    private void ResumeBossModMovement(string preset)
    {
        if (BossModIPC.Instance.ClearTransientStrategy(preset, NormalMovementModule, NormalMovementDestinationTrack)) return;

        Diag($"Could not clear the NormalMovement override on preset '{preset}'; re-applying the preset instead");
        BossModIPC.Instance.ClearActive();
    }

    private sealed class EngageReachTracker(float reachMeters)
    {
        private float anchorDistance = float.MaxValue;
        private long stalledSinceMs = Environment.TickCount64;

        public float Meters { get; } = reachMeters;
        public int Repositions { get; private set; }

        public bool Stalled(float nearestDistance)
        {
            var now = Environment.TickCount64;

            if (nearestDistance <= Meters)
            {
                MarkInReach();
                return false;
            }

            if (nearestDistance + EngageApproachProgressMeters < anchorDistance)
            {
                anchorDistance = nearestDistance;
                stalledSinceMs = now;
                return false;
            }

            if (nearestDistance > anchorDistance) anchorDistance = nearestDistance;

            return now - stalledSinceMs >= EngageReachStallMs;
        }

        public void CountReposition() => Repositions++;

        public void MarkInReach()
        {
            Repositions = 0;
            Restart();
        }

        public void Restart()
        {
            anchorDistance = float.MaxValue;
            stalledSinceMs = Environment.TickCount64;
        }
    }

    private async Task SettleGemstoneReward()
    {
        if (!GemstoneCatalog.TryCurrentWalletCount(out var before)) { session.UpdateGemstones(); return; }

        var deadline = Environment.TickCount64 + GemstoneSettleTimeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (CancelToken.IsCancellationRequested) break;
            if (GemstoneCatalog.TryCurrentWalletCount(out var now) && now != before) break;
            await DelayMs(GemstoneSettlePollMs);
        }

        session.UpdateGemstones();
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
