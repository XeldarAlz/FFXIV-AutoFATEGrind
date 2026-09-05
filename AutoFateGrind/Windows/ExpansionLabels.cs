using AutoFateGrind.Core.Localization;
using AutoFateGrind.Core.Zones;

namespace AutoFateGrind.Windows;

internal static class ExpansionLabels
{
    public static string Name(ExpansionKind kind) => kind switch
    {
        ExpansionKind.ARR => Loc.T(L.Grind.ExpansionArr),
        ExpansionKind.HW  => Loc.T(L.Grind.ExpansionHw),
        ExpansionKind.SB  => Loc.T(L.Grind.ExpansionSb),
        ExpansionKind.ShB => Loc.T(L.Grind.ExpansionShb),
        ExpansionKind.EW  => Loc.T(L.Grind.ExpansionEw),
        _                 => Loc.T(L.Grind.ExpansionDt),
    };
}
