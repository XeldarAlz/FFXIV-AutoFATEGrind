namespace AutoFateGrind.Core.Zones;

public enum ExpansionKind
{
    ARR = 0,
    HW  = 1,
    SB  = 2,
    ShB = 3,
    EW  = 4,
    DT  = 5,
}

public static class ExpansionKindExtensions
{
    public static string DisplayName(this ExpansionKind exp) => exp switch
    {
        ExpansionKind.ARR => "A Realm Reborn 2.0",
        ExpansionKind.HW  => "Heavensward 3.0",
        ExpansionKind.SB  => "Stormblood 4.0",
        ExpansionKind.ShB => "Shadowbringers 5.0",
        ExpansionKind.EW  => "Endwalker 6.0",
        ExpansionKind.DT  => "Dawntrail 7.0",
        _ => exp.ToString(),
    };

    public static ExpansionKind FromExVersion(uint exVersion) => exVersion switch
    {
        0 => ExpansionKind.ARR,
        1 => ExpansionKind.HW,
        2 => ExpansionKind.SB,
        3 => ExpansionKind.ShB,
        4 => ExpansionKind.EW,
        5 => ExpansionKind.DT,
        _ => ExpansionKind.DT,
    };
}
