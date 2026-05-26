using clib.Extensions;

namespace AutoFateGrind.Core.Ipc;

internal static class DefaultCombatPreset
{
    public const string Name = AfgConstants.BundledCombatPresetName;

    private const string OriginalName = "CBT - DwD";

    private const string Base64Brotli =
        "GwwhAORUXTtl2E+e+WjPVqrAATn5Vo7kAFPsV3yB7j9/70DoI3U+9gNJLIvWvQ+obKxFY4OEmQ5LM14T" +
        "x7oq1h1/UtrJk9/GAwBR4oGEDyIKa3iC+BSLybsB0rb9npzw4dspAURonZO29YKebWcZWVvjSe2aoHg+" +
        "pMZuTSx8+PzJgL9NCMPrDpud8EGkQZ3QwcEQTdWNFT6IxOByT6341fL41D2aVRGTJSCWCPYnvLgJN+Zv" +
        "XIKQ6ndG48VlJJCcwn61crTbpGd7QNbNTgZMj2mPF2IriLSt8EFUG73iCUTc7SePgrnkBySYJwxDpX3V" +
        "Zi0DDoLHZ0x5FxYZ5Z22cuqrsildhctZ5GgfouzrTBLj2hg+mvDXxoQTZWZtKG0rPS0Di6i2Rxh4MUrL" +
        "WvSl7MmwPhBzuOEq4mNlFjsEwlIFUt61Zcm28jvIwMtCaRsG3sy8dpo32qwrg/8SGqbQrENNwGSafVkp" +
        "i7ITlqwM7ds7i9TrshKraXcvUA4zgJn2y4PlNfc/y0pQ1fL+aAlV2HzfEinuZTiz07eRtmm2F7qY10AJ" +
        "YXH3bFNttNtE9rC0nR8sKnYR5SRWWaeneLcuRaZn1nunALFUF9NwgARvhesSntF4ETqmTvjwqUfaICfc" +
        "UxcUfxRE0p5oQuSTEvi4azS77/UsBfNZ2u6Ae2mPdCCTrcAmJsfaUKRIeDUL5M1Km8gyu/EA4DEePAA=";

    private static string? cached;

    public static string GetSerialized()
    {
        if (cached is not null) return cached;
        var json = Base64Brotli.FromBase64();
        cached = json.Replace($"\"Name\": \"{OriginalName}\"", $"\"Name\": \"{Name}\"");
        return cached;
    }
}
