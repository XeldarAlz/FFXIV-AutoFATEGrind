<p align="center">
  <img src="AutoFateGrind/Images/Icon.png" width="180" alt="Auto Fate Grind icon" />
</p>

<h1 align="center">Auto Fate Grind</h1>

<p align="center">
  <a href="https://github.com/XeldarAlz/FFXIV-AutoFateGrind/releases/latest"><img alt="Release" src="https://img.shields.io/github/v/release/XeldarAlz/FFXIV-AutoFateGrind?style=flat-square&color=blue"></a>
  <a href="https://github.com/XeldarAlz/FFXIV-AutoFateGrind/releases"><img alt="Downloads" src="https://img.shields.io/github/downloads/XeldarAlz/FFXIV-AutoFateGrind/total?style=flat-square&color=blue"></a>
  <a href="https://github.com/XeldarAlz/FFXIV-AutoFateGrind/actions/workflows/release.yml"><img alt="Build" src="https://img.shields.io/github/actions/workflow/status/XeldarAlz/FFXIV-AutoFateGrind/release.yml?style=flat-square"></a>
  <a href="LICENSE.md"><img alt="License" src="https://img.shields.io/badge/license-AGPL--3.0--or--later-blue?style=flat-square"></a>
</p>

<p align="center">
  <em>Shared FATE farming, automated. Built on Dalamud.</em>
</p>

---

## What it does

Lists every Shared FATE zone from Shadowbringers through Dawntrail in one window. Tick the zones you want, press **Run selected**, and the plugin teleports to each one, scans for active FATEs, flies to them, engages, and rotates to the next selected zone when the current one runs dry.

Two grind modes ship in v1:

- **Achievement** prioritizes zones with unfinished "Date with Destiny" progress and stops when all selected achievements are done.
- **Bicolor Gemstones** maximizes gemstones/hour, prefers bonus FATEs, and stops at the 1500 cap (or manual stop).

## Features

- One window listing every Shared FATE zone, grouped by expansion (ShB / EW / DT).
- Click cards to select; **Run selected** rotates through them back-to-back.
- Per-zone live state: active FATE count pill, achievement progress bar.
- Live FATE tracker inline in the main window (and as a separate HUD overlay when enabled).
- FATE filters: minimum time remaining, maximum progress, persistent blacklist.
- Mode picker: Achievement vs Bicolor Gemstones, with per-mode zone routing.
- Locked zones greyed out with hover tooltips explaining why.
- Cancellable mid-run; selection persists across reloads.

## Install

In-game: `/xlsettings` -> **Experimental** -> paste into **Custom Plugin Repositories**:

```
https://raw.githubusercontent.com/XeldarAlz/DalamudPlugins/main/repo.json
```

Tick **Enabled**, click **+**, then **Save and Close**. Open `/xlplugins` -> **All Plugins**, search for **Auto Fate Grind**, and install.

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

## License

AGPL-3.0-or-later. See [LICENSE.md](LICENSE.md).
