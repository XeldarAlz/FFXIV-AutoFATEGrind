using AutoFateGrind.Core.Game;
using AutoFateGrind.Core.Ipc;
using clib.TaskSystem;
using Dalamud.Game.ClientState.Conditions;
using ECommons;
using ECommons.DalamudServices;
using ECommons.Throttlers;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Numerics;
using System.Threading.Tasks;

namespace AutoFateGrind.Core.Tasks;

public sealed class AutoReturnToInn : AutoCommon
{
    private const int TeleportWatchdogMs = 60_000;
    private const int WalkWatchdogMs     = 120_000;
    private const int DismountWatchdogMs = 30_000;
    private const int NavmeshReadyWaitMs = 60_000;
    private const int EnterInnTimeoutMs  = 60_000;
    private const float ArrivalTolerance = 3.5f;

    private readonly record struct Inn(uint CityTerritory, uint InnTerritory, uint InnkeeperDataId, Vector3 InnkeeperPos, string City);

    private static unsafe Inn Resolve()
    {
        var ps = PlayerState.Instance();
        var gc = ps is null ? 0 : ps->GrandCompany;
        return gc switch
        {
            2 => new Inn(132, 179, 1000102, new Vector3(25.6627f, -8f, 99.74237f), "Gridania"),
            3 => new Inn(130, 178, 1001976, new Vector3(28.85994f, 6.999999f, -80.12716f), "Ul'dah"),
            _ => new Inn(128, 177, 1000974, new Vector3(15.42688f, 39.99999f, 12.466553f), "Limsa Lominsa"),
        };
    }

    protected override async Task Execute()
    {
        var inn = Resolve();
        Diag($"Return to inn: city {inn.City} (territory {inn.CityTerritory}), inn territory {inn.InnTerritory}.");

        if (Svc.ClientState.TerritoryType == inn.InnTerritory)
        {
            Diag("Already inside the inn; nothing to do.");
            return;
        }

        Svc.Chat.Print($"[AFG] Run complete — retiring to the inn in {inn.City}.");

        if (Svc.ClientState.TerritoryType != inn.CityTerritory)
        {
            var reached = false;
            await RunWithStatusPinned($"Teleporting to {inn.City}",
                async () => reached = await TeleportToTerritory(inn.CityTerritory, Vector3.Zero, "inn-teleport", TeleportWatchdogMs));
            if (!reached)
            {
                Diag($"Return to inn aborted: could not reach {inn.City} (still in {Svc.ClientState.TerritoryType}).");
                return;
            }
        }

        await WaitForNavmeshReady();
        if (CancelToken.IsCancellationRequested) return;

        if (Svc.Condition[ConditionFlag.Mounted])
            await RunCancellable(new MoveOp(o => o.DismountNow()), DismountWatchdogMs, "inn-dismount");

        Status = "Walking to the innkeeper";
        var walk = new MoveOp(o => o.Move(inn.CityTerritory, inn.InnkeeperPos,
            MovementConfig.Everything.WithTolerance(ArrivalTolerance),
            allowTeleportIfFaster: false,
            stopCondition: () => CancelToken.IsCancellationRequested,
            allowAethernetWithinTerritory: true));
        await RunCancellable(walk, WalkWatchdogMs, "inn-walk", IdleStallAbort(IdleStallTimeoutMs));
        if (CancelToken.IsCancellationRequested) return;

        await EnterInn(inn);
    }

    private async Task EnterInn(Inn inn)
    {
        Status = $"Entering the inn in {inn.City}";
        var deadline = Environment.TickCount64 + EnterInnTimeoutMs;
        while (Environment.TickCount64 < deadline && !CancelToken.IsCancellationRequested)
        {
            if (Svc.ClientState.TerritoryType == inn.InnTerritory)
            {
                Diag("Reached the inn.");
                return;
            }

            // A dialog is up → drive it (SelectString -> Yes -> Talk). Throttled so we don't spam callbacks.
            if (DriveInnDialogs()) { await NextFrame(250); continue; }

            var npc = RepairOps.FindObjectByBaseId(inn.InnkeeperDataId);
            if (npc is null) { await NextFrame(250); continue; }

            var player = Svc.Objects.LocalPlayer;
            if (player is not null && Vector3.Distance(player.Position, npc.Position) > 5f)
            {
                var step = new MoveOp(o => o.Move(inn.CityTerritory, npc.Position,
                    MovementConfig.Everything.WithTolerance(2.5f),
                    allowTeleportIfFaster: false,
                    stopCondition: () => CancelToken.IsCancellationRequested,
                    allowAethernetWithinTerritory: false));
                await RunCancellable(step, 25_000, "inn-approach", IdleStallAbort(IdleStallTimeoutMs));
                continue;
            }

            var interact = new MoveOp(o => o.Interact(npc, waitUntil: null, skip: UiSkipOptions.Talk));
            await RunCancellable(interact, 8_000, "inn-interact");
            await NextFrame(500);
        }
        Diag("Return to inn: timed out before entering the inn room.");
    }

    // Returns true if a relevant dialog was present this tick, so the caller waits instead of re-interacting.
    private static unsafe bool DriveInnDialogs()
    {
        if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("SelectString", out var ss) && GenericHelpers.IsAddonReady(ss))
        {
            if (EzThrottler.Throttle("AFG.InnSelectString", 600))
            {
                var m = new AddonMaster.SelectString((nint)ss);
                if (m.EntryCount > 0) m.Entries[0].Select();   // first entry = retire to your private chambers
            }
            return true;
        }
        if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("SelectYesno", out var yn) && GenericHelpers.IsAddonReady(yn))
        {
            if (EzThrottler.Throttle("AFG.InnSelectYesno", 600))
                new AddonMaster.SelectYesno((nint)yn).Yes();
            return true;
        }
        if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("Talk", out var tk) && GenericHelpers.IsAddonReady(tk))
        {
            if (EzThrottler.Throttle("AFG.InnTalk", 400))
                new AddonMaster.Talk((nint)tk).Click();
            return true;
        }
        return false;
    }

    private async Task WaitForNavmeshReady()
    {
        if (NavmeshIPC.Instance.IsReady()) return;
        var deadline = Environment.TickCount64 + NavmeshReadyWaitMs;
        while (!NavmeshIPC.Instance.IsReady())
        {
            if (CancelToken.IsCancellationRequested) return;
            if (Environment.TickCount64 >= deadline)
            {
                Diag($"WAIT TIMEOUT: navmesh not ready within {NavmeshReadyWaitMs / 1000}s; proceeding anyway");
                return;
            }
            var progress = NavmeshIPC.Instance.BuildProgress();
            Status = progress is >= 0f and <= 1f
                ? $"Please wait — navmesh is loading ({progress * 100f:F0}%)"
                : "Please wait — navmesh is loading…";
            await NextFrame(120);
        }
    }
}
