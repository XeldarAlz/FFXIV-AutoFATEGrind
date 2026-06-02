using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Lumina.Excel.Sheets;

namespace AutoFateGrind.Core.Debug;

internal static unsafe class TargetDumper
{
    public static void Dump()
    {
        var territoryId = Svc.ClientState.TerritoryType;
        var territoryName = Svc.Data.GetExcelSheet<TerritoryType>()
            ?.GetRowOrDefault(territoryId)
            ?.PlaceName.Value.Name.ToString() ?? "?";

        Svc.Chat.Print($"{AfgConstants.LogPrefix} Territory: {territoryId} ({territoryName})");

        var target = TargetSystem.Instance()->Target;
        if (target == null)
        {
            Svc.Chat.Print($"{AfgConstants.LogPrefix} No target. Click an NPC or FATE marker first, then re-run /afg target.");
            return;
        }

        var baseId = target->BaseId;
        var name = target->NameString;
        var residentName = Svc.Data.GetExcelSheet<ENpcResident>()
            ?.GetRowOrDefault(baseId)?.Singular.ToString() ?? name;

        Svc.Chat.Print($"{AfgConstants.LogPrefix} Target: BaseId={baseId}  Name=\"{residentName}\"");
        Svc.Log.Info($"[TargetDumper] territory={territoryId} BaseId={baseId} name='{residentName}'");
    }
}
