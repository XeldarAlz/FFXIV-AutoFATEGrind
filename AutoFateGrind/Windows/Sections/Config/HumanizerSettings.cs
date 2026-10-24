using AutoFateGrind.Core.Game.Ops;
using AutoFateGrind.Core.Ipc;
using AutoFateGrind.Core.Localization;
using AutoFateGrind.Core.Zones;
using Dalamud.Bindings.ImGui;
using ECommons.DalamudServices;
using Glasswork;
using Glasswork.Backend;

namespace AutoFateGrind.Windows.Sections.Config;

internal static class HumanizerSettings
{
    private const long ModuleCheckIntervalMs = 5_000;
    private const int ReactionMaxSec = 30;
    private const int FatesBetweenMin = 1;
    private const int FatesBetweenMax = 100;
    private const int BreakMinutesMin = 1;
    private const int BreakMinutesMax = 60;
    private const int PauseMaxSec = 60;
    private const int WanderMetersMin = 5;
    private const int WanderMetersMax = 200;

    private static readonly HumanizerBreakActivity[] breakActivities =
        [HumanizerBreakActivity.Wander, HumanizerBreakActivity.IdleAtSpot];

    private static readonly SettingsControls.Choices.Choice[] breakActivityChoices =
    [
        new(L.Settings.BreakActivityWanderName, L.Settings.BreakActivityWanderDetail),
        new(L.Settings.BreakActivityIdleName, L.Settings.BreakActivityIdleDetail),
    ];

    private static readonly string[] delayLabels = new string[3];
    private static readonly string[] roomLabels = new string[4];

    private static string? moduleCheckPreset;
    private static long moduleCheckTick;
    private static bool moduleMissing;

    public static void Draw(Configuration cfg)
    {
        DrawPacingGroup(cfg);
        DrawCombatMovementGroup(cfg);
        DrawBreaksGroup(cfg);
        if (!cfg.HumanizerEnabled)
        {
            return;
        }

        var idle = cfg.HumanizerBreakActivity == HumanizerBreakActivity.IdleAtSpot;
        if (idle)
        {
            DrawIdleSpotsGroup(cfg);
        }
        else
        {
            DrawWanderingGroup(cfg);
        }

        if (!idle || cfg.HumanizerIdleSpots.Count == 0)
        {
            DrawCitiesGroup(cfg);
        }
    }

    private static void DrawPacingGroup(Configuration cfg)
    {
        if (Gw.BeginSection(Loc.T(L.Settings.Pacing)))
        {
            DrawPacingRows(cfg);
        }

        Gw.EndSection();
    }

    private static void DrawPacingRows(Configuration cfg)
    {
        var pacing = cfg.PacingEnabled;
        if (SettingsControls.ToggleRow(L.Settings.PacingEnable, L.Settings.PacingEnableHelp, ref pacing))
        {
            cfg.PacingEnabled = pacing;
            cfg.SaveDebounced();
        }

        if (!cfg.PacingEnabled)
        {
            SettingsControls.Note(Loc.T(L.Settings.PacingOff));
            return;
        }

        var reactionMin = cfg.PacingReactionMinSec;
        var reactionMax = cfg.PacingReactionMaxSec;
        if (SettingsControls.RangeRow(L.Settings.ReactionDelay, L.Settings.ReactionDelayHelp, ref reactionMin, ref reactionMax, 0, ReactionMaxSec, Loc.T(L.Settings.SecondsFormat)))
        {
            cfg.PacingReactionMinSec = reactionMin;
            cfg.PacingReactionMaxSec = reactionMax;
            cfg.SaveDebounced();
        }

        var pickVariety = cfg.PacingPickVariety;
        if (SettingsControls.ToggleRow(L.Settings.PickVariety, L.Settings.PickVarietyHelp, ref pickVariety))
        {
            cfg.PacingPickVariety = pickVariety;
            cfg.SaveDebounced();
        }
    }

    private static void DrawCombatMovementGroup(Configuration cfg)
    {
        if (Gw.BeginSection(Loc.T(L.Settings.CombatMovement)))
        {
            DrawCombatMovementRows(cfg);
        }

        Gw.EndSection();
    }

    private static void DrawCombatMovementRows(Configuration cfg)
    {
        var enabled = cfg.CombatMovementEnabled;
        if (SettingsControls.ToggleRow(L.Settings.CombatMovementEnable, L.Settings.CombatMovementEnableHelp, ref enabled))
        {
            cfg.CombatMovementEnabled = enabled;
            cfg.SaveDebounced();
        }

        if (!cfg.CombatMovementEnabled)
        {
            SettingsControls.Note(Loc.T(L.Settings.CombatMovementOff));
            return;
        }

        ResolveMovementLabels();

        var dodgeMin = cfg.CombatDodgeDelayMin;
        var dodgeMax = cfg.CombatDodgeDelayMax;
        if (SettingsControls.ComboRangeRow(L.Settings.DodgeDelay, L.Settings.DodgeDelayHelp, ref dodgeMin, ref dodgeMax, delayLabels))
        {
            cfg.CombatDodgeDelayMin = dodgeMin;
            cfg.CombatDodgeDelayMax = dodgeMax;
            cfg.SaveDebounced();
        }

        var moveMin = cfg.CombatMoveDelayMin;
        var moveMax = cfg.CombatMoveDelayMax;
        if (SettingsControls.ComboRangeRow(L.Settings.MoveDelay, L.Settings.MoveDelayHelp, ref moveMin, ref moveMax, delayLabels))
        {
            cfg.CombatMoveDelayMin = moveMin;
            cfg.CombatMoveDelayMax = moveMax;
            cfg.SaveDebounced();
        }

        var cushionMin = cfg.CombatCushionMin;
        var cushionMax = cfg.CombatCushionMax;
        if (SettingsControls.ComboRangeRow(L.Settings.DangerRoom, L.Settings.DangerRoomHelp, ref cushionMin, ref cushionMax, roomLabels))
        {
            cfg.CombatCushionMin = cushionMin;
            cfg.CombatCushionMax = cushionMax;
            cfg.SaveDebounced();
        }

        if (PresetLacksMovementModule(cfg.CombatPresetName))
        {
            SettingsControls.Note(Loc.T(L.Settings.CombatMovementNoModule, cfg.CombatPresetName), Frame.Look.Warning);
        }
    }

    private static void ResolveMovementLabels()
    {
        var none = Loc.T(L.Settings.MovementLevelNone);
        delayLabels[0] = none;
        delayLabels[1] = Loc.T(L.Settings.DelayShort);
        delayLabels[2] = Loc.T(L.Settings.DelayLong);
        roomLabels[0] = none;
        roomLabels[1] = Loc.T(L.Settings.RoomSmall);
        roomLabels[2] = Loc.T(L.Settings.RoomMedium);
        roomLabels[3] = Loc.T(L.Settings.RoomLarge);
    }

    // Fetching the preset serializes it on BossMod's side, so the check is only refreshed every few seconds.
    private static bool PresetLacksMovementModule(string preset)
    {
        var now = Environment.TickCount64;
        if (preset != moduleCheckPreset || now - moduleCheckTick >= ModuleCheckIntervalMs)
        {
            moduleCheckPreset = preset;
            moduleCheckTick = now;
            moduleMissing = BossModMovementTuning.PresetHasModule(preset) == false;
        }

        return moduleMissing;
    }

    private static void DrawBreaksGroup(Configuration cfg)
    {
        if (Gw.BeginSection(Loc.T(L.Settings.HumanizerBreaks)))
        {
            DrawBreaksRows(cfg);
        }

        Gw.EndSection();
    }

    private static void DrawBreaksRows(Configuration cfg)
    {
        var enabled = cfg.HumanizerEnabled;
        if (SettingsControls.ToggleRow(L.Settings.HumanizerEnable, L.Settings.HumanizerEnableHelp, ref enabled))
        {
            cfg.HumanizerEnabled = enabled;
            cfg.SaveDebounced();
        }

        if (!cfg.HumanizerEnabled)
        {
            SettingsControls.Note(Loc.T(L.Settings.HumanizerOff));
            return;
        }

        var fatesBetween = cfg.HumanizerFatesBeforeBreak;
        if (SettingsControls.SliderRow(L.Settings.FatesBetween, L.Settings.FatesBetweenHelp, ref fatesBetween, FatesBetweenMin, FatesBetweenMax, Loc.T(L.Settings.FatesFormat)))
        {
            cfg.HumanizerFatesBeforeBreak = fatesBetween;
            cfg.SaveDebounced();
        }

        var breakMin = cfg.HumanizerBreakMinMinutes;
        var breakMax = cfg.HumanizerBreakMaxMinutes;
        if (SettingsControls.RangeRow(L.Settings.BreakLength, L.Settings.BreakLengthHelp, ref breakMin, ref breakMax, BreakMinutesMin, BreakMinutesMax, Loc.T(L.Settings.MinutesFormat)))
        {
            cfg.HumanizerBreakMinMinutes = breakMin;
            cfg.HumanizerBreakMaxMinutes = breakMax;
            cfg.SaveDebounced();
        }

        var activity = Math.Max(0, Array.IndexOf(breakActivities, cfg.HumanizerBreakActivity));
        if (SettingsControls.Choices.DrawRow(L.Settings.BreakActivity, L.Settings.BreakActivityHelp, breakActivityChoices, ref activity))
        {
            cfg.HumanizerBreakActivity = breakActivities[activity];
            cfg.SaveDebounced();
        }
    }

    private static void DrawIdleSpotsGroup(Configuration cfg)
    {
        if (Gw.BeginSection(Loc.T(L.Settings.IdleSpots)))
        {
            SettingsControls.TitleRow(L.Settings.SavedSpots, L.Settings.SavedSpotsHelp);
            DrawIdleSpotList(cfg);
        }

        Gw.EndSection();
    }

    private static void DrawIdleSpotList(Configuration cfg)
    {
        var spots = cfg.HumanizerIdleSpots;
        if (spots.Count == 0)
        {
            SettingsControls.Note(Loc.T(L.Settings.NoSpots));
        }

        var removeIndex = -1;
        for (var spotIndex = 0; spotIndex < spots.Count; spotIndex++)
        {
            var spot = spots[spotIndex];
            var territory = TerritoryNames.Of(spot.TerritoryId);
            ImGui.PushID(spotIndex);
            Gw.BeginRow(spot.Name.Length > 0 ? spot.Name : territory, spot.Name.Length > 0 ? territory : string.Empty);
            if (SettingsControls.RemoveButton())
            {
                removeIndex = spotIndex;
            }

            Gw.EndRow();
            ImGui.PopID();
        }

        if (removeIndex >= 0)
        {
            spots.RemoveAt(removeIndex);
            cfg.SaveDebounced();
        }

        ImGui.BeginDisabled(Svc.Objects.LocalPlayer is null);
        if (Gw.Button(Loc.T(L.Settings.SaveSpot), ButtonStyle.Prominent, ButtonSize.Small))
        {
            SaveCurrentSpot(cfg);
        }

        ImGui.EndDisabled();
    }

    private static void SaveCurrentSpot(Configuration cfg)
    {
        switch (IdleSpotOps.TryCapture(out var spot))
        {
            case IdleSpotCapture.Saved:
                cfg.HumanizerIdleSpots.Add(spot!);
                GameTextGlyphs.Add(spot!.Name);
                cfg.SaveDebounced();
                Svc.Chat.Print(Loc.T(L.Settings.SpotSavedChat, TerritoryNames.Of(spot.TerritoryId)));
                break;
            case IdleSpotCapture.Airborne:
                Svc.Chat.PrintError(Loc.T(L.Settings.SpotAirborneChat));
                break;
            case IdleSpotCapture.NoAetheryte:
                Svc.Chat.PrintError(Loc.T(L.Settings.SpotNoAetheryteChat));
                break;
        }
    }

    private static void DrawWanderingGroup(Configuration cfg)
    {
        if (Gw.BeginSection(Loc.T(L.Settings.HumanizerWandering)))
        {
            DrawWanderingRows(cfg);
        }

        Gw.EndSection();
    }

    private static void DrawWanderingRows(Configuration cfg)
    {
        var pauseMin = cfg.HumanizerPauseMinSec;
        var pauseMax = cfg.HumanizerPauseMaxSec;
        if (SettingsControls.RangeRow(L.Settings.PauseBetween, L.Settings.PauseBetweenHelp, ref pauseMin, ref pauseMax, 0, PauseMaxSec, Loc.T(L.Settings.SecondsFormat)))
        {
            cfg.HumanizerPauseMinSec = pauseMin;
            cfg.HumanizerPauseMaxSec = pauseMax;
            cfg.SaveDebounced();
        }

        var wanderMin = cfg.HumanizerWanderMinMeters;
        var wanderMax = cfg.HumanizerWanderMaxMeters;
        if (SettingsControls.RangeRow(L.Settings.WalkDistance, L.Settings.WalkDistanceHelp, ref wanderMin, ref wanderMax, WanderMetersMin, WanderMetersMax, Loc.T(L.Settings.MetersFormat)))
        {
            cfg.HumanizerWanderMinMeters = wanderMin;
            cfg.HumanizerWanderMaxMeters = wanderMax;
            cfg.SaveDebounced();
        }
    }

    private static void DrawCitiesGroup(Configuration cfg)
    {
        if (Gw.BeginSection(Loc.T(L.Settings.HumanizerCities)))
        {
            SettingsControls.TitleRow(L.Settings.AllowedCities, L.Settings.AllowedCitiesHelp);
            DrawHumanizerCityList(cfg);
        }

        Gw.EndSection();
    }

    private static void DrawHumanizerCityList(Configuration cfg)
    {
        var cities = CityCatalog.All;
        for (var kind = ExpansionKind.DT; kind >= ExpansionKind.ARR; kind--)
        {
            if (!HasCities(cities, kind))
            {
                continue;
            }

            Gw.Text(ExpansionLabels.Name(kind), TextRole.Footnote, Frame.Look.SecondaryLabel);
            for (var index = 0; index < cities.Length; index++)
            {
                var city = cities[index];
                if (city.Expansion != kind)
                {
                    continue;
                }

                var selected = cfg.HumanizerCities.Contains(city.TerritoryId);
                if (!Gw.Checkbox(ref selected, city.Name))
                {
                    continue;
                }

                if (selected)
                {
                    cfg.HumanizerCities.Add(city.TerritoryId);
                }
                else
                {
                    cfg.HumanizerCities.Remove(city.TerritoryId);
                }

                cfg.SaveDebounced();
            }

            ImGui.Spacing();
        }

        if (cfg.HumanizerCities.Count == 0)
        {
            SettingsControls.Note(Loc.T(L.Settings.NoCities), Frame.Look.Destructive);
        }
    }

    private static bool HasCities(CityInfo[] cities, ExpansionKind kind)
    {
        for (var index = 0; index < cities.Length; index++)
        {
            if (cities[index].Expansion == kind)
            {
                return true;
            }
        }

        return false;
    }
}
