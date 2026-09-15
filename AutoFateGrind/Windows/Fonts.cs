using AutoFateGrind.Core;
using AutoFateGrind.Core.Localization;
using Dalamud;
using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Utility.Raii;
using ECommons.DalamudServices;
using System.IO;

namespace AutoFateGrind.Windows;

internal static class Fonts
{
    private const float TitlePx = 24f;
    private const float HeadlinePx = 18f;
    private const float CaptionPx = 14f;
    private const float IconLargePx = 24f;
    private const float IconDisplayPx = 34f;

    private const string LatinFontFile = "NotoSans-Medium-Latin.ttf";
    private const int LatinBlocksEnd = 0x036F;
    private const int LatinAdditionalStart = 0x1E00;
    private const int LatinAdditionalEnd = 0x1EFF;

    // Face indices inside Dalamud's NotoSansCJK-Regular.ttc collection.
    private const int NotoCjkJapaneseFace = 0;
    private const int NotoCjkSimplifiedChineseFace = 2;

    private static readonly ushort[] LatinBlocks =
    [
        0x0020, 0x00FF,
        0x0100, 0x017F,
        0x0180, 0x024F,
        0x2000, 0x206F,
    ];

    private static readonly ushort[] SymbolBlocks =
    [
        0x2190, 0x21FF,
    ];

    private static readonly NoOpScope noOp = new();

    private static IUiBuilder? builder;
    private static IFontAtlas? atlas;
    private static byte[]? latinFont;
    private static ushort[] latinRanges = [0];
    private static ushort[] mergeRanges = [0];
    private static int cjkFace = NotoCjkJapaneseFace;
    private static int bakedGameTextVersion;

    private static IFontHandle? body;
    private static IFontHandle? title;
    private static IFontHandle? headline;
    private static IFontHandle? caption;
    private static IFontHandle? iconLarge;
    private static IFontHandle? iconDisplay;

    public static void Initialize(IUiBuilder uiBuilder, string pluginDirectory)
    {
        builder = uiBuilder;
        atlas = uiBuilder.FontAtlas;
        latinFont = LoadLatinFont(Path.Combine(pluginDirectory, "Fonts", LatinFontFile));
        RefreshRanges();

        body = TextHandle(UiBuilder.DefaultFontSizePx);
        title = TextHandle(TitlePx);
        headline = TextHandle(HeadlinePx);
        caption = TextHandle(CaptionPx);
        iconLarge = atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk => tk.AddFontAwesomeIconFont(new SafeFontConfig { SizePx = IconLargePx })));
        iconDisplay = atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk => tk.AddFontAwesomeIconFont(new SafeFontConfig { SizePx = IconDisplayPx })));
        uiBuilder.Draw += RebuildOnNewGameText;

        if (atlas.AutoRebuildMode == FontAtlasAutoRebuildMode.Disable)
        {
            _ = atlas.BuildFontsAsync();
        }
    }

    public static void OnLanguageChanged() => Rebuild();

    public static void Dispose()
    {
        if (builder is not null)
        {
            builder.Draw -= RebuildOnNewGameText;
        }

        body?.Dispose();
        title?.Dispose();
        headline?.Dispose();
        caption?.Dispose();
        iconLarge?.Dispose();
        iconDisplay?.Dispose();
        body = title = headline = caption = iconLarge = iconDisplay = null;
        builder = null;
        atlas = null;
        latinFont = null;
    }

    public static IDisposable PushBody() => body?.Push() ?? noOp;

    public static IDisposable PushTitle() => title?.Push() ?? noOp;

    public static IDisposable PushHeadline() => headline?.Push() ?? noOp;

    public static IDisposable PushCaption() => caption?.Push() ?? noOp;

    public static IDisposable PushIconLarge() => iconLarge?.Push() ?? ImRaii.PushFont(UiBuilder.IconFont);

    public static IDisposable PushIconDisplay() => iconDisplay?.Push() ?? ImRaii.PushFont(UiBuilder.IconFont);

    public static IDisposable PushIconFor(float unscaledPx)
    {
        if (unscaledPx >= 30f) return PushIconDisplay();
        if (unscaledPx >= 20f) return PushIconLarge();
        return ImRaii.PushFont(UiBuilder.IconFont);
    }

    // The game's AXIS font and Dalamud's Noto Sans CJK both stop at Latin-1, and merging a second font
    // only for the missing letters mixes two typefaces inside one word. The bundled Latin subset of
    // Noto Sans is therefore the primary font for every Latin letter, digit and punctuation mark, and
    // Noto Sans CJK only fills in the scripts it does not carry.
    private static byte[]? LoadLatinFont(string path)
    {
        try
        {
            if (File.Exists(path)) return File.ReadAllBytes(path);
            Svc.Log.Warning($"{AfgConstants.LogPrefix} Latin font missing at '{path}'; falling back to the Dalamud default font, Latin Extended letters will not render");
        }
        catch (Exception exception)
        {
            Svc.Log.Error(exception, $"{AfgConstants.LogPrefix} Failed to read the Latin font");
        }

        return null;
    }

    private static IFontHandle TextHandle(float sizePx)
        => atlas!.NewDelegateFontHandle(e => e.OnPreBuild(tk =>
        {
            var primary = latinFont is not null
                ? tk.AddFontFromMemory(latinFont, new SafeFontConfig { SizePx = sizePx, GlyphRanges = latinRanges }, LatinFontFile)
                : tk.AddDalamudDefaultFont(sizePx, latinRanges);
            tk.Font = primary;

            if (mergeRanges.Length <= 1) return;
            tk.AddDalamudAssetFont(DalamudAsset.NotoSansCjkRegular, new SafeFontConfig
            {
                SizePx = sizePx,
                GlyphRanges = mergeRanges,
                MergeFont = primary,
                FontNo = cjkFace,
            });
        }));

    private static void RebuildOnNewGameText()
    {
        if (bakedGameTextVersion != GameTextGlyphs.Version)
        {
            Rebuild();
        }
    }

    private static void Rebuild()
    {
        RefreshRanges();
        if (atlas is not null)
        {
            _ = atlas.BuildFontsAsync();
        }
    }

    // The delegates above run again on every atlas rebuild, so refreshing these arrays and queueing a rebuild
    // is all a language switch or newly seen game text needs. Game text glyphs are baked in every language
    // because they follow the client language, and every native name is baked for the language picker.
    private static void RefreshRanges()
    {
        var latin = new bool[GlyphRanges.CodepointCount];
        var merge = new bool[GlyphRanges.CodepointCount];
        var extra = new bool[GlyphRanges.CodepointCount];
        GlyphRanges.MarkRanges(latin, LatinBlocks);
        GlyphRanges.MarkRanges(merge, SymbolBlocks);
        GlyphRanges.MarkRanges(extra, Loc.Current.ExtraGlyphRanges);
        GlyphRanges.MarkRanges(extra, Loc.CatalogGlyphRanges);
        MarkNativeNames(extra);

        var gameText = GameTextGlyphs.Present;
        for (var codepoint = GlyphRanges.FirstNonAsciiCodepoint; codepoint < GlyphRanges.CodepointCount; codepoint++)
        {
            if (latin[codepoint] || (!extra[codepoint] && !gameText[codepoint]))
            {
                continue;
            }

            if (IsLatinCodepoint(codepoint))
            {
                latin[codepoint] = true;
            }
            else
            {
                merge[codepoint] = true;
            }
        }

        latinRanges = GlyphRanges.ToRanges(latin);
        mergeRanges = GlyphRanges.ToRanges(merge);
        cjkFace = ReferenceEquals(Loc.Current, Languages.Chinese) ? NotoCjkSimplifiedChineseFace : NotoCjkJapaneseFace;
        bakedGameTextVersion = GameTextGlyphs.Version;
    }

    private static bool IsLatinCodepoint(int codepoint)
        => codepoint <= LatinBlocksEnd || (codepoint >= LatinAdditionalStart && codepoint <= LatinAdditionalEnd);

    private static void MarkNativeNames(bool[] extra)
    {
        var languages = Languages.All;
        for (var languageIndex = 0; languageIndex < languages.Length; languageIndex++)
        {
            GlyphRanges.MarkText(extra, languages[languageIndex].NativeName);
        }
    }

    private sealed class NoOpScope : IDisposable
    {
        public void Dispose() { }
    }
}
