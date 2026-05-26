namespace AutoFateGrind.Core.Zones;

// Shared FATE zones (post-Shadowbringers overworld zones that grant Bicolor Gemstones).
// TerritoryId values verified against game data; AchievementId placeholders are 0 until
// they are looked up from the Achievement sheet (or resolved live via AgentFateProgress).
public static class ZoneRegistry
{
    public static readonly ZoneInfo[] Zones =
    [
        new() { TerritoryId = 813, Name = "Lakeland",              Expansion = ExpansionKind.ShB, MinLevel = 70, AchievementId = 0 },
        new() { TerritoryId = 814, Name = "Kholusia",              Expansion = ExpansionKind.ShB, MinLevel = 70, AchievementId = 0 },
        new() { TerritoryId = 815, Name = "Amh Araeng",            Expansion = ExpansionKind.ShB, MinLevel = 70, AchievementId = 0 },
        new() { TerritoryId = 816, Name = "Il Mheg",               Expansion = ExpansionKind.ShB, MinLevel = 70, AchievementId = 0 },
        new() { TerritoryId = 817, Name = "The Rak'tika Greatwood",Expansion = ExpansionKind.ShB, MinLevel = 70, AchievementId = 0 },
        new() { TerritoryId = 818, Name = "The Tempest",           Expansion = ExpansionKind.ShB, MinLevel = 70, AchievementId = 0 },

        new() { TerritoryId = 956, Name = "Labyrinthos",           Expansion = ExpansionKind.EW,  MinLevel = 80, AchievementId = 0 },
        new() { TerritoryId = 957, Name = "Thavnair",              Expansion = ExpansionKind.EW,  MinLevel = 80, AchievementId = 0 },
        new() { TerritoryId = 958, Name = "Garlemald",             Expansion = ExpansionKind.EW,  MinLevel = 80, AchievementId = 0 },
        new() { TerritoryId = 959, Name = "Mare Lamentorum",       Expansion = ExpansionKind.EW,  MinLevel = 80, AchievementId = 0 },
        new() { TerritoryId = 960, Name = "Ultima Thule",          Expansion = ExpansionKind.EW,  MinLevel = 80, AchievementId = 0 },
        new() { TerritoryId = 961, Name = "Elpis",                 Expansion = ExpansionKind.EW,  MinLevel = 80, AchievementId = 0 },

        new() { TerritoryId = 1187, Name = "Urqopacha",            Expansion = ExpansionKind.DT,  MinLevel = 90, AchievementId = 0 },
        new() { TerritoryId = 1188, Name = "Kozama'uka",           Expansion = ExpansionKind.DT,  MinLevel = 90, AchievementId = 0 },
        new() { TerritoryId = 1189, Name = "Yak T'el",             Expansion = ExpansionKind.DT,  MinLevel = 90, AchievementId = 0 },
        new() { TerritoryId = 1190, Name = "Shaaloani",            Expansion = ExpansionKind.DT,  MinLevel = 90, AchievementId = 0 },
        new() { TerritoryId = 1191, Name = "Heritage Found",       Expansion = ExpansionKind.DT,  MinLevel = 90, AchievementId = 0 },
        new() { TerritoryId = 1192, Name = "Living Memory",        Expansion = ExpansionKind.DT,  MinLevel = 90, AchievementId = 0 },
    ];

    public static IEnumerable<ZoneInfo> ByExpansion(ExpansionKind exp) => Zones.Where(z => z.Expansion == exp);
}
