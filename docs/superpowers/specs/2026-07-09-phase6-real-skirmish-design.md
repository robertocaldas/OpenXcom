# Phase 6 — Real Data & First Editor Run — Design Spec

**Date:** 2026-07-09
**Status:** Approved (design); pending implementation plan
**Project:** OpenXcom Extended (C++) → C#/Unity clean-slate rewrite.
**Parent spec:** `2026-07-04-battlescape-skirmish-design.md`

## 1. Goal & context

Phases 1-5 built the full skirmish loop (map render, movement, combat/LOS,
enemy AI, turn/win-loss) and it is fully covered by `Tests.Standalone`
(xUnit, no Editor needed) — but it has **never run inside the Unity Editor**.
Two things block that:

1. All unit/armor/weapon stats are hardcoded C# stand-ins
   (`RuleUnit.Soldier`/`.Sectoid`, `RuleArmor.None`/`.Personal`,
   `RuleItem.Rifle`/`.Pistol`) rather than loaded from the real ruleset data
   `Xcom.Convert` was designed to produce. Only terrain/map data (CULTA,
   CULTA00) has ever actually been converted.
2. Nothing wires `BattleState` to `BattleController.Bind` — no scene exists,
   no code spawns unit GameObjects, and `TileRenderer` has no collider, so
   `BattleController`'s `Physics.Raycast`-based input can't hit anything even
   if it were wired up.

This slice closes both gaps: real Sectoid-vs-soldier data, converted for
real, driving an actual playable scene in the Editor. Verified live this
session via the Coplay MCP plugin (a connected Unity Editor instance driven
directly through `mcp__coplay-mcp__*` tools — check
`list_unity_project_roots` before assuming one isn't available; see
`CLAUDE.md`).

## 2. Scope — squad & data sources

2 XCom soldiers vs. 2 Sectoids, spawned at CULTA00's route nodes (the only
converted mapblock). Stats sourced from `bin/standard/xcom1/`:

| Unit | Source | Notes |
|---|---|---|
| XCom soldier | `soldiers.rul` `STR_SOLDIER` | Use `minStats` as a fixed "rookie" baseline — no min/max random rolling this slice. |
| Sectoid | `units.rul` `STR_SECTOID_SOLDIER` | `stats:` block, `armor: SECTOID_ARMOR0`. |
| Sectoid armor | `armors.rul` `SECTOID_ARMOR0` | `frontArmor`/`sideArmor`/`rearArmor`/`underArmor` → `RuleArmor`. `spriteSheet: SECTOID.PCK` confirms which sprite set to convert. |
| Soldier weapon | `items.rul` `STR_RIFLE` + `STR_RIFLE_CLIP` | Clip's `power: 30` flattens onto the weapon (Core has no ammo/clip model — matches the existing simplification). |
| Sectoid weapon | `items.rul` `STR_PLASMA_PISTOL` + `STR_PLASMA_PISTOL_CLIP` | Clip's `power: 52`. `damageType: 5` maps to `DamageType.Plasma` — cosmetic label only; `Combat.cs` doesn't branch on `DamageType` yet, so this doesn't change any formula. |
| Soldier armor | `RuleArmor.None` (existing stand-in) | No `STR_PERSONAL_ARMOR` conversion this slice — soldier starts unarmored, matches vanilla UFO's first-mission rookies. |

## 3. `Xcom.Convert` additions

New `RuleYamlDecoder` (YamlDotNet), reading a **curated list of specific
entry IDs** out of `soldiers.rul`/`units.rul`/`armors.rul`/`items.rul` — not
a general schema for every field/entry in these files. Looks up exactly the
six IDs in the table above and emits JSON shaped to match Core's existing
`RuleUnit`/`RuleArmor`/`RuleItem` field names:

- `units.json` — `{ id, stats: {...}, armorId }` for `STR_SOLDIER` and
  `STR_SECTOID_SOLDIER`.
- `armors.json` — `{ id, front, side, rear, under }` for `SECTOID_ARMOR0`.
- `items.json` — `{ id, twoHanded, power, damageType, accuracySnap,
  accuracyAimed, accuracyAuto, autoShots, tuSnap, tuAimed, tuAuto }` for
  `STR_RIFLE` and `STR_PLASMA_PISTOL` (power pulled from the paired clip
  entry).

`ConvertJob.Run` gains a fourth sprite atlas: `SECTOID.PCK`/`.TAB` via the
existing `PckDecoder.Load(..., 32, 40)` call (same frame dimensions as
`XCOM_0`), added to `manifest.json` alongside the three new JSON files.

Golden test in `Tests.Standalone`: known `.rul` snippet → expected decoded
`RuleUnit`/`RuleArmor`/`RuleItem`, matching the existing `McdDecoder`/
`PckDecoder` golden-test pattern.

## 4. `OpenXcom.Core` additions

`DataLoader` gains `LoadUnits`, `LoadArmors`, `LoadItems` (parallel to the
existing `LoadTiles`/`LoadTerrain`/`LoadMapBlock`), reading the new JSON into
`RuleUnit`/`RuleArmor`/`RuleItem`. The existing hardcoded stand-ins
(`RuleUnit.Soldier`/`.Sectoid`, `RuleArmor.None`/`.Personal`,
`RuleItem.Rifle`/`.Pistol`) are **left in place** — existing xUnit tests
depend on them as fixtures. The bootstrap uses the loaded-from-JSON versions
instead of calling the static factories.

## 5. `OpenXcom.Unity` additions

- **`TileRenderer`**: add a `BoxCollider2D` sized to one tile's footprint.
  This is a bug fix, not new behavior — `BattleController.Update()` already
  raycasts against tiles by parsing `hit.transform.parent.name`
  (`"Tile_x_y_z"`), but no collider has ever existed for it to hit.
- **`UnitRenderer`** (new): single `SpriteRenderer` + `BoxCollider2D`,
  positioned via the same `IsoProjection.MapToScreen` math `TileRenderer`
  uses. Displays one frame from the unit's sprite atlas (direction/animation
  frame selection is out of scope — see §6).
- **`BattlescapeBootstrap`** (new MonoBehaviour): on `Start()`, loads
  terrain/tiles/mapblock (same loading `BattlescapeMapView` already does)
  plus `units.json`/`armors.json`/`items.json`, builds the hardcoded 2v2
  squad as `BattleUnit`s with real `RuleUnit`/`RuleArmor`/`RuleItem` data,
  calls `BattleState.SpawnAtRouteNodes`, instantiates one `UnitRenderer` per
  unit, and calls `battleController.Bind(state, transforms)`. Everything is
  constructed in code — no Inspector drag-and-drop references required.
- **`Assets/Scenes/Battlescape.unity`** (new): orthographic camera + one
  GameObject holding `BattlescapeMapView` (or folded into
  `BattlescapeBootstrap`) + `BattleController`. Built and saved live through
  Coplay MCP tools (`create_scene`/`create_game_object`/`add_component`/
  `set_property`), not hand-authored YAML.

## 6. What this slice does NOT include

Explicitly deferred, so a future session knows what's still stubbed:

- **No on-screen HUD.** Hit/miss, damage, turn changes, and battle-over are
  still `Debug.Log` console output only (already wired in
  `BattleController.DrainAndAnimate`) — no TU/health bars, no action menu.
- **No `alienDeployments.rul`/`alienRaces.rul` parsing.** Squad composition
  is hardcoded in `BattlescapeBootstrap`, not deployment-driven —
  `BattleState.SpawnAtRouteNodes` doesn't consume deployment data at all
  (see its `[SIMPLIFIED]` doc comment), so parsing these files would be dead
  work for this slice.
- **No soldier stat randomization.** `STR_SOLDIER`'s `minStats` is used as a
  fixed baseline; `maxStats`/stat-roll-on-recruit is not implemented.
- **No ammo/clip inventory model.** Weapon `power` is flattened from the
  clip at conversion time; there's no reloading, no ammo count, no
  `compatibleAmmo` handling.
- **No soldier armor conversion.** Soldiers use the existing `RuleArmor.None`
  stand-in, not a converted `STR_PERSONAL_ARMOR` (or equivalent).
- **No unit sprite direction/animation.** `UnitRenderer` shows one static
  frame; the 8-direction × walk-phase indexing the parent spec describes
  (§5) is not implemented.
- **No general `.rul` YAML converter.** `RuleYamlDecoder` only looks up the
  six specific entry IDs named in §2 — adding a new unit/weapon/armor still
  requires touching the decoder, not just editing a ruleset file.
- **General scope carried over from the parent spec, still deferred:**
  Geoscape, Basescape, research/manufacturing, inventory screens, throwing,
  psi, morale/panic, TFTD, mod support, save/load, audio, night/lighting,
  sprite recoloring, voxel LOS, trajectory-based hit deviation.

## 7. Verification

Live, in-Editor, via Coplay MCP this session (not a blind hand-off):

1. `dotnet test` (`Tests.Standalone`) — golden test for `RuleYamlDecoder`,
   `DataLoader.LoadUnits`/`LoadArmors`/`LoadItems` round-trip tests.
2. `mcp__coplay-mcp__check_compile_errors` after each Unity-side change.
3. `play_game`, then drive the scene: click-select a soldier, click-move,
   right-click-fire at a Sectoid, Space to end turn and watch the AI turn
   run, confirm `BattleOverEvent` fires on a wipe.
4. `get_unity_logs` to confirm the expected `Debug.Log` sequence.
5. `capture_scene_object` / screenshot to visually sanity-check tile and
   unit sprites render in roughly the right isometric positions.
