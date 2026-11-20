# LevelEasily

Client-only BepInEx mod for Valheim (requires Jotunn). Adds **Level area** to the hoe menu: one click levels a rectangle
snapped to the metre grid, for the same price as doing it by hand. Other players and the server do not need the mod.
Detailed rules: [SPEC.md](https://github.com/PetyM/valheim-mods/blob/master/LevelEasily/SPEC.md).

## Usage
- The preview shows a rectangle snapped to the metre grid (heightmap vertices) with points coloured by what happens to them:
  green = level, red = cut, blue = fill, purple = out of reach (±8 m from the original terrain),
  grey = under a building or plant (skipped), dark red = no pickaxe, dimmed = not enough resources.
- A click levels the area at once and paints it with dirt (like vanilla Level ground, no grass grows on it).
  While the hoe is blocked you hear and see the work: hoe swings, pickaxe hits and Raise ground over the area, as used.
  The cost and key hints are shown below the crosshair.
- **Arrow keys** change the size (left/right = width relative to the camera, up/down = length), default 4 × 4 m, max 16 m.
- The target is the terrain under the player (like vanilla). **L** locks the target to the top surface of the building
  piece under the cursor and locks the grid, so further areas join exactly side by side. **PageUp/PageDown** moves the
  target by 0.1 m (and locks it).

## Cost
- Hoe swings (about 1 per 4 points), cutting deeper than 1 m takes pickaxe hits from the inventory (stamina and its wear),
  filling above 1 m or above the terrain under the player takes stone at the Raise ground rate.
- The hoe is then blocked for as long as the work would take. What resources do not cover is not levelled (nearest points first).
- Stamina has a 30 % discount compared with manual work (`Cost / Stamina discount`).
- Whatever the vanilla pickaxe would drop while digging (stone) goes into your inventory (average, rounded down).

## Surfaces and protection
- Cultivated and paved ground is levelled but not repainted (config option). No lava appears in the Ashlands.
- Respects wards and no-build zones over the whole area and the vanilla hoe restrictions.

## Configuration
`BepInEx/config/PetyM.LevelEasily.cfg` (also in Configuration Manager). Sections `Keys`, `Area`, `Cost`.

## Installation
Install with r2modman or Thunderstore Mod Manager (BepInEx and Jotunn are installed as dependencies).
Manually: put `LevelEasily.dll` into `BepInEx/plugins/`. Only the players who want it need the mod, the server does not.

## Source and issues
Source code, bug reports and suggestions: [https://github.com/PetyM/valheim-mods](https://github.com/PetyM/valheim-mods). Licence MIT.
