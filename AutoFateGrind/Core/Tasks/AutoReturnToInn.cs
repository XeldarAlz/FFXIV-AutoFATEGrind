using AutoFateGrind.Core.Game.Ops;
using AutoFateGrind.Core.Game.Player;
using clib.TaskSystem;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using System.Numerics;
using System.Threading.Tasks;

namespace AutoFateGrind.Core.Tasks;

public sealed class AutoReturnToInn : AutoCommon
{
    private const int   TeleportWatchdogMs        = 60_000;
    private const int   WalkWatchdogMs            = 120_000;
    private const int   NavmeshReadyWaitMs        = 60_000;
    private const int   EnterInnTimeoutMs         = 60_000;
    private const int   ApproachWatchdogMs        = 25_000;
    private const int   InteractWatchdogMs        = 8_000;
    private const int   DialogPollMs              = 250;
    private const int   InteractRetryMs           = 500;
    private const float ArrivalTolerance          = 3.5f;
    private const float InnkeeperApproachDistance = 5f;
    private const float InnkeeperStepTolerance    = 2.5f;

    private readonly record struct Inn(uint CityTerritory, uint InnTerritory, uint InnkeeperDataId, Vector3 InnkeeperPos, string City);

    private static unsafe Inn Resolve()
    {
        var ps = PlayerState.Instance();
        var gc = ps is null ? 0 : ps->GrandCompany;
        return gc switch
        {
            GrandCompanyId.TwinAdder      => new Inn(132, 179, 1000102, new Vector3(25.6627f, -8f, 99.74237f), "Gridania"),
            GrandCompanyId.ImmortalFlames => new Inn(130, 178, 1001976, new Vector3(28.85994f, 6.999999f, -80.12716f), "Ul'dah"),
            _                             => new Inn(128, 177, 1000974, new Vector3(15.42688f, 39.99999f, 12.466553f), "Limsa Lominsa"),
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

        await WaitForNavmeshReady(NavmeshReadyWaitMs);
        if (CancelToken.IsCancellationRequested) return;

        await SafeDismount("inn-dismount");

        Status = "Walking to the innkeeper";
        var walk = new MoveOp(o => o.Move(inn.CityTerritory, inn.InnkeeperPos,
            MovementConfig.Everything.WithTolerance(ArrivalTolerance),
            allowTeleportIfFaster: false,
            stopCondition: () => CancelToken.IsCancellationRequested,
            allowAethernetWithinTerritory: true));
        await RunCancellable(walk, WalkWatchdogMs, "inn-walk", StuckDetector.MoveStallAbort("inn-walk"));
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
            if (NpcInteraction.DriveDialog()) { await DelayMs(DialogPollMs); continue; }

            var npc = RepairOps.FindObjectByBaseId(inn.InnkeeperDataId);
            if (npc is null) { await DelayMs(DialogPollMs); continue; }

            var player = Svc.Objects.LocalPlayer;
            if (player is not null && Vector3.Distance(player.Position, npc.Position) > InnkeeperApproachDistance)
            {
                var step = new MoveOp(o => o.Move(inn.CityTerritory, npc.Position,
                    MovementConfig.Everything.WithTolerance(InnkeeperStepTolerance),
                    allowTeleportIfFaster: false,
                    stopCondition: () => CancelToken.IsCancellationRequested,
                    allowAethernetWithinTerritory: false));
                await RunCancellable(step, ApproachWatchdogMs, "inn-approach", StuckDetector.MoveStallAbort("inn-approach"));
                continue;
            }

            var interact = new MoveOp(o => o.Interact(npc, waitUntil: NpcInteraction.DialogAddonOpen, skip: UiSkipOptions.Talk));
            await RunCancellable(interact, InteractWatchdogMs, "inn-interact");
            if (interact.Fault is { } fault) Diag($"Innkeeper interaction failed: {fault.Message}; retrying");
            await DelayMs(InteractRetryMs);
        }
        Diag("Return to inn: timed out before entering the inn room.");
    }
}
