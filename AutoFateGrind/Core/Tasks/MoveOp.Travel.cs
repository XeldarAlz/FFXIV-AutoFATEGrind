using AutoFateGrind.Core.Game.Ops;
using AutoFateGrind.Core.Ipc;
using clib.Enums;
using clib.Extensions;
using clib.TaskSystem;
using clib.Utils;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Network;
using FFXIVClientStructs.FFXIV.Client.UI;
using System.Numerics;
using System.Threading.Tasks;
using OnDispose = clib.Services.OnDispose;

namespace AutoFateGrind.Core.Tasks;

// The library's own movement methods live on TaskBase, which installs two native hooks per instance for its
// straight-line walk. These are the same travel, teleport, aethernet and interaction steps over vnavmesh alone, so a
// session can create an operation per move for hours without exhausting the hook allocator.
internal sealed partial class MoveOp
{
    private const float DefaultArriveMeters = 3f;
    private const float TeleportCastSeconds = 5f;
    private const uint FlyingMountRouletteGeneralActionId = 24;
    private const int InteractAttempts = 5;

    private static NavmeshIPC Navmesh => NavmeshIPC.Instance;

    private static IPlayerCharacter? Player => Svc.Objects.LocalPlayer;

    private async Task Navigate(Vector3 destination, MovementConfig config, Func<bool>? stopCondition)
    {
        await WaitUntil(() => Player.Available, "WaitingForPlayer");
        var tolerance = Math.Max(config.Tolerance ?? 0f, Navmesh.GetTolerance());
        if (Player.WithinRange(destination, tolerance))
        {
            return;
        }

        if (config.Movement.HasFlag(MovementOptions.Mount) || config.Movement.HasFlag(MovementOptions.Fly))
        {
            await Mount();
        }

        Status = "Waiting for Navmesh";
        await WaitUntil(() => Navmesh.IsReady() || Navmesh.BuildProgress() >= 0f, "WaitForBuildStart");
        await WaitWhile(() => Navmesh.BuildProgress() >= 0f, "BuildMesh");
        ErrorIf(!Navmesh.IsReady(), $"Failed to build navmesh for zone #{Svc.ClientState.TerritoryType}");
        await WaitWhile(Navmesh.IsPathfinding, "WaitingForInProgressCalls");
        CancelToken.ThrowIfCancellationRequested();

        var fly = Player.InFlight || (config.Movement.HasFlag(MovementOptions.Fly) && Control.CanFly);
        ErrorIf(!Navmesh.PathfindAndMoveCloseTo(destination, fly, config.Tolerance ?? DefaultArriveMeters),
            $"Failed to start pathfinding to destination [#{Svc.ClientState.TerritoryType} - {destination}]");
        Status = $"Moving to {destination}";

        // Registered with this operation, so cancelling it stops the path at once instead of leaving vnav walking.
        using (new OnDispose(Navmesh.Stop))
        {
            await NextFrame();
            await WaitWhile(
                () => !Player.WithinRange(destination, tolerance)
                    && stopCondition?.Invoke() != true
                    && (Navmesh.IsMovePending() || Navmesh.IsRunning()),
                "Navigate");
            if (stopCondition?.Invoke() == true)
            {
                await StopPathfinding();
            }

            if (config.Movement.HasFlag(MovementOptions.Dismount) && Player.WithinRange(destination, tolerance))
            {
                await Dismount();
            }
        }
    }

    private async Task TeleportToTerritory(uint territoryId, Vector3 destination, bool allowSameZoneTeleport)
    {
        if (!allowSameZoneTeleport && Svc.ClientState.TerritoryType == territoryId)
        {
            return;
        }

        await StopPathfinding();
        await WaitWhile(() => Player.IsUiFading, "WaitForUiUnfade");
        var aetheryteId = Coords.FindPrimaryAetheryte(Coords.FindClosestAetheryte(territoryId, destination).GetValueOrDefault());
        ErrorIf(aetheryteId == 0, $"Failed to find an aetheryte in territory {territoryId}");
        var aetheryte = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Aetheryte>().GetRow(aetheryteId);
        if (Svc.ClientState.TerritoryType != aetheryte.Territory.RowId || allowSameZoneTeleport)
        {
            Status = $"Teleporting to {aetheryte.PlaceName.Value.Name}";
            await CastTeleport(aetheryteId);
        }

        if (Svc.ClientState.TerritoryType != territoryId)
        {
            await UseAethernet(territoryId, destination);
        }

        ErrorIf(Svc.ClientState.TerritoryType != territoryId,
            $"Failed to reach territory (exp: {territoryId}, act: {Svc.ClientState.TerritoryType})");
    }

    private async Task CastTeleport(uint aetheryteId)
    {
        var arrived = false;
        while (!arrived)
        {
            await StopPathfinding();
            await WaitWhile(() => Player.IsBusy, "WaitForTeleportReady");
            ErrorIf(!ActionManager.Teleport(aetheryteId), $"Failed to teleport to {aetheryteId}");
            arrived = await WaitForTeleportOutcome();
        }
    }

    // True once the destination loaded behind a UI fade. False when the cast stopped short with no fade, which is an
    // interrupted cast, so the caller casts again.
    private async Task<bool> WaitForTeleportOutcome()
    {
        var sawCast = false;
        var sawUiFade = false;
        while (true)
        {
            var fading = Player.IsUiFading;
            var casting = Player?.IsCasting == true;
            sawCast |= casting;
            sawUiFade |= fading;
            if (sawUiFade && !fading)
            {
                await WaitUntil(() => GameMain.IsTerritoryLoaded && Player.Interactable, "WaitTransportFinish");
                return true;
            }

            if (sawCast && !casting && !sawUiFade && ActionManager.GetCastAction().Elapsed != TeleportCastSeconds)
            {
                return false;
            }

            await NextFrame();
        }
    }

    private async Task UseAethernet(uint territoryId, Vector3 destination)
    {
        var sourceId = Coords.FindClosestAetheryte(Svc.ClientState.TerritoryType, Player!.Position).GetValueOrDefault();
        var destinationId = Coords.FindClosestAetheryte(territoryId, destination).GetValueOrDefault();
        if (sourceId == 0 || destinationId == 0 || sourceId == destinationId)
        {
            return;
        }

        var primaryId = Coords.FindPrimaryAetheryte(sourceId);
        if (primaryId == 0 || primaryId != Coords.FindPrimaryAetheryte(destinationId))
        {
            return;
        }

        var (objectId, position) = Coords.FindAetheryte(sourceId);
        if (objectId == 0)
        {
            (objectId, position) = Coords.FindAetheryte(primaryId);
        }

        if (objectId == 0)
        {
            return;
        }

        Status = $"Interacting with aethernet to get to [{territoryId}]";
        await Navigate(position, MovementConfig.Default.WithTolerance(InteractRange.Aetheryte), null);
        if (Player.Mounted)
        {
            await Dismount();
        }

        ErrorIf(!TargetSystem.InteractWith(objectId), "Failed to interact with aetheryte");
        var isMainAetheryte = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Aetheryte>().GetRow(sourceId).IsAetheryte;
        var menu = isMainAetheryte ? AfgConstants.AddonNames.SelectString : AfgConstants.AddonNames.TelepotTown;
        await WaitUntilSkipping(() => NpcInteraction.AddonOpen(menu), isMainAetheryte ? UiSkipOptions.Talk : UiSkipOptions.None);
        PacketDispatcher.TeleportToAethernet(sourceId, destinationId);
        await WaitUntilThenFalse(() => Svc.Condition[ConditionFlag.BetweenAreas], "TeleportStart");
        await WaitUntil(
            () => Svc.ClientState.TerritoryType == territoryId && GameMain.IsTerritoryLoaded && Player.Interactable,
            "TeleportFinish");
    }

    private async Task InteractWith(IGameObject target, Func<bool>? waitUntil, UiSkipOptions skip)
    {
        ErrorIf(waitUntil is null && skip != UiSkipOptions.None, "Skip arguments provided but no wait condition");
        if (!target.IsInInteractRange())
        {
            // The library walked this stretch in a straight line, which is what needed the hooks.
            await Navigate(target.Position, MovementConfig.Default.WithTolerance(InteractRange.EventObj), target.IsInInteractRange);
        }

        Status = $"Interacting with {target.Name}";
        await WaitWhile(() => Player.IsJumping, "WaitForAbleToInteract");
        for (var attempt = 0; attempt < InteractAttempts; attempt++)
        {
            if (TargetSystem.InteractWith(target.GameObjectId))
            {
                if (waitUntil is not null)
                {
                    await WaitUntilSkipping(waitUntil, skip);
                }

                return;
            }

            await NextFrame();
        }

        Error($"Failed to interact with object after {InteractAttempts} tries");
    }

    private async Task WaitUntilSkipping(Func<bool> condition, UiSkipOptions skip)
    {
        while (!condition())
        {
            if (skip.HasFlag(UiSkipOptions.Talk) && NpcInteraction.AddonOpen(AfgConstants.AddonNames.Talk))
            {
                AddonTalk.Progress();
            }

            if (skip.HasFlag(UiSkipOptions.YesNo) && NpcInteraction.AddonOpen(AfgConstants.AddonNames.SelectYesno))
            {
                AddonSelectYesno.Yes();
            }

            await NextFrame();
        }
    }

    private async Task Mount()
    {
        if (!Player.CanMount)
        {
            return;
        }

        Status = "Mounting";
        while (!Player.Mounted)
        {
            if (!Player.IsBusy && !ActionManager.IsActionInUse(ActionType.GeneralAction, FlyingMountRouletteGeneralActionId))
            {
                ActionManager.UseAction(ActionType.GeneralAction, FlyingMountRouletteGeneralActionId);
            }

            await NextFrame();
        }
    }

    private async Task Dismount()
    {
        if (Player is null || !Player.Mounted)
        {
            return;
        }

        if (Player.InFlight)
        {
            if (Navmesh.NearestPointReachable(Player.Position) is { } landing)
            {
                await Navigate(landing, MovementConfig.Everything, null);
            }
            else
            {
                Warning($"No nearest landable point found from {Player.Position}. Dismounting may fail");
            }
        }

        Status = "Dismounting";
        while (Player.Mounted)
        {
            if (Player.InFlight && !Player.IsAirDismountable)
            {
                ActionManager.UseAction(ActionType.GeneralAction, AutoCommon.DismountGeneralActionId);
            }
            else if (Player.InFlight)
            {
                GameMain.ExecuteLocationCommand(LocationCommandFlag.Dismount, Player!.Position, (int)Player.PackedRotation);
            }
            else
            {
                GameMain.ExecuteCommand(CommandFlag.Dismount, 1);
            }

            await NextFrame();
        }
    }

    private async Task StopPathfinding()
    {
        Navmesh.Stop();
        if (!Navmesh.IsMovePending())
        {
            return;
        }

        await WaitWhile(Navmesh.IsMovePending, "WaitForPathfindingStop");
        Navmesh.Stop();
    }
}
