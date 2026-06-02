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
    private async Task Revive()
    {
        DisableTextAdvance();
        try { BossModIPC.Instance.ClearActive(); } catch { /* best-effort */ }

        var startZoneId = Svc.ClientState.TerritoryType;
        var startPos = Svc.Objects.LocalPlayer?.Position;

        var soloWait = Svc.Party.Length == 0;
        Status = soloWait ? "KO — releasing" : "KO — waiting for raise";
        Diag(soloWait ? "Solo KO: returning home." : "Party KO: waiting up to 30s for a raise.");

        var returningHome = soloWait;
        if (soloWait)
        {
            TriggerReturnHome();
        }
        else
        {
            var raiseDeadline = Environment.TickCount64 + RaiseWaitMs;
            var accepted = false;
            while (Environment.TickCount64 < raiseDeadline)
            {
                if (CancelToken.IsCancellationRequested) return;
                if (!IsPlayerKO())
                {
                    Diag("Raised by another player.");
                    accepted = true;
                    break;
                }
                if (TryAcceptRaisePrompt())
                {
                    Diag("Accepted raise prompt programmatically.");
                    accepted = true;
                    break;
                }
                await NextFrame(30);
            }

            if (!accepted)
            {
                Diag("No raise within window; falling back to return-home.");
                TriggerReturnHome();
                returningHome = true;
            }
        }

        await WaitForReviveOrTransition(reissueReturn: returningHome);

        // Revive can fail (window not ready, command dropped). Never chase a FATE while still
        // on the ground — bail and let the outer loop re-enter Unconscious and retry the release.
        if (IsPlayerKO())
        {
            Diag("Still KO after revive window; outer loop will retry.");
            return;
        }

        if (returnToFateId is { } retId
            && PublicEvent.GetFateById(retId) is { Progress: < 100 } retFate
            && Svc.ClientState.TerritoryType == zone.TerritoryId)
        {
            Status = "Returning to FATE";
            Diag($"Re-engaging FATE {retId} after revive.");
            await PrepareForTeleport($"revive-return-{retId}");
            if (CancelToken.IsCancellationRequested) return;
            var retPos = retFate.Position;
            var tp = new MoveOp(o => o.Teleport(zone.TerritoryId, retPos, allowSameZoneTeleport: true));
            if (await RunCancellable(tp, TeleportWatchdogMs, $"revive-return-tp-{retId}", IdleStallAbort(IdleStallTimeoutMs)))
            {
                var aeth = new MoveOp(o => o.Aethernet(zone.TerritoryId, retPos));
                await RunCancellable(aeth, AethernetWatchdogMs, $"revive-return-aethernet-{retId}", IdleStallAbort(IdleStallTimeoutMs));
            }
        }
        else if (Svc.ClientState.TerritoryType != startZoneId && startPos is not null)
        {
            Diag($"Revived in {Svc.ClientState.TerritoryType}, expected {startZoneId}; outer loop will retarget.");
        }
    }

    private void TriggerReturnHome()
    {
        if (!TryExecuteReviveCommand(ReviveParamReturn))
            Diag("Return-home command dispatch failed.");
    }

    private static bool TryExecuteReviveCommand(int param)
    {
        try
        {
            GameMain.ExecuteCommand((int)ReviveCommandId, param, 0, 0, 0);
            return true;
        }
        catch (Exception ex)
        {
            Svc.Log.Warning(ex, "[AFG] GameMain.ExecuteCommand revive failed");
            return false;
        }
    }

    private static bool TryAcceptRaisePrompt()
    {
        // No reliable raise-prompt addon check that's API-stable across patches; accept-attempt is a no-op
        // unless the prompt is showing, so we can spam it without side effects.
        return TryExecuteReviveCommand(ReviveParamAccept);
    }

    private async Task WaitForReviveOrTransition(bool reissueReturn)
    {
        var deadline = Environment.TickCount64 + ReleaseTransitionWaitMs;
        var nextReissueAt = Environment.TickCount64 + ReturnReissueMs;
        while (Environment.TickCount64 < deadline)
        {
            if (CancelToken.IsCancellationRequested) return;
            var stillKO = IsPlayerKO();
            var transitioning = Svc.Condition[ConditionFlag.BetweenAreas]
                             || Svc.Condition[ConditionFlag.BetweenAreas51];
            if (!stillKO && !transitioning)
            {
                await NextFrame(60); // settle so weakness statuses register
                return;
            }
            // The revive window isn't accepting input the instant Unconscious flips, so a single
            // Return fire can land on nothing. Keep re-issuing until the home teleport starts.
            if (reissueReturn && stillKO && !transitioning && Environment.TickCount64 >= nextReissueAt)
            {
                TriggerReturnHome();
                nextReissueAt = Environment.TickCount64 + ReturnReissueMs;
            }
            await NextFrame(30);
        }
        Diag("Revive transition timed out; outer loop will retry.");
    }

    private static bool IsPlayerKO() => Svc.Condition[ConditionFlag.Unconscious];

    private void StartFollowUpWatch(uint parentFateId)
    {
        var sheet = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Fate>();
        var parent = sheet?.GetRowOrDefault(parentFateId);
        if (parent?.HasFollowUp != true) return;
        if (followUpFateId != parentFateId)
            Diag($"Watching for follow-up to FATE {parentFateId} for {FollowUpWatchMs/1000}s");
        followUpFateId = parentFateId;
        followUpWatchUntilMs = Environment.TickCount64 + FollowUpWatchMs;
    }

    private bool ShouldWaitForFollowUp()
    {
        if (followUpFateId is not { } fateId) return false;
        var sheet = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Fate>();
        var row = sheet?.GetRowOrDefault(fateId);
        if (row is null) { followUpFateId = null; return false; }

        var locationId = row.Value.Location;
        if (locationId != 0 && PublicEvent.Fates is { } fates &&
            fates.Any(f => f.Id > fateId && sheet?.GetRowOrDefault(f.Id)?.Location == locationId))
        {
            Diag($"Follow-up FATE detected for parent {fateId}; resuming");
            followUpFateId = null;
            return false;
        }

        if (Environment.TickCount64 >= followUpWatchUntilMs)
        {
            followUpFateId = null;
            return false;
        }
        return true;
    }

    private async Task ActivateFate(PublicEvent fate)
    {
        Status = $"Activating {fate.Name}";
        Diag($"FATE {fate.Id} in Preparation, walking to MotivationNpc {fate.MotivationNpcId:X}");

        var npcDeadline = Environment.TickCount64 + NpcSpawnTimeoutMs;
        while (Environment.TickCount64 < npcDeadline)
        {
            if (CancelToken.IsCancellationRequested) return;
            // Re-resolve each frame: clib's getters NRE if the FATE despawns during the NPC-spawn wait.
            if (PublicEvent.GetFateById(fate.Id) is not { } live) return;
            fate = live;
            if (fate.State == FateState.Running) return;
            if (fate.MotivationNpc?.IsTargetable == true) break;
            await NextFrame(30);
        }

        if (PublicEvent.GetFateById(fate.Id) is not { } refreshed) return;
        fate = refreshed;
        if (fate.State == FateState.Running) return;
        var npc = fate.MotivationNpc;
        if (npc is null || !npc.IsTargetable)
        {
            Diag($"NPC for FATE {fate.Id} never spawned within {NpcSpawnTimeoutMs/1000}s; blacklisting for session");
            sessionStuckFateIds.Add(fate.Id);
            return;
        }

        try
        {
            var activateLabel = $"Activating {fate.Name}";
            var npcPos = npc.Position;
            var move = new MoveOp(o => o.Move(zone.TerritoryId, npcPos,
                MovementConfig.InteractRange,
                allowTeleportIfFaster: false,
                stopCondition: () => { Status = activateLabel; return fate.State == FateState.Running; },
                allowAethernetWithinTerritory: false));
            await RunCancellable(move, ActivateMoveWatchdogMs, $"activate-move-{fate.Id}");

            if (fate.State == FateState.Running) return;
            if (Svc.Condition[ConditionFlag.Mounted]) await DismountViaOp($"dismount-activate-{fate.Id}");

            var interact = new MoveOp(o => o.Interact(npc,
                waitUntil: () => fate.State == FateState.Running,
                skip: UiSkipOptions.Talk | UiSkipOptions.YesNo));
            await RunCancellable(interact, NpcSpawnTimeoutMs, $"activate-interact-{fate.Id}");
        }
        catch (Exception ex)
        {
            Diag($"ActivateFate caught: {ex.Message}");
        }
    }

    // BossMod navigates around terrain only when an obstacle map is active; regenerate if we engaged
    // without one (death-teleport return, follow-up, or a FATE that popped on us).
    private async Task EnsureObstacleMapForEngage(PublicEvent fate)
    {
        if (!BossModIPC.Instance.IsAvailable) return;
        if (!fate.IsOnMap) return;
        if (BossModIPC.Instance.HasTempObstacleMap()) return;
        await GenerateObstacleMap(fate);
    }

    private enum MoveStopReason { None, StuckRetry, StuckTeleport, StuckInCombat, HigherPriority, NpcSpawned, FateInvalid }

    private async Task GenerateObstacleMap(PublicEvent fate)
    {
        if (obstacleMapBlacklist.Contains(fate.Id)) return;
        if (Plugin.Cfg.RuntimeBadObstacleMaps.Contains(fate.Id)) return;
        if (!BossModIPC.Instance.IsAvailable) return;
        if (!NavmeshIPC.Instance.IsReady()) return;

        var safe = NavmeshIPC.Instance.NearestPointReachable(fate.Position, 5f, 5f);
        var anchor = safe ?? fate.Position;
        var margin = safe.HasValue ? Vector3.Distance(fate.Position, safe.Value) : 0f;
        var radius = Math.Max(fate.Radius + margin, 10f);

        if (!BossModIPC.Instance.GenerateObstacleMap(anchor, radius, writeToFile: false))
        {
            Diag($"Obstacle map generate IPC returned false for FATE {fate.Id}");
            return;
        }

        var deadline = Environment.TickCount64 + ObstacleMapGenTimeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (CancelToken.IsCancellationRequested) return;
            var status = BossModIPC.Instance.GetObstacleMapStatus();
            if (status is null) return;
            if (status == TaskStatus.RanToCompletion) break;
            if (status == TaskStatus.Faulted || status == TaskStatus.Canceled)
            {
                Diag($"Obstacle map generation {status} for FATE {fate.Id}");
                return;
            }
            await NextFrame();
        }

        if (BossModIPC.Instance.EvaluateTempMapQualityIsBad())
        {
            Diag($"Obstacle map quality too poor for FATE {fate.Id}; clearing and adding to runtime blacklist");
            Plugin.Cfg.RuntimeBadObstacleMaps.Add(fate.Id);
            BossModIPC.Instance.ClearTempObstacleMap();
        }
    }

}
