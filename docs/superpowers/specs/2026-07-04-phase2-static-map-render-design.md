# Phase 2 — Static Map Render — Design Addendum

**Date:** 2026-07-04
**Status:** Approved (author: Claude, operating autonomously per explicit user
delegation — see note below).
**Parent spec:** `docs/superpowers/specs/2026-07-04-battlescape-skirmish-design.md`
(§7, step 2: "Static map render — `MapGenerator` builds a `TileGrid` from real
mapblocks; iso renderer draws it. *Pan around a real X-COM map.*")

> **Process note:** The user authorized autonomous execution for this session
> ("decide for yourself... do not stop to ask me anything"). This addendum
> substitutes for interactive brainstorming Q&A: it makes the same category of
> decisions a brainstorming session would, using the parent spec's already-
> approved architecture as the governing constraint, and documents the
> reasoning so it can be reviewed after the fact instead of before.

## 1. Goal

Render one real, unmodified X-COM mapblock (`MAPS/CULTA00.MAP` +
`ROUTES/CULTA00.RMP`, terrain `CULTA`) as a correctly-overlapping isometric
scene in Unity, built from a `TileGrid` assembled by a ported `MapGenerator`.
No units, no combat, no input beyond camera pan — purely "does a real map look
right." Camera pan and initial framing may use hardcoded values; player-facing
camera controls are a later phase's concern.

## 2. Verified formats (ported from the C++, this session)

### 2.1 `MAPS/*.MAP` — `BattlescapeGenerator::loadMAP`, `BattlescapeGenerator.cpp:2079-2177`

- **Header:** 3 bytes: `[0]=sizeY`, `[1]=sizeX`, `[2]=sizeZ` (Y, X, Z order —
  not X,Y,Z).
- **Tile records:** 4 bytes each, `[floor, westWall, northWall, object]`, each
  a raw index into the terrain's *concatenated* dataset list (see §2.3). `0`
  means "nothing in this slot" and is never resolved to a dataset lookup.
- **Iteration order:** for level `z` from `sizeZ-1` down to `0` (**top level
  first in the file**), then `y` from `0..sizeY-1`, then `x` from `0..sizeX-1`
  (x fastest). One full `sizeX*sizeY` tile block per level.
- Verified against real `CULTA00.MAP` (403 bytes = 3-byte header + 10×10×1×4
  tile bytes — header read as `sizeY=10, sizeX=10, sizeZ=1`, matches file
  size exactly).

### 2.2 `ROUTES/*.RMP` — `BattlescapeGenerator::loadRMP`, `BattlescapeGenerator.cpp:2353-2410`

- **24-byte fixed records.** Verified: real `CULTA00.RMP` is exactly 24 bytes
  (1 node).
- Byte 0 = `posY`, byte 1 = `posX`, byte 2 = `posZ` (raw), byte 3 = unused.
- Final node position: `(x=posX, y=posY, z=sizeZ-1-posZ)` — Z is inverted
  relative to the raw byte.
- Bytes 4,7,10,13,16 = the 5 link slots' node-index byte (the other 2 bytes
  per 3-byte slot are legacy/unused, per the C++). Decode: raw `<= 250` → link
  is an absolute node index (add running node-count offset if merging blocks —
  not needed for a single block, offset is 0); raw `> 250` → special:
  `255→none, 254→north exit, 253→east exit, 252→south exit, 251→west exit`.
- Byte 19 = `type`, byte 20 = `rank`, byte 21 = `flags`, byte 22 = `reserved`
  (unused), byte 23 = `priority`.

### 2.3 Terrain dataset concatenation & index resolution

A terrain (e.g. `CULTA`) is defined in the ruleset (`bin/standard/xcom1/terrains.rul`)
as an **ordered list of MCD datasets**: `CULTA → [BLANKS, CULTIVAT, BARN]`.
Raw per-tile index bytes from the `.MAP` file index directly (0-based, no
off-by-one) into the **concatenation of these datasets' object lists in
order** (`RuleTerrain::getMapData`, `RuleTerrain.cpp:238-260`): walk the
dataset list, subtracting each dataset's record count from the raw index
until it fits within one dataset, then that dataset's local record is the
result. Verified real record counts: `BLANKS`=2, `CULTIVAT`=37, `BARN`=29
(from file sizes ÷ 62). Index `0` is reserved ("nothing") and never resolved —
`BLANKS` record 0 is consequently unreachable from any `.MAP` byte, matching
the C++ comment ("set this broken tile reference to BLANKS 0" is the
*corruption fallback*, not the normal path).

We are **not** parsing `.rul` YAML in this phase (full ruleset conversion is
a separate, later concern per the parent spec §3/§9). We hardcode the one
terrain we need — `CULTA → [BLANKS, CULTIVAT, BARN]` — directly in
`Xcom.Convert`, the same way Phase 1 hardcoded `CULTIVAT`/`XCOM_0`. This is a
straight-line extension of the established Phase-1 pattern, not a new
architectural decision.

### 2.4 Sprite index scope: per-dataset, not global

A `MapData` record's 8 animation-frame indices (`Frame[0..7]`) are indices
into **its own dataset's** sprite atlas — never a global/merged atlas. `BLANKS`,
`CULTIVAT`, and `BARN` each get their own PNG atlas + frame-rect JSON (same
`AtlasWriter`/`PckDecoder` pipeline Phase 1 already built, just invoked three
times). The merged/concatenated list in §2.3 exists **only** to resolve which
*record* a raw `.MAP` byte points to; once resolved, rendering that record
uses its own dataset's atlas.

### 2.5 `isBackTileObject` — draw-order flag already available

`MapData::isBackTileObject()` (`MapData.cpp:135-137`) = `BigWall < 6 ||
BigWall == 9`. `BigWall` is already a decoded field on `McdRecord` from Phase
1 (`McdDecoder.cs`, byte offset 33). No converter change needed — this is
computed in `OpenXcom.Core`, not re-decoded.

### 2.6 Y-offset for floor draw position

`MapData::getYOffset()` is set from the MCD record's `P_Level` field
(`MapDataSet.cpp:172`: `to->setYOffset((int)mcd.P_Level)`) — already decoded
as `McdRecord.PLevel` (byte offset 49) in Phase 1. No converter change needed.

### 2.7 Isometric projection — `Camera::convertMapToScreen`, `Camera.cpp:475-480`

With standard sprite size 32×40 px:

```
screenX = (mapX - mapY) * 16                      // spriteWidth/2  = 16
screenY = (mapX + mapY) * 8  - mapZ * 24           // spriteWidth/4 = 8, per-level = (spriteHeight + spriteWidth/4)/2 = (40+8)/2 = 24
```

This is the *tile origin* in screen space; the camera's own pixel pan offset
is added on top by the caller (`Map.cpp:910`) — out of scope for this phase
(fixed/hardcoded camera position is fine).

### 2.8 Draw order — `Map::drawTerrain`, `Map.cpp:737+`

Loop nesting: Z outer (ascending), Y middle, X inner (`Map.cpp:900-907`). Per
tile, in order:

1. Floor (`O_FLOOR`), screen Y shifted by `-tile.YOffset`
2. West wall (`O_WESTWALL`)
3. North wall (`O_NORTHWALL`)
4. Object (`O_OBJECT`), **only if `IsBackTileObject`**
5. *(units/items/projectiles/etc. — not in this phase)*
6. Object (`O_OBJECT`) again, **only if NOT `IsBackTileObject`** (drawn after
   units, i.e. visually "in front")

Since this phase has no units, the practical draw order collapses to: floor →
west wall → north wall → object (regardless of back/front flag — with no units
between them, back-object and front-object painting land in the same visual
place). We still record the `IsBackTileObject` flag per-object now (it's free
— already decoded) so Phase 3+ (units) doesn't need to revisit this code.

**Painter's-algorithm ordering in Unity:** rather than relying on draw *call*
order, use `SpriteRenderer.sortingOrder`, computed once per tile-part at
`TileGrid` build time:

```
sortingOrder = ((z * mapHeight + y) * mapWidth + x) * 4 + partRank
  partRank: floor=0, westWall=1, northWall=2, object=3
```

This reproduces the C++'s exact back-to-front nesting (z, then y, then x, then
part) as a single monotonically increasing integer — standard Unity 2D
sorting-order pattern for isometric tile stacks.

## 3. Scope for this phase

**In scope:**
- `Xcom.Convert`: decode `BLANKS`, `BARN` datasets (reuse existing
  `PckDecoder`/`McdDecoder`/`AtlasWriter` — no new decoder code, just new
  invocations); new `MapBlockDecoder` for `.MAP`+`.RMP` → `mapblock-CULTA00.json`.
- `OpenXcom.Core`: `RuleTerrain` (ordered dataset list + index resolution),
  `MapDataTile` (converted-JSON-facing tile-part model), `DataLoader` (reads
  Convert's JSON via `System.Text.Json` — already in the .NET 8 shared
  framework, so this stays a "zero UnityEngine references" pure-C# addition,
  not a new external dependency), `MapGenerator` (raw mapblock + terrain →
  populated `TileGrid`). All fully unit-testable via `dotnet test`.
- `OpenXcom.Unity`: a pure-math `IsoProjection` class (no `UnityEngine` types
  — physically lives in the `Unity` assembly folder but is written so
  `Tests.Standalone` can compile and test it directly, same pattern as Core)
  implementing §2.7/§2.8's formulas; `TileRenderer`/`BattlescapeMapView`
  MonoBehaviours that load the converted atlases + `TileGrid` and instantiate
  `SpriteRenderer`s with computed `sortingOrder`. Runtime asset loading reads
  PNG/JSON directly from `Assets/GameData/` via `File.ReadAllBytes` +
  `Texture2D.LoadImage` (works in Editor Play mode without needing the
  gitignored folder to go through Unity's asset-import pipeline).

**Explicitly out of scope (later phases per parent spec §7):** units,
pathfinding, combat, LOS/fog, AI, turns, ruleset YAML parsing, multi-mapblock
composition (the 10×10-grid tiling of several blocks into one battlefield),
click/input picking (§2.7's inverse formula), HUD.

**Known limitation, called out explicitly:** this development environment
cannot open the Unity Editor. `OpenXcom.Core` and the pure-math
`IsoProjection` class are fully verified by `dotnet test`. The
`TileRenderer`/`BattlescapeMapView` MonoBehaviours are written to the same
standard of care but **cannot be visually verified this session** — they are
correct-per-spec best-effort code, not confirmed-by-eyeball. This will be
flagged again at hand-off, not silently glossed over.

## 4. Testing strategy

- `MapBlockDecoder`: golden test against real `CULTA00.MAP`/`.RMP` — assert
  exact dims (10×10×1), exact tile count (100), at least one hand-verified
  non-zero raw tile-part value at a known offset, and the RMP's 1 node with
  its decoded position/links.
- `RuleTerrain.ResolveTile`: synthetic 2-dataset terrain (sizes 3 and 5) —
  assert indices 0-2 resolve into dataset A at local 0-2, indices 3-7 resolve
  into dataset B at local 0-4. Separately, a real-data test using the actual
  `BLANKS`(2)/`CULTIVAT`(37)/`BARN`(29) sizes confirms boundary indices (1,
  2, 3, 39, 67) land in the expected dataset.
- `MapGenerator`: build a `TileGrid` from the real converted `CULTA00`
  mapblock + the real 3-dataset terrain; assert grid dimensions, that at
  least one known tile's floor/wall/object resolve to the expected
  dataset+local-index+sprite-frame, and that `IsBackTileObject`/`YOffset`
  flow through correctly.
- `IsoProjection`: exact-value tests for `MapToScreen` at several `(x,y,z)`
  against the formula in §2.7 (hand-computed expected values), and a
  monotonicity test for the `sortingOrder` formula (later-painted tiles/parts
  always get a strictly higher order than earlier ones, per the exact nesting
  in §2.8).
- Unity MonoBehaviours: **not** unit-tested (require the Editor); reviewed for
  spec compliance only.

## 5. Self-review

- Placeholder scan: none found — every format/formula above has a file:line
  citation and was cross-checked against real data on disk in this repo
  (record counts, file sizes, header values).
- Consistency: `MapDataTile` field names in §2 map 1:1 onto the already-shipped
  `McdRecord` fields from Phase 1 (`BigWall`, `PLevel`, `Frames[8]`) — no
  renaming, no drift.
- Scope check: single mapblock, single terrain, no input/units/combat — matches
  parent spec §7 step 2 exactly ("static map render... pan around a real map").
- Ambiguity check: the one real design choice made here (rather than purely
  discovered) is per-tile-part `sortingOrder` vs. Unity's default
  transform-Z-based sorting — resolved in favor of explicit `sortingOrder`
  because it's the direct analogue of the C++'s explicit draw-call order and
  removes any dependency on Unity's default 2D sort settings.
