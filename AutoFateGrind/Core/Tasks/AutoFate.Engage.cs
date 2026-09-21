using AutoFateGrind.Core.External;
using AutoFateGrind.Core.Game.Fates;
using AutoFateGrind.Core.Game.Ops;
using AutoFateGrind.Core.Game.Yokai;
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
        await EnsureYokaiCompanion();
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
        var fateName = fate.Name;
        var isCollect = fate.Rule == PublicEvent.FateRule.Collect;
        var spawn = new FateSpawnKey(fateId, fate.StartTimeEpoch);
        Status = $"Engaging {fateName}";

        var lastProgress = fate.Progress;
        var lastProgressAtMs = Environment.TickCount64;
        var lastInCombatAtMs = Environment.TickCount64;
        var lastBounceAtMs = Environment.TickCount64;
        var combatStallBounces = 0;
        // Only an entry that fought the fate while Running may book the completion; the spawn key below
        // guards a re-entry into a Collect FATE's lingering 100% window from double-counting.
        var sawRunning = false;
        var idle = new EngageIdleTracker(EngageReachMeters());

        if (isCollect) BeginCollectFate(spawn, fateName);

        try
        {
            while (!CancelToken.IsCancellationRequested)
            {
                var refreshed = PublicEvent.GetFateById(fateId);
                if (refreshed is null || refreshed.State != FateState.Running) break;
                if (IsPlayerKO()) break;
                fate = refreshed;
                sawRunning = true;

                // A Collect FATE at 100% is won; its row lingers as the hand-in window (leftovers go in below), not a stall.
                if (isCollect && fate.Progress >= 100) break;

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
                    Diag($"EngageFate stalled: no progress in {EngageStallTimeoutMs/1000}s and out of combat {EngageOutOfCombatGraceMs/1000}s on FATE {fateId}; bailing ({DescribeEngageSituation(fateId, idle.Meters)})");
                    RegisterEngageStall(fateId, fate.Name);
                    break;
                }
                else if (Environment.TickCount64 - lastProgressAtMs > EngageCombatStallMs
                      && Environment.TickCount64 - lastBounceAtMs > EngageCombatStallMs
                      && !(Svc.Condition[ConditionFlag.InCombat] && HasTargetInReach(fateId, idle.Meters)))
                {
                    lastBounceAtMs = Environment.TickCount64;
                    combatStallBounces++;
                    if (combatStallBounces > MaxCombatStallBounces)
                    {
                        Diag($"FATE {fateId} still not progressing after {MaxCombatStallBounces} preset bounces ({DescribeEngageSituation(fateId, idle.Meters)})");
                        RegisterEngageStall(fateId, fate.Name);
                        break;
                    }
                    Diag($"No progress in {EngageCombatStallMs/1000}s on FATE {fateId}; bouncing combat preset ({combatStallBounces}/{MaxCombatStallBounces}; {DescribeEngageSituation(fateId, idle.Meters)})");
                    await BounceCombatPreset(preset);
                }

                if (Svc.Condition[ConditionFlag.Mounted])
                {
                    BossModIPC.Instance.ClearActive();
                    await SafeDismount($"dismount-engage-{fateId}");
                    AssertPresetActive(preset);
                }
                else
                {
                    AssertPresetActive(preset);
                }

                SyncToFate(fateId);

                if (isCollect)
                {
                    // A hand-in trip is progress in its own right; give the stall clocks a fresh window after one.
                    if (await MaybeHandInCollectItems(fateId, fateName, preset))
                    {
                        lastProgressAtMs = Environment.TickCount64;
                        lastInCombatAtMs = Environment.TickCount64;
                    }
                }
                else if (await TickEngagementWatchdog(fateId, fate, idle))
                {
                    break;
                }

                await NextFrame(30);
            }

            if (isCollect && sawRunning && PublicEvent.GetFateById(fateId) is { Progress: >= 100 })
                await WrapUpCollectFate(fateId, fateName, preset);
        }
        finally
        {
            BossModIPC.Instance.ClearActive();
            if (isCollect) DisableTextAdvance();
        }

        var finalProgress = PublicEvent.GetFateById(fateId)?.Progress ?? lastProgress;
        var ended = sawRunning && (PublicEvent.GetFateById(fateId) is null || finalProgress >= 100);
        if (ended && lastCompletedSpawn != spawn)
        {
            lastCompletedSpawn = spawn;
            ClearEngageStall(fateId);
            session.CompletedCount++;
            session.FatesSinceLastBreak++;
            zone.CompletedThisRun++;
            // A Collect reward only lands once the row clears, so there is nothing to settle at 100% yet.
            if (isCollect) session.UpdateGemstones(); else await SettleGemstoneReward();
            session.UpdateExp();
            YokaiProgress.Invalidate();
            Diag($"FATE {fateId} done (session total: {session.CompletedCount}, wallet {session.GemstoneCurrent}g)");
            StartFollowUpWatch(fateId);

            if (AdvanceClassQueueIfCapHit()) return ExitReason.Quit;

            if (QueueHandoffIfDue())
            {
                await HoldForCollectReward();
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

    private async Task<bool> TickEngagementWatchdog(uint fateId, PublicEvent fate, EngageIdleTracker idle)
    {
        if (fate.Rule == PublicEvent.FateRule.Collect)
        {
            return false;
        }
        if (Svc.Condition[ConditionFlag.Mounted])
        {
            return false;
        }
        if (StuckDetector.IsPositionFrozenLegit())
        {
            return false;
        }
        if (Svc.Objects.LocalPlayer is not { } player)
        {
            return false;
        }

        if (Svc.Condition[ConditionFlag.InCombat] && HasTargetInReach(fateId, idle.Meters))
        {
            idle.MarkInReach();
            return false;
        }

        if (!idle.Stalled(player.Position))
        {
            return false;
        }

        var fateName = fate.Name;
        var survey = FateMobScanner.Survey(fateId, player.Position);
        if (!survey.Any)
        {
            if (await SeekFateCentre(fateId, fateName, fate.Position, player.Position))
            {
                Status = $"Engaging {fateName}";
            }
            idle.Restart();
            return false;
        }

        if (idle.Repositions >= MaxEngageRepositions)
        {
            Diag($"FATE {fateId} ({fateName}) unreachable: still {survey.NearestDistanceToHitbox:F0}m from the nearest mob's hitbox after {MaxEngageRepositions} repositions; abandoning and blacklisting for this session ({DescribeEngageSituation(fateId, idle.Meters)})");
            AbandonFate(fateId);
            return true;
        }

        await RepositionToFateMob(fateId, fateName, survey, idle);
        idle.Restart();
        Status = $"Engaging {fateName}";
        return false;
    }

    private static bool HasTargetInReach(uint fateId, float reachMeters)
        => Svc.Objects.LocalPlayer is { } player
        && FateMobScanner.TryGetTargetedMob(fateId, player.Position, out var distance)
        && distance <= reachMeters;

    private async Task RepositionToFateMob(uint fateId, string fateName, FateMobSurvey survey, EngageIdleTracker idle)
    {
        idle.CountReposition();
        Status = $"Closing on {fateName}";
        Diag($"Engagement idle on FATE {fateId} ({fateName}) for {EngageIdleStallMs / 1000}s with nothing in reach; walking to the nearest mob with vnav (attempt {idle.Repositions}/{MaxEngageRepositions}; {DescribeEngageSituation(fateId, idle.Meters)})");

        var dest = survey.NearestPosition.OnMesh();
        var tolerance = survey.NearestHitboxRadius + (idle.Meters <= EngageMeleeReachMeters
            ? EngageMeleeApproachToleranceMeters
            : EngageRangedApproachToleranceMeters);
        var config = MovementConfig.Default.WithTolerance(tolerance);
        var reachMeters = idle.Meters;

        bool InRangeOrGone()
        {
            if (PublicEvent.GetFateById(fateId) is not { State: FateState.Running })
            {
                return true;
            }
            if (Svc.Objects.LocalPlayer is not { } moving)
            {
                return true;
            }
            var live = FateMobScanner.Survey(fateId, moving.Position);
            return live.Any && live.NearestDistanceToHitbox <= reachMeters;
        }

        await WalkWithBossModParked(dest, config, InRangeOrGone, $"engage-reposition-{fateId}");
    }

    private async Task<bool> SeekFateCentre(uint fateId, string fateName, Vector3 centre, Vector3 from)
    {
        var distance = Vector3.Distance(from, centre);
        if (distance <= EngageCentreSeekMinMeters)
        {
            return false;
        }

        Status = $"Searching {fateName}";
        Diag($"No live mob of FATE {fateId} ({fateName}) is loaded; walking to the ring centre {distance:F0}m away to load the rest");

        var dest = centre.OnMesh();
        var config = MovementConfig.Default.WithTolerance(EngageCentreSeekToleranceMeters);

        bool MobSeenOrGone()
        {
            if (PublicEvent.GetFateById(fateId) is not { State: FateState.Running })
            {
                return true;
            }
            if (Svc.Objects.LocalPlayer is not { } moving)
            {
                return true;
            }
            return FateMobScanner.Survey(fateId, moving.Position).Any;
        }

        await WalkWithBossModParked(dest, config, MobSeenOrGone, $"engage-seek-centre-{fateId}");
        return true;
    }

    // On foot only: clib's Mount() has no in-combat guard and spins until the idle abort.
    private async Task WalkWithBossModParked(Vector3 dest, MovementConfig config, Func<bool> stopCondition, string label)
    {
        var preset = Plugin.Cfg.CombatPresetName;
        var parked = ParkBossModMovement(preset);
        try
        {
            var op = new MoveOp(o => o.MoveInZone(dest, config, stopCondition));
            await RunCancellable(op, EngageRepositionWatchdogMs, label, StuckDetector.MoveStallAbort(label));

            if (op.Fault is { } fault)
            {
                Diag($"{label} faulted: {fault.Message}");
            }
        }
        finally
        {
            if (parked)
            {
                ResumeBossModMovement(preset);
            }
        }
    }

    private void RegisterEngageStall(uint fateId, string fateName)
    {
        if (engageStallFateId != fateId)
        {
            engageStallFateId = fateId;
            engageStallStrikes = 0;
        }

        engageStallStrikes++;
        if (engageStallStrikes < MaxEngageStallStrikes)
        {
            Diag($"FATE {fateId} ({fateName}) engagement bail {engageStallStrikes}/{MaxEngageStallStrikes}; re-entering engagement from scratch");
            return;
        }

        Diag($"FATE {fateId} ({fateName}) made no progress through {engageStallStrikes} engagement attempts; abandoning and blacklisting for this session");
        AbandonFate(fateId);
    }

    private void ClearEngageStall(uint fateId)
    {
        if (engageStallFateId != fateId)
        {
            return;
        }
        engageStallFateId = null;
        engageStallStrikes = 0;
    }

    private void AbandonFate(uint fateId)
    {
        abandonedFateId = fateId;
        sessionStuckFateIds.Add(fateId);
        ClearEngageStall(fateId);
    }

    private static unsafe string DescribeEngageSituation(uint fateId, float reachMeters)
    {
        if (Svc.Objects.LocalPlayer is not { } player)
        {
            return "player=none";
        }

        var position = player.Position;
        var survey = FateMobScanner.Survey(fateId, position);
        var target = Svc.Targets.Target;
        var targetDescription = target is null
            ? "none"
            : FateMobScanner.TryGetTargetedMob(fateId, position, out var targetDistance)
                ? $"{target.Name}@{targetDistance:F0}m"
                : $"{target.Name}(not this FATE)";
        var nearest = survey.Any
            ? $"{survey.NearestDistanceToHitbox:F0}m dY={survey.NearestVerticalDelta:F0}"
            : "none";
        var manager = CSFateManager.Instance();
        var synced = manager is not null && manager->SyncedFateId == fateId;

        return $"pos=({position.X:F0},{position.Y:F0},{position.Z:F0}) combat={Svc.Condition[ConditionFlag.InCombat]} target={targetDescription} liveMobs={survey.LiveCount} nearest={nearest} reach={reachMeters:F0}m synced={synced} preset={BossModIPC.Instance.GetActive() ?? "none"}";
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

    private sealed class EngageIdleTracker(float reachMeters)
    {
        private Vector3 anchor;
        private bool anchored;
        private long idleSinceMs;

        public float Meters { get; } = reachMeters;
        public int Repositions { get; private set; }

        public bool Stalled(Vector3 position)
        {
            var now = Environment.TickCount64;
            if (!anchored || Vector3.Distance(anchor, position) > StuckDetector.StuckMoveThresholdMeters)
            {
                anchored = true;
                anchor = position;
                idleSinceMs = now;
                return false;
            }
            return now - idleSinceMs >= EngageIdleStallMs;
        }

        public void CountReposition() => Repositions++;

        public void MarkInReach()
        {
            Repositions = 0;
            Restart();
        }

        public void Restart() => anchored = false;
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

        var trader = GemstoneTrader.PickForItem(targetId, zone.TerritoryId, zone.Expansion, out var availability);
        if (trader is null)
        {
            Diag(availability == TraderAvailability.AllLocked
                ? $"Trade-on-cap skipped: every Bicolor trader selling {target.ItemName} stands in an unattuned zone ({GemstoneTrader.DescribeSellerZones(targetId)}). Pick a different item in /afg config → Trader."
                : $"Trade-on-cap skipped: no registered Bicolor trader sells {target.ItemName}. Pick a different item in /afg config → Trader.");
            return false;
        }

        Diag($"Gemstone threshold {Plugin.Cfg.TradeThreshold}g reached: queueing auto-trade for {qty}× {target.ItemName} at {trader.Name} (territory {trader.TerritoryId}).");
        session.PendingTradeFromZone = zone;
        return true;
    }

}
