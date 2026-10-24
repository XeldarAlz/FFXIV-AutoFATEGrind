using AutoFateGrind.Core.Game.Ops;
using AutoFateGrind.Core.Localization;
using Dalamud.Bindings.ImGui;
using ECommons.DalamudServices;
using Glasswork.Backend;

namespace AutoFateGrind.Windows.Sections.Config;

internal static class RepairSettings
{
    private const int ThresholdMin = 5;
    private const int ThresholdMax = 80;
    private const string PercentFormat = "%d%%";

    private static readonly RepairMode[] repairModes =
        [RepairMode.SelfThenNpc, RepairMode.SelfOnly, RepairMode.NpcOnly];

    private static readonly SettingsControls.Choices.Choice[] repairModeChoices =
    [
        new(L.Settings.RepairSelfThenNpcName, L.Settings.RepairSelfThenNpcDetail),
        new(L.Settings.RepairSelfOnlyName, L.Settings.RepairSelfOnlyDetail),
        new(L.Settings.RepairNpcOnlyName, L.Settings.RepairNpcOnlyDetail),
    ];

    public static void Draw(Configuration cfg)
    {
        DrawTriggerGroup(cfg);
        if (!cfg.AutoRepair)
        {
            return;
        }

        DrawSourceGroup(cfg);
    }

    private static void DrawTriggerGroup(Configuration cfg)
    {
        if (Gw.BeginSection(Loc.T(L.Settings.RepairTrigger)))
        {
            DrawTriggerRows(cfg);
        }

        Gw.EndSection();
    }

    private static void DrawTriggerRows(Configuration cfg)
    {
        var autoRepair = cfg.AutoRepair;
        if (SettingsControls.ToggleRow(L.Settings.AutoRepair, L.Settings.AutoRepairHelp, ref autoRepair))
        {
            cfg.AutoRepair = autoRepair;
            cfg.SaveDebounced();
        }

        if (!cfg.AutoRepair)
        {
            SettingsControls.Note(Loc.T(L.Settings.AutoRepairOff));
            return;
        }

        var threshold = cfg.AutoRepairThresholdPct;
        if (SettingsControls.SliderRow(L.Settings.RepairThreshold, L.Settings.RepairThresholdHelp, ref threshold, ThresholdMin, ThresholdMax, PercentFormat))
        {
            cfg.AutoRepairThresholdPct = threshold;
            cfg.SaveDebounced();
        }
    }

    private static void DrawSourceGroup(Configuration cfg)
    {
        if (Gw.BeginSection(Loc.T(L.Settings.RepairSource)))
        {
            DrawSourceRows(cfg);
        }

        Gw.EndSection();
    }

    private static void DrawSourceRows(Configuration cfg)
    {
        var selected = Math.Max(0, Array.IndexOf(repairModes, cfg.RepairMode));
        if (SettingsControls.Choices.DrawRow(L.Settings.RepairSource, L.Settings.RepairSourceHelp, repairModeChoices, ref selected))
        {
            cfg.RepairMode = repairModes[selected];
            cfg.SaveDebounced();
        }

        if (cfg.RepairMode == RepairMode.SelfOnly)
        {
            return;
        }

        SettingsControls.TitleRow(L.Settings.CustomNpc, L.Settings.CustomNpcHelp);
        DrawCustomRepairNpc(cfg);
        SettingsControls.Note(Loc.T(L.Settings.NpcNote));
    }

    private static void DrawCustomRepairNpc(Configuration cfg)
    {
        var npc = cfg.PreferredRepairNpc;
        if (npc is not null)
        {
            SettingsControls.Note(Loc.T(L.Settings.NpcSet, npc.Name, npc.TerritoryId), Frame.Look.Success);
        }
        else
        {
            SettingsControls.Note(Loc.T(L.Settings.NpcNone));
        }

        if (Gw.Button(Loc.T(L.Settings.SetFromTarget), ButtonStyle.Prominent, ButtonSize.Small))
        {
            CaptureTarget(cfg);
        }

        if (npc is null)
        {
            return;
        }

        ImGui.SameLine();
        if (Gw.Button(Loc.T(L.Common.Clear), ButtonStyle.Destructive, ButtonSize.Small))
        {
            cfg.PreferredRepairNpc = null;
            cfg.SaveDebounced();
        }
    }

    private static void CaptureTarget(Configuration cfg)
    {
        var captured = RepairOps.CaptureCurrentTargetAsRepairNpc();
        if (captured is null)
        {
            Svc.Chat.PrintError(Loc.T(L.Settings.NoTargetChat));
            return;
        }

        cfg.PreferredRepairNpc = captured;
        GameTextGlyphs.Add(captured.Name);
        cfg.SaveDebounced();
        Svc.Chat.Print(Loc.T(L.Settings.NpcSetChat, captured.Name, captured.TerritoryId));
    }
}
