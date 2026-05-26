namespace AutoFateGrind.Core.Zones;

public enum ExpansionKind
{
    ShB,
    EW,
    DT,
}

public static class ExpansionKindExtensions
{
    public static string DisplayName(this ExpansionKind exp) => exp switch
    {
        ExpansionKind.ShB => "Shadowbringers 5.0",
        ExpansionKind.EW  => "Endwalker 6.0",
        ExpansionKind.DT  => "Dawntrail 7.0",
        _ => exp.ToString(),
    };
}
