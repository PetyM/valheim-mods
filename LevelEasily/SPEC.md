# LevelEasily – specification

Hoe piece **Level area**: one click levels a rectangle snapped to the metre grid to a target height.
Principle: it costs as much as the same work done by hand with vanilla tools (hoe, pickaxe, Raise ground). Only the
convenience is faster, not the resources. The vanilla behaviour the model is based on: `../kb/vanilla-motyka-dosah-a-cena.md`
(knowledge base, in Czech).

## Area and target
- Rectangle snapped to whole metres (heightmap vertices). Arrow keys change its size relative to the camera, 1 to `Largest side`.
- Default target = terrain under the player (`ZoneSystem.GetGroundHeight`), like vanilla Level ground, unaffected by jumping or swimming.
- **L** locks the target to the top surface of the building piece under the cursor (even when aiming at its side; otherwise
  terrain under the player) and locks the grid; pressing L again unlocks. **PageUp/PageDown** moves the target by
  `Height step` and locks it.

## What happens to a vertex
Each vertex gets one state (colour in the preview):
- **level** (green): deviation up to 1 cm, nothing is done,
- **cut** (red) / **fill** (blue): saturation by deviation,
- **out of reach** (purple): the target is more than ±8 m from the base height; levelled only to the edge of the reach,
- **under building** (grey): a building piece or plant is near the vertex (±0.45 m, from min to max height ±0.5 m); skipped,
- **no pickaxe** (dark red): would need a pickaxe and there is no usable one in the inventory; skipped,
- **not enough resources** (dimmed): does not fit into stamina, wear or stone; skipped.

## Cost
For a vertex with change Δ (towards the reachable target) and existing smooth delta `s`:
- **Hoe**: every changed vertex needs finishing. Level ground swings = changed vertices ÷ vertices per swing, where
  vertices per swing = k², k being the largest swing spacing at which a grid of vanilla swings levels every vertex to at
  least 95 % (from the `mud_road_v2` settings; vanilla gives k = 2, i.e. 4 vertices).
- **Pickaxe**: the hoe lowers a vertex by at most `1 + s`. The rest is pickaxe volume (1 m² per vertex), hits =
  volume ÷ volume of one hit (from the `TerrainOp` of the pickaxe's `m_spawnOnHitTerrain` prefab). A hit costs stamina like a
  pickaxe attack (skill, equipment, status effects) and pickaxe wear. The pickaxe used is the one in the inventory
  (Pickaxes skill) that can still dig the most (hits left × volume per hit); it does not need to be equipped.
- **Stone**: the hoe raises a vertex for free by at most `1 − s`, and only when the target is not above the terrain under
  the player (vanilla smooth pulls towards the terrain under the player). The rest of the fill is Raise ground volume:
  swings = volume ÷ volume of a `raise_v2` swing, stone = swings × `raise_v2` cost. Each Raise ground swing costs hoe
  stamina and wear.
- **Time**: after the click the hoe is blocked for (hoe swings × `m_placeDelay`) + (pickaxe hits × `Pickaxe hit time`).
- **Dug-up material**: whatever a vanilla terrain hit drops (`TerrainOp.m_spawnOnPlaced`, chance, 1–max pieces; nothing at
  7.95 m below the base unless the prefab allows it) is given for the counted hits as the average rounded down.
  Straight into the inventory, overflow dropped at the feet. Stone dug up is not counted towards stone for filling the same area.
- **Stamina discount** (config `Cost / Stamina discount`, default 30 %): swings and hits cost that much less stamina.
  User decision after testing in game (2026-11-20). Wear, stone and time are not discounted.
- `m_noPlacementCost` (debug) waives only stone, like vanilla.

## Partial levelling
When resources are short, as many vertices as fit are levelled, nearest to the player first. The preview shows what will
be levelled and the readout below the crosshair shows how many points. Nothing affordable = a message saying what is missing.

## Surface
- Dirt (r = 1, g = b = 0) is painted only in cells whose four corners are all level after the write and at least one of
  them changes. Alpha (lava in the Ashlands, vegetation elsewhere) is kept.
- `Preserve cultivated and paved ground` (on by default): cells with g > 0.5 (outside the Deep North, where g is snow) or
  b > 0.5 are not repainted.

## Checks before writing
- Vanilla placement status (`UpdatePlacementGhost`): deep snow, dungeon, invalid spot etc. with the vanilla message.
- Ward and no-build zone for **every** vertex the write touches (including level corners of painted cells).
- Another player within 1.5 m of a vertex that rises by more than 0.5 m: refuse.
- Target below sea level: only a warning in the readout.

## Writing (multiplayer)
1. Create/claim (`ClaimOwnership`) the `TerrainComp` of only those zones containing touched vertices, wait 0.5 s; the hoe
   is blocked meanwhile.
2. Verify ownership and `m_initialized`, load fresh data (`CheckLoad`, `Poke`), recompute the plan only from vertices in
   claimed zones and repeat all checks.
3. Write `m_levelDelta = target − base height` (not `LevelTerrain`, which misses the target when the deltas sum beyond
   ±8 m), paint, `m_operations++`, `m_lastOpPoint/Radius`, `Save`, `Poke`.
4. Only after a successful write deduct stamina, wear and stone, give dug-up material, play the swing animation and the
   vanilla effects of the work done: Level ground (`Piece.m_placeEffect`, `TerrainOp.m_onPlacedEffect`, hoe `m_buildEffect`),
   Raise ground when stone was used, pickaxe terrain hit (weapon and attack `m_hitTerrainEffect`) when the pickaxe was used.
Any failure before step 3 = nothing is written or paid.

## Out of scope
- Pickaxe skill is not raised for counted hits.
