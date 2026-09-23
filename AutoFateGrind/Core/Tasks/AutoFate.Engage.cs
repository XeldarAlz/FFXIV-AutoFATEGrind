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
using FFXIVClientStructs.FFXIV.Client.Enums;
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

        // A completed Collect FATE can remain in CurrentFate during its reward window, then leave it briefly
        // empty when the reward settles during travel. Remember that we physically reached this running FATE
        // so the next state tick engages it instead of issuing the same move again in either transition.
        var reached = PublicEvent.GetFateById(pickedId);
        if (reached is { State: FateState.Running, Progress: < 100 }
         && PublicEvent.CurrentFate?.Id != pickedId
         && Svc.Objects.LocalPlayer is { } arrivedPlayer
         && Vector3.Distance(arrivedPlayer.Position, reached.Position) <= reached.Radius)
        {
            arrivedFateId = pickedId;
            Diag($"Reached FATE {pickedId} ({pickedName}) while CurrentFate is stale or empty; engaging without repeating movement");
        }

        if (returnToFateId == fate.Id && fate.State == FateState.Running)
            returnToFateId = null;

        return ExitReason.Continue;
    }

    private async Task<ExitReason> EngageCurrentFate()
    {
        var fate = ResolveEngagementFate(PublicEvent.CurrentFate);
        if (fate is null) return ExitReason.Continue;
        var fateId = fate.Id;
        if (arrivedFateId == fateId) arrivedFateId = null;

        // A ring the character is already standing in still has to pay, so the minion comes out before the rotation starts.
        await EnsureYokaiCompanion();
        if (PublicEvent.GetFateById(fateId) is null) return ExitReason.Continue;

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
        var fateType = fate.FateType;
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
                if (IsPlayerKO())
                {
                    RegisterDeath(fateId, fateName, fateType);
                    break;
                }
                fate = refreshed;
                sawRunning = true;

                if (FateBlacklist.Contains(Plugin.Cfg, fate))
                {
                    Diag($"FATE {fateId} ({fateName}) is blacklisted; leaving it for the next pick");
                    LeaveFate(fateId);
                    break;
                }

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

                if (isCollect && await MaybeHandInCollectItems(fateId, fateName, preset))
                {
                    // A hand-in trip is progress in its own right; give the stall clocks a fresh window after one.
                    lastProgressAtMs = Environment.TickCount64;
                    lastInCombatAtMs = Environment.TickCount64;
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
            LogYokaiDropState();
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
        if (Svc.Condition[ConditionFlag.Mounted])
        {
            idle.ResetTargetRangeWatch();
            return false;
        }
        if (StuckDetector.IsPositionFrozenLegit())
        {
            idle.ResetTargetRangeWatch();
            return false;
        }
        if (Svc.Objects.LocalPlayer is not { } player)
        {
            idle.ResetTargetRangeWatch();
            return false;
        }

        if (FateMobScanner.TrySurveyTargetedMob(fateId, player.Position, out var target))
        {
            if (target.DistanceToHitbox <= idle.Meters)
            {
                idle.ResetTargetRangeWatch();
                if (Svc.Condition[ConditionFlag.InCombat])
                {
                    idle.MarkInReach();
                    return false;
                }
            }
            else if (!idle.TargetOutOfRangeLongEnough(target, player.Position))
            {
                return false;
            }
            else
            {
                await RepositionToTargetedFateMob(fateId, fate.Name, target, idle);
                idle.Restart();
                Status = $"Engaging {fate.Name}";
                return false;
            }
        }
        else
        {
            idle.ResetTargetRangeWatch();
        }

        // Collect FATEs use their own pickup and hand-in movement, but still need the selected-target
        // range correction above while fighting for materials.
        if (fate.Rule == PublicEvent.FateRule.Collect)
        {
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

    private async Task RepositionToTargetedFateMob(uint fateId, string fateName, FateMobTarget target, EngageIdleTracker idle)
    {
        Status = $"Closing on {fateName}";
        Diag($"Selected target for FATE {fateId} ({fateName}) remains {target.DistanceToHitbox:F0}m from its hitbox (attack reach {idle.Meters:F0}m); moving into range");

        var targetId = target.GameObjectId;
        var targetPosition = target.Position;
        var dest = targetPosition.OnMesh();
        var tolerance = target.HitboxRadius + (idle.Meters <= EngageMeleeReachMeters
            ? EngageMeleeApproachToleranceMeters
            : EngageRangedApproachToleranceMeters);
        var config = MovementConfig.Default.WithTolerance(tolerance);
        var reachMeters = idle.Meters;

        bool InRangeMovedOrGone()
        {
            if (PublicEvent.GetFateById(fateId) is not { State: FateState.Running }
             || Svc.Objects.LocalPlayer is not { } moving
             || !FateMobScanner.TrySurveyTargetedMob(fateId, moving.Position, out var live)
             || live.GameObjectId != targetId)
            {
                return true;
            }
            return live.DistanceToHitbox <= reachMeters
                || Vector3.Distance(live.Position, targetPosition) >= EngageTargetRepathMeters;
        }

        await WalkWithBossModParked(dest, config, InRangeMovedOrGone, $"engage-target-{fateId}");
    }

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
        if (arrivedFateId == fateId) arrivedFateId = null;
        sessionStuckFateIds.Add(fateId);
        LeaveFate(fateId);
    }

    // Keeps ComputeState from routing the ring the character still stands in back to Engaging, and
    // drops the post-KO return so a revive does not walk straight back into it.
    private void LeaveFate(uint fateId)
    {
        abandonedFateId = fateId;
        if (returnToFateId == fateId)
        {
            returnToFateId = null;
        }

        ClearEngageStall(fateId);
    }

    private PublicEvent? ResolveEngagementFate(PublicEvent? current)
    {
        // A 100% Collect row stays Running while it waits to pay out, but there is no combat left in it. Once
        // it clears, CurrentFate can also be briefly empty. In every other case the game value is authoritative.
        if (current is { State: FateState.Running }
         && abandonedFateId != current.Id
         && !(current.Rule == PublicEvent.FateRule.Collect && current.Progress >= 100))
        {
            if (arrivedFateId is { } arrivedId && arrivedId != current.Id)
                arrivedFateId = null;
            return current;
        }

        if (arrivedFateId is not { } fallbackId
         || abandonedFateId == fallbackId
         || PublicEvent.GetFateById(fallbackId) is not { State: FateState.Running, Progress: < 100 } fallback
         || Svc.Objects.LocalPlayer is not { } player
         || Vector3.Distance(player.Position, fallback.Position) > fallback.Radius)
        {
            arrivedFateId = null;
            return null;
        }

        return fallback;
    }

    private void RegisterDeath(uint fateId, string fateName, FateType fateType)
    {
        var deaths = session.CountDeath(fateId);
        var cfg = Plugin.Cfg;
        if (!cfg.AutoBlacklistOnDeaths)
        {
            Diag($"KO'd in FATE {fateId} ({fateName}); death {deaths} there this run (auto-blacklist off)");
            return;
        }

        if (deaths < cfg.AutoBlacklistDeathCount)
        {
            Diag($"KO'd in FATE {fateId} ({fateName}); death {deaths}/{cfg.AutoBlacklistDeathCount} there this run");
            return;
        }

        Diag($"KO'd in FATE {fateId} ({fateName}) {deaths} times this run; blacklisting it and moving on");
        FateBlacklist.Add(cfg, fateType, fateId);
        LeaveFate(fateId);
        Svc.Chat.Print($"[AFG] Died {deaths} times in {fateName}; it is now blacklisted. Remove it under Settings > FATE filters > Blacklist to grind it again.");
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
    private const string StayCloseToTargetModule = "BossMod.Autorotation.MiscAI.StayCloseToTarget";
    private const string StayCloseToTargetRangeTrack = "range";
    private const string StayCloseMeleeRangeOption = "4";
    private const string StayCloseRangedRangeOption = "25";

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
        private ulong watchedTargetId;
        private long targetOutOfRangeSinceMs;
        private Vector3 targetRangePlayerAnchor;
        private float targetRangeDistanceAnchor;
        private long targetApproachSuppressedUntilMs;

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
            ResetTargetRangeWatch();
            Restart();
        }

        public bool TargetOutOfRangeLongEnough(FateMobTarget target, Vector3 playerPosition)
        {
            var now = Environment.TickCount64;
            if (watchedTargetId != target.GameObjectId || targetOutOfRangeSinceMs == 0)
            {
                watchedTargetId = target.GameObjectId;
                targetOutOfRangeSinceMs = now;
                targetRangePlayerAnchor = playerPosition;
                targetRangeDistanceAnchor = target.DistanceToHitbox;
                targetApproachSuppressedUntilMs = target.IsCasting ? now + EngageTargetCastSettleMs : 0;
                return false;
            }

            // A growing target distance while the player is moving is BossMod deliberately retreating (or a
            // knockback), not a failed approach. Keep AFG's vnav correction out of the way until the mechanic
            // resolves. Any movement also restarts the short approach grace so two movement controllers never
            // fight each other while BossMod is already repositioning.
            if (Vector3.Distance(targetRangePlayerAnchor, playerPosition) >= EngageTargetMovementSampleMeters)
            {
                if (target.DistanceToHitbox >= targetRangeDistanceAnchor + EngageTargetRetreatIncreaseMeters)
                {
                    targetApproachSuppressedUntilMs = Math.Max(targetApproachSuppressedUntilMs,
                        now + EngageTargetRetreatHoldMs);
                }

                targetRangePlayerAnchor = playerPosition;
                targetRangeDistanceAnchor = target.DistanceToHitbox;
                targetOutOfRangeSinceMs = now;
            }

            // Most avoidable FATE mechanics are telegraphed by the selected mob's cast. Refreshing this on
            // every tick keeps the approach blocked until shortly after that cast completes.
            if (target.IsCasting)
            {
                targetApproachSuppressedUntilMs = Math.Max(targetApproachSuppressedUntilMs,
                    now + EngageTargetCastSettleMs);
            }

            if (now < targetApproachSuppressedUntilMs)
            {
                targetOutOfRangeSinceMs = now;
                return false;
            }

            return now - targetOutOfRangeSinceMs >= EngageTargetOutOfRangeGraceMs;
        }

        public void ResetTargetRangeWatch()
        {
            watchedTargetId = 0;
            targetOutOfRangeSinceMs = 0;
            targetRangePlayerAnchor = default;
            targetRangeDistanceAnchor = 0;
            targetApproachSuppressedUntilMs = 0;
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
