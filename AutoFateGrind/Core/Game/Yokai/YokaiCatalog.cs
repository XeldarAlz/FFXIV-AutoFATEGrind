namespace AutoFateGrind.Core.Game.Yokai;

internal readonly record struct YokaiEntry(uint MinionId, uint MedalItemId, uint WeaponItemId, uint[] ZoneIds);

internal static class YokaiCatalog
{
    public const uint WatchItemId = 15222;
    public const uint PlainMedalItemId = 15167;

    private const uint MiddleLaNoscea = 134;
    private const uint LowerLaNoscea = 135;
    private const uint WesternLaNoscea = 138;
    private const uint UpperLaNoscea = 139;
    private const uint WesternThanalan = 140;
    private const uint CentralThanalan = 141;
    private const uint EasternThanalan = 145;
    private const uint SouthernThanalan = 146;
    private const uint CentralShroud = 148;
    private const uint EastShroud = 152;
    private const uint SouthShroud = 153;
    private const uint NorthShroud = 154;
    private const uint OuterLaNoscea = 180;

    private const uint CoerthasWesternHighlands = 397;
    private const uint DravanianForelands = 398;
    private const uint DravanianHinterlands = 399;
    private const uint ChurningMists = 400;
    private const uint SeaOfClouds = 401;
    private const uint AzysLla = 402;

    private const uint Fringes = 612;
    private const uint RubySea = 613;
    private const uint Yanxia = 614;
    private const uint Peaks = 620;
    private const uint Lochs = 621;
    private const uint AzimSteppe = 622;

    private const uint Jibanyan = 200;
    private const uint Komasan = 201;
    private const uint Whisper = 202;
    private const uint Blizzaria = 203;
    private const uint Kyubi = 204;
    private const uint Komajiro = 205;
    private const uint Manjimutt = 206;
    private const uint Noko = 207;
    private const uint Venoct = 208;
    private const uint Shogunyan = 209;
    private const uint Hovernyan = 210;
    private const uint Robonyan = 211;
    private const uint Usapyon = 212;
    private const uint LordEnma = 390;
    private const uint LordAnanta = 391;
    private const uint Zazel = 392;
    private const uint Damona = 393;

    private static readonly uint[] HeavenswardZones =
        [CoerthasWesternHighlands, DravanianForelands, DravanianHinterlands, ChurningMists, SeaOfClouds, AzysLla];

    private static readonly uint[] StormbloodZones =
        [Fringes, RubySea, Yanxia, Peaks, Lochs, AzimSteppe];

    public static readonly YokaiEntry[] Entries =
    [
        new(Jibanyan, 15168, 15210, [CentralShroud, LowerLaNoscea, CentralThanalan]),
        new(Komasan, 15169, 15216, [EastShroud, WesternLaNoscea, EasternThanalan]),
        new(Whisper, 15170, 15212, [SouthShroud, UpperLaNoscea, SouthernThanalan]),
        new(Blizzaria, 15171, 15217, [NorthShroud, OuterLaNoscea, MiddleLaNoscea]),
        new(Kyubi, 15172, 15213, [WesternThanalan, CentralShroud, LowerLaNoscea]),
        new(Komajiro, 15173, 15219, [CentralThanalan, EastShroud, WesternLaNoscea]),
        new(Manjimutt, 15174, 15218, [EasternThanalan, SouthShroud, UpperLaNoscea]),
        new(Noko, 15175, 15220, [SouthernThanalan, NorthShroud, OuterLaNoscea]),
        new(Venoct, 15176, 15211, [MiddleLaNoscea, WesternThanalan, CentralShroud]),
        new(Shogunyan, 15177, 15221, [LowerLaNoscea, CentralThanalan, EastShroud]),
        new(Hovernyan, 15178, 15214, [WesternLaNoscea, EasternThanalan, SouthShroud]),
        new(Robonyan, 15179, 15215, [UpperLaNoscea, SouthernThanalan, NorthShroud]),
        new(Usapyon, 15180, 15209, [OuterLaNoscea, MiddleLaNoscea, WesternThanalan]),
        new(LordEnma, 30805, 30809, StormbloodZones),
        new(LordAnanta, 30804, 30808, HeavenswardZones),
        new(Zazel, 30803, 30807, HeavenswardZones),
        new(Damona, 30806, 30810, StormbloodZones),
    ];

    public static int IndexOfMinion(uint minionId)
    {
        if (minionId == 0)
        {
            return -1;
        }

        for (var index = 0; index < Entries.Length; index++)
        {
            if (Entries[index].MinionId == minionId)
            {
                return index;
            }
        }

        return -1;
    }
}
