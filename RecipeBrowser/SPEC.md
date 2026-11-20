# RecipeBrowser – specification 1.0

Status: approved · 2026-11-20 (category tabs → tags + chips, tags in English, collapse into a bar, implement phases 1–4). The starting state (0.1.0) was described in the repo-root `README.md`; current documentation is in `README.md` in this folder.
Facts about game data: `kb/lokalizace-chybejici-klic.md`, `kb/filtr-nedostupnych-predmetu.md`, `kb/zdroje-biomu-predmetu.md`.

## 1. Catalog: cleanup and metadata

### 1.1 Filtering unobtainable entries
Rules in order. The result for each entry is logged (debug), so it is possible to find out why something is missing.
1. Fixed exclusions: `ItemType` Customization and Attach_Atgeir, prefabs `SP_*` and `FW_*`, names `*_attach`, `*_nochest`,
   `*_ragdoll`, `*Cheat*`, `*Test*`.
2. DLC that is not installed (`m_dlc` + `DLCMan.IsDLCInstalled`).
3. Missing translation: hide vanilla entries. Show modded entries under a cleaned-up prefab name (`SwordBlackmetal_TW`
   → "Sword Blackmetal TW").
4. Duplicates with the same `m_name`: keep the one that has a recipe or a source.
5. No source and no use (no recipe, conversion, drop, pickup, trader, ingredient): hide.
   Config `Catalog / Hide unobtainable` (on by default).
6. Pieces: hide `m_repairPiece`/`m_removePiece`, pieces without resources (except seeds and saplings) and stations that are
   not in any tool's piece table (`fire_pit_haldor`, `goblin_bed`…). Treat feasts (`Feast`) as food, not as a tool.
7. User ignore list in the config (exact names, `*suffix`, `prefix*`).

### 1.2 Entry metadata
`Entry` gets: main category, subcategories, a set of tags, biome (progression order), station level, set
(`m_setName`) and food values. Tags are computed once when the catalog is built.

### 1.3 Progression (biome)
- Order: Meadows 0, Black Forest 1, Swamp 2, Mountains 3, Plains 4, Mistlands 5, Ashlands 6, Deep North 7.
  Ocean has its own tag `#ocean` and sits between Swamp and Mountains. No source = "Unknown", sorted last.
- Seed from direct sources (lowest biome): vegetation and its drops, creatures from spawn lists and their drops. The biome
  of a trader or location is only taken as max(biome of the place, biome of the boss from `m_requiredGlobalKey`).
- Propagate to a fixed point. A recipe gets max(ingredient biomes, biome of the station and its level). An entry gets
  the min over all ways to obtain it. For "any one ingredient" the min is used. Upgrades (q>1) are not propagated.
- A station's biome is the biome of its piece; level L is the L-th lowest biome of its extensions.
- A small vanilla override table (Chitin, Serpent meat, Coins, Amber, Ruby, boss drops) and a user override in the config.
- The biome index is built lazily after entering the world. `ZoneSystem` does not exist in the menu and modded spawn lists
  may only arrive with the server sync.

### 1.4 New "Sources" view
Next to recipes, cards without crafting: creature drop (with chance), pickup, mining (with the required pickaxe tier), tree,
chest in a location, trader (price, condition). The card shows the biome; cards are sorted by biome progression
(Meadows first, unknown last). The data come for free from the seed phase in 1.3.

## 2. Search and tags (replaces category tabs)
- Words: all must match the name or prefab, as before.
- `@mod`: as before.
- `#tag`: entry tag. A colon means "and", so `#weapons:plains` = `#weapons` + `#plains`
  and `#food:stamina` = `#food` + `#stamina`. A prefix is enough (`#swo` → `#swords`) if it is unambiguous.
- `-x`: negation of any token (`-@Therzie`, `-#pieces`, `-leather`).
- `^name`: entries that use the given entry as an ingredient.
- `|` between tokens: or (`#swords|#axes`).
- Suggestions below the field: while typing `#`/`@`, offer tags or mods with result counts, Tab completes.
- Below the field a row of "chips" (Weapons, Armor, Food, Materials, Pieces…). Click inserts or removes the tag in the search,
  right click inserts it negated. Chips are only a shortcut, the state is always visible in the search text.

### 2.1 Tags (English, short; with a description in the suggestions)
- Categories: `#materials #food #potions #weapons #armor #ammo #tools #pieces #trophies #fish #misc`.
- Weapons: `#swords #axes #battleaxes #clubs #sledges #spears #polearms #knives #fists #bows #crossbows #staffs
  #shields` and `#1h`/`#2h`. Shields: `#tower #buckler`.
- Armor: `#helmet #chest #legs #cape #gloves #utility #trinket`, sets `#set:<name>`.
- Food: `#health #stamina #eitr #balanced` (dominant stat), `#meads`, `#drinks`.
- Pieces by `PieceCategory`: `#crafting #building #stone #furniture #feasts`, and `#seeds`.
- Biomes: `#meadows #blackforest #swamp #mountains #plains #mistlands #ashlands #deepnorth #ocean #unknown`.
- State: `#craftable` (can be crafted now from the inventory at a nearby station), `#bookmarked`, `#station:<station>`.

## 3. Sorting
- Default: biome → category → subcategory → station level → name. A Progression / Name / Mod switch above the grid.
- Optional biome separator in the grid (a thin row with the biome name). Config, off by default.

## 4. UI
- **Collapse:** a key (config, default `Y`, only with the inventory open) and a button in the panel header collapse both
  panels into a small bar with a title. The state is remembered. The recipe window can be opened even with the panel
  collapsed (R/U over the inventory).
- **Move and resize:** each panel (results, bookmarks, recipe window) can be dragged by its header and resized by the corner.
  The grid size snaps to whole slots (columns × rows), the recipe window to whole cards.
  Positions and sizes are saved to the config relative to the screen, so they survive resolution changes.
  A "Reset layout" button or config option.
- **Scrolling:** the mouse wheel scrolls by rows, not pages. The `<` `>` buttons scroll by pages.
- **Recipe window:** station groups wrap without a row limit (previously the rest was lost). Notes on a card wrap.
  The card shows the recipe's biome.
- **Craft:** clicking a card while standing at the right station selects the recipe in the vanilla crafting list.
- **"Have" counts:** optionally also from nearby chests if CraftFromContainers is running (soft dependency, otherwise
  inventory only).
- **Bookmarks:** reorder by dragging. Bookmark groups in a later version.

## 5. Performance and robustness
- The catalog is rebuilt when the number of recipes or items in ObjectDB changes. The biome index is recomputed only then.
- Search runs over precomputed tags (HashSet), without LINQ allocations per keystroke. The grid recycles slots (already does).
- Catalog building is timed and logged (target < 200 ms, the biome index may run in a coroutine).

## 6. Out of scope for 1.0
Server/sync, recipe editing, cheats (spawning items), bookmark groups, data export.

## 7. Implementation phases
1. Catalog: filter 1.1, metadata and tags 1.2, new search 2 and chips instead of tabs, sorting by category.
2. Progression: biome index 1.3, sorting 3, "Sources" view 1.4.
3. UI: collapsing, move and resize, saving the layout, scrolling by rows, recipe window fixes.
4. Extras: craft from a card, counts from chests, `#craftable`, bookmark order.
