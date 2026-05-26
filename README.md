<p align="center">
  <img src="AutoFateGrind/Images/Icon.png" width="180" alt="Auto FATE Grind icon" />
</p>

<h1 align="center">Auto FATE Grind</h1>

<p align="center">
  <a href="https://github.com/XeldarAlz/FFXIV-AutoFATEGrind/releases/latest"><img alt="Release" src="https://img.shields.io/github/v/release/XeldarAlz/FFXIV-AutoFATEGrind?style=flat-square&color=blue"></a>
  <a href="https://github.com/XeldarAlz/FFXIV-AutoFATEGrind/releases"><img alt="Downloads" src="https://img.shields.io/github/downloads/XeldarAlz/FFXIV-AutoFATEGrind/total?style=flat-square&color=blue"></a>
  <a href="https://github.com/XeldarAlz/FFXIV-AutoFATEGrind/actions/workflows/release.yml"><img alt="Build" src="https://img.shields.io/github/actions/workflow/status/XeldarAlz/FFXIV-AutoFATEGrind/release.yml?style=flat-square"></a>
  <a href="LICENSE.md"><img alt="License" src="https://img.shields.io/badge/license-AGPL--3.0--or--later-blue?style=flat-square"></a>
</p>

<p align="center">
  <em>Shared FATEs, farmed for you. Built on Dalamud.</em>
</p>

---

<p align="center">
  <img src="AutoFateGrind/Images/demo.gif" alt="Auto FATE Grind demo" />
</p>

## What it does

Lists every FATE zone from A Realm Reborn through Dawntrail in one window. Tick the zones you want, press **Run selected**, and the plugin teleports to each one, scans for active FATEs, flies to them, engages, and rotates to the next selected zone when the current one runs dry.

## Features

- One window listing every FATE zone from ARR through DT, grouped by expansion. Shared FATE modes auto-scope to ShB / EW / DT, with a toggle to show all expansions.
- Click cards to select; **Run selected** rotates through them back-to-back.
- Grind modes: endless, achievement-driven, Bicolor gemstone cap, or fixed FATE count.
- Per-zone live state: active FATE count pill, achievement progress bar.
- Live FATE tracker inline in the main window (and as a separate HUD overlay when enabled).
- FATE filters: minimum time remaining, maximum progress, persistent blacklist.
- Auto-trade Bicolor Gemstones at threshold, with resume-after-trade.
- Locked zones greyed out with hover tooltips explaining why.
- Cancellable mid-run; selection persists across reloads.

## Install

In-game: `/xlsettings` → **Experimental** → paste into **Custom Plugin Repositories**:

```
https://raw.githubusercontent.com/XeldarAlz/DalamudPlugins/main/repo.json
```

Tick **Enabled**, click **+**, then **Save and Close**. Open `/xlplugins` → **All Plugins**, search for **Auto FATE Grind**, and install.

The plugin needs a few helpers for movement and combat to be installed and loaded. Open `/afg deps` after install to see the list and one-click each missing one.

## Commands

| Command | Action |
|---|---|
| `/afg` | Toggle the main window |
| `/fategrind` | Alias for `/afg` |
| `/afg config` | Open settings |
| `/afg deps` | Open dependencies window |
| `/afg about` | Open credits / links |
| `/afg target` | Log targeted NPC's BaseId (debug helper) |

## More from me

If you liked this plugin, take a look at my other Dalamud work. You might find something else there for you.

→ [XeldarAlz Dalamud Plugins](https://github.com/XeldarAlz/DalamudPlugins)

## License

AGPL-3.0-or-later. See [LICENSE.md](LICENSE.md).
