# SpiderHeck Palette

Add any RGB color to SpiderHeck's spider customization menu — not just the seven vanilla presets.

## Features

- **Arbitrary colors.** Any hex value, registered as a real, selectable swatch in the vanilla
  customization menu — not a separate overlay or replacement screen.
- **In-game color picker.** Mods menu → SpiderHeckPalette: RGB sliders with a live preview bar,
  plus naming and renaming through the game's own text-input popup. No file editing required.
- **Persistent.** Colors survive a restart and apply correctly to other players in multiplayer
  (see [Notes](#notes) for the one cosmetic exception).
- **Config-file colors.** Prefer hand-authoring a fixed palette instead of the picker? Edit the
  YAML directly.

## Requirements

- SpiderHeck (Mono build) with the [Silk](https://github.com/SilkModding/Silk) mod loader
- Silk 0.7.0+

No other mods required.

## Installation

1. Download `SpiderHeckPalette.dll` from the [releases page](https://github.com/AeonSaber/SpiderHeckPalette/releases)
2. Copy it to `SpiderHeck/Silk/Mods/`
3. Launch the game — default config files are written on first run.

## Usage

### In-game picker

Open the mods menu → **SpiderHeckPalette**. Drag the six RGB sliders (body + legs), watch the
preview bar update live, then **Add this colour** and give it a name in the popup that appears.
It becomes selectable in the customization menu immediately — no restart needed.

- **Copy legs from body** — quick monochrome colors.
- Colors you've added are listed with **Rename** and **Remove** buttons (see
  [Limitations](#limitations)).

### Customization menu

Added colors appear as normal swatches alongside the vanilla ones — same carousel, same preview,
same *Done* flow. Selecting one saves it exactly like a vanilla color.

## Configuration

Two YAML files live in `Silk/Config/Mods/`:

| File | Purpose |
|---|---|
| `SpiderHeckPalette.yaml` | Hand-edit this one. Never touched by the in-game picker, so your comments and organization survive. |
| `SpiderHeckPalette.picker.yaml` | Owned by the in-game picker. Rewritten every time you add or remove a color there. |

Example entry:

```yaml
colors:
  - id: aeons_ember
    name: Ember
    primary: '#FF3B1F'    # body color
    secondary: '#FFB347'  # leg color
```

`id` must stay stable once you've selected that color in-game — the game saves your selection by
ID. Renaming a color in-game (or in the YAML) only ever changes its `name:`; the `id:` never
changes, so a rename can never orphan a save.

Colors added via the picker are also editable directly in `SpiderHeckPalette.picker.yaml` if you'd
rather batch-edit than use the popups.

## Limitations

- **Removing a picker color takes effect on next launch**, not immediately; it stays selectable
  for the remainder of the current session.
- **Renaming** updates the customization carousel once you back out of the mod-settings menu.
- **Multiplayer:** in-match color is fully correct for every player, modded or not — colors are
  sent as raw RGB, not palette indices. The *lobby preview* of your color may show a fallback
  tint on an unmodded peer's screen; that's cosmetic only and doesn't affect the match itself.

## How it works

SpiderHeck's color system is value-based end to end — `Color` values travel over the network
as-is, so any RGB works, including for players without this mod. Disk persistence, however, is
ID-based (the game saves *which* color config you picked, not the raw RGB), and two vanilla UI
paths null-reference on an active color with no matching registered entry. So this mod registers
real `ColorConfig` entries into the game's own swatch list rather than injecting raw colors —
everything downstream (preview, save, restore, network sync) stays unmodified vanilla behavior.

Built with [Harmony](https://github.com/pardeike/Harmony) via
[Silk](https://github.com/SilkModding/Silk). No ILRepack — the DLL binds directly against the
assemblies Silk already loads, so it stays a few KB rather than a few MB.

## Building from source

```
dotnet build -c Release
```

Targets `net472`. Requires a `lib/` folder (not included: the game's and Silk's DLLs are not redistributed) populated with the referenced DLLs from your
`Silk/Library/` install (see `SpiderHeckPalette.csproj` for the exact list).

## Credits

Built on [Silk](https://github.com/SilkModding/Silk) and
[HarmonyX](https://github.com/BepInEx/HarmonyX).
