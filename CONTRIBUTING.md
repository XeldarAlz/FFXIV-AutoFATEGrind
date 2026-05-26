# Contributing

Thanks for taking an interest. This is a small solo project, but PRs are welcome and I'll review them.

## Quick start

```bash
git clone --recurse-submodules https://github.com/XeldarAlz/FFXIV-AutoFATEGrind.git
cd FFXIV-AutoFATEGrind
dotnet build AutoFateGrind.sln -c Release
```

You need the .NET 10 SDK. The plugin requires Dalamud at runtime; CI pulls a Dalamud dev build automatically and that's enough to compile. See `.github/workflows/release.yml` if you want to reproduce CI locally.

Load the built plugin via `/xlsettings` -> **Experimental** -> **Dev Plugin Locations**, pointing at `AutoFateGrind/bin/x64/Release/AutoFateGrind/AutoFateGrind.dll`.

## Project layout

- `AutoFateGrind/Core/`: zone data, FATE scanner, automation state machine, IPC adapters.
- `AutoFateGrind/Windows/`: ImGui main window, settings, dependencies.
- `AutoFateGrind/`: plugin entry points, config, command wiring.
- `ECommons/`: submodule, shared Dalamud helpers. Don't patch this directly; upstream it.

Keep logic small and direct. This plugin has one job.

## Before you open a PR

1. `dotnet build -c Release` cleanly.
2. Test in-game in at least one zone per expansion you touched. FATE behavior shifts between expansions, so a fix that works for Dawntrail may not work for Shadowbringers.
3. Keep the diff focused. One concern per PR.
4. Match the existing style. No heavy abstractions "for later."
5. If your change affects what a user sees or types (commands, window layout, settings), update the README.

## Good first issues

Check the tracker for anything labeled `good first issue`. Zone-specific FATE quirks (broken obstacle maps, unreachable centers, weird preparation NPCs) are usually the lowest-friction way to help: pick a zone that's misbehaving, attach a log of what the plugin did vs. what should have happened, and a fix is usually a small change.

## Security

Please don't file public issues for security problems; see [SECURITY.md](SECURITY.md).

## Code of conduct

See [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md). Be decent.

## License

By contributing, you agree your contributions are licensed under AGPL-3.0-or-later, the same as the project.
