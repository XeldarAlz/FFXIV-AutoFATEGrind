using clib.Extensions;

namespace AutoFateGrind.Core.Ipc;

internal static class DefaultCombatPreset
{
    public const string Name = AfgConstants.BundledCombatPresetName;

    public const int Revision = 1;

    private const string Base64Brotli =
        "4YgOAWCc5ErO8zbnUraX8y98h5DuAagZYbHoXK2N6aRPiUwJNEIjld/bxzzxDUIjpMcWlWRSGomQm4jf" +
        "FrsA7OV1gZloBS4PQgZ+GVMNnujMzN/yBp5AHEKhArI3xFZLoaLw6TaTcyrRN0LcOB+3ryPOIXFUb53s" +
        "bQaByUQgEkgrBXcRmxLUEpIjJ7S1T6D58c7MeCAijidFhVXNe0xyt+d9a1qgMkT6mYBcitoU4y2BlgAg" +
        "i5WnuI+CAwv759WxqGX+I6sGHnI+3qUAhCLd7Fs+eYCY3zmLr1qZfoH7GUhGsxEWtA9BWtabbii+i1Ys" +
        "gpBn5jh9hA9EapIlIIZD+lS+Ta0ANdKWQT0+BjwtI+F3ziK4nwmmPpP1Wh6yiDCTl/nbvTuMkOcUJEsr" +
        "UuCbQPKBwA4ykpKvmIGSPViEL9f5ioZudihAg5kkf7AsLfvzg4ALhz5J8WhR0G2+aPEU/8mK5+cai3sn" +
        "lzwpTMAJPxwzbpX+Fx58sKBmInIWDc1MEMnMLaUA+8kZ5/I+L9+/ojJcdPJKzIVh6rko73mf4PPSn8qp" +
        "GsDt1u3fK53c0e+MpAB8Pdgkzsn45SUjHpbEeFqJriUeDgdEMP0fb3lrxQrOrRM=";

    private static string? cached;

    public static string GetSerialized() => cached ??= Base64Brotli.FromBase64();
}
