using AutoFateGrind.Core.Localization;

namespace AutoFateGrind.Core.Changelog;

internal readonly record struct ChangelogEntry(string Version, string Date, LocString[] Highlights);
