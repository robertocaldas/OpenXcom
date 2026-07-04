# Battlescape Skirmish — Design Spec

**Date:** 2026-07-04
**Status:** Approved (design); pending implementation plan
**Project:** OpenXcom Extended (C++) → C#/Unity clean-slate rewrite, first vertical slice.

## 1. Goal & constraints

Reimplement OXCE's tactical **Battlescape** as a fully playable squad-vs-squad
skirmish in C#/Unity, as the first vertical slice of a ground-up rewrite.

Established constraints (from brainstorming):

- **Goal:** ship a real Unity game (not a throwaway prototype).
- **Fidelity — visual:** must look **exactly like X-COM (UFO: Enemy Unknown)** —
  same isometric view, same sprites, same tile draw order.
- **Fidelity — data:** clean slate for *save/ruleset formats* (no original save
  compatibility, no mod support), BUT we **do** reuse the original **graphics and
  rulesets as source data**, converted offline.
- **Effort:** solo / spare time → build in always-runnable increments.
- **Scope of this slice:** the *full* skirmish — squad vs squad, real maps,
  line-of-sight/fog, cover/terrain, enemy AI, win/lose. (Approach 2: full data
  conversion.)
- **Target game:** UFO (Enemy Unknown), not TFTD.

The C++ tree in `../../src` is a **reference spec**, not source to transpile. We
read it for formulas and formats and reimplement idiomatically in C#.

Original data files live (gitignored) at `unity/RawData/Resources/UFO/` — genuine
DOS X-COM data (`GEODATA`, `TERRAIN`, `UNITS`, `MAPS`, `ROUTES`, `UFOGRAPH`, ...).

## 2. Architecture — four isolated modules

```
Xcom.Convert (offline CLI)  →  Assets/GameData/ (gitignored)  →  OpenXcom.Core  →  OpenXcom.Unity
                                                                      ↑
                                                          Tests.Standalone (xUnit)
```

- **`Xcom.Convert`** — offline .NET CLI. Reads `RawData/Resources/UFO/*`, emits
  Unity-ready PNG atlases + JSON. Pure C#, no Unity. Testable standalone.
- **`OpenXcom.Core`** — the game itself. Pure C#, **zero `UnityEngine`
  references** (enforced via asmdef `noEngineReferences: true`). Deterministic
  (seeded RNG). Unit-tested.
- **`OpenXcom.Unity`** — MonoBehaviours: iso rendering, input, HUD. References
  Core. Contains **zero game rules**.
- **`Tests.Standalone`** — xUnit project compiling `Convert` + `Core`, run via
  `dotnet test`. Lives outside `Assets/` so Unity ignores it.

**Dependency direction:** `Unity → Core`; `Convert → nothing`; `Tests → Convert +
Core`. Core never depends upward. This boundary is load-bearing: it lets combat,
AI, pathfinding, and LOS be tested in seconds without the Editor. The exact-X-COM
*look* lives entirely in `OpenXcom.Unity` + converted assets; `Core` deals only in
grid `Position`s and events, never pixels.

## 3. `Xcom.Convert` — offline converter

Invocation:
`dotnet run --project Xcom.Convert -- --data ../RawData/Resources/UFO --out ../Assets/GameData`

Five decoders, each ported from a specific OpenXcom source:

| Input | Port from | Output |
|---|---|---|
| `PALETTES.DAT`, `BACKPALS.DAT` (256-color, 6-bit RGB, 4 palettes) | `Engine/Palette.cpp` | `palettes.json` (battlescape palette → RGBA32) |
| `*.PCK`+`*.TAB` sprites (RLE: `0xFF` end, `0xFE n` transparent-skip) | `Engine/SurfaceSet.cpp` | one **PNG atlas** per set (units, bigobs, floorob, handob, terrain) + `atlas-*.json` frame rects |
| `TERRAIN/*.MCD` (62-byte records) | `Mod/MapDataSet.cpp` (`struct MCD`, ~line 115) | `tiles-<terrain>.json`: per tile-part — 8 sprite frames, `objectType` (floor/westwall/northwall/object), `blocksLOS`, TU cost, height, `isDoor`, `deathTile`, `altTile` |
| `MAPS/*.MAP` + `ROUTES/*.RMP` | `Battlescape/BattlescapeGenerator.cpp` (map load) | `mapblocks.json`: dims + per-tile 4 MCD indices; route nodes (spawn/patrol, rank, links) |
| `*.rul` / `*.yml` (from `Resources/standard` + `UFO`) | the `Rule*::load()` methods | `units.json`, `items.json`, `armors.json`, `terrains.json`, `deployments.json`, `alienraces.json` |

Decisions:
- **Serialization deps:** YamlDotNet (converter only, reads `.rul`/`.yml`);
  Newtonsoft.Json (emitted JSON, read by both Core and Unity).
- **Core never parses YAML** — only the converter does. Core reads JSON.
- **Determinism:** byte-stable output for identical input.
- **Out of scope:** unit sprite recoloring / palette-swap armor tiers (OXCE
  y-script). We bake the default palette; not needed for the base-game look.
- **Version control:** `Assets/GameData/` is **gitignored** (derivatives of
  copyrighted art). Commit the converter + a `manifest.json` of expected outputs;
  regenerate locally. `RawData/` is likewise gitignored.

## 4. `OpenXcom.Core` — model & systems

**`Rules/` — immutable templates** (loaded from converter JSON via `DataLoader`):
`RuleUnit`, `RuleItem`, `RuleArmor`, `RuleTerrain` (→ `MapDataSet`s),
`RuleDeployment`, `AlienRace`, `MapData`/`MapDataTile` (per-tile-part MCD record).
Never mutate post-load.

**`Battle/` — mutable runtime state:**
- `BattleState` — the `SavedBattleGame` equivalent: owns `TileGrid`,
  `List<BattleUnit>`, current faction/turn, seeded `Rng`. Single source of truth.
- `BattleUnit`, `BattleItem` — per-instance state referencing their `Rule*`.

**Systems (operate over `BattleState`):**
- `MapGenerator` — port of `BattlescapeGenerator`: assemble `TileGrid` from
  `RuleDeployment` → terrain mapblocks, place units at `ROUTES` nodes.
- `Pathfinding` — A* on TU budget; per-tile costs from `MapData`; diagonals ×1.5;
  blocked by walls/occupants. Port of `Pathfinding.cpp`.
- `TileEngine` — line-of-sight / fog-of-war + target visibility.
- `Combat` — hit chance, hit roll, damage roll, armor, death.
- `TurnManager` — IGOUGO turn/faction flow, TU/energy refresh, win/lose.
- `AiModule` — hostile turn: pick target, move into range, shoot. Trimmed port of
  `AIModule.cpp`.

**Core → Unity communication — ordered `BattleEvent` stream:**
Systems mutate state immediately and push ordered events (`UnitMoved(path)`,
`ProjectileFired(from,to,hit)`, `UnitHit(unit,dmg,side)`, `UnitDied`,
`FovChanged(tiles)`, `TurnChanged(faction)`) onto a queue. Unity drains and
animates at its own pace. Decouples logic timing from rendering; makes turns
replayable and assertable in tests.

### Verified combat formulas (from C++)

Firing accuracy — port of `BattleUnit::getFiringAccuracy`
(`src/Savegame/BattleUnit.cpp:2473`):

```
base   = accuracyStat * weaponAccuracy% / 100     (stat: Firing / Melee / Throwing)
if kneeled (firing only):        base = base * 115 / 100
if two-handed & other hand full: base = base * 80 / 100
final  = base * accuracyModifier / 100
  where accuracyModifier = healthPercent − 10 * fatalWounds   (>= 0)
```

Distance drop-off — `TileEngine.cpp:2621-2635`:
```
if distance > upperLimit: acc -= (distance − upperLimit) * dropOff
if distance < lowerLimit: acc -= (lowerLimit − distance) * dropOff
```

Damage — `TileEngine.cpp:3233` (`getRandomDamage`, standard type):
```
rolled = RNG(0,200) * power / 100
final  = max(0, rolled − armor[hitSide])
health -= final    (unit dead at health <= 0)
```

**Simplifications (marked, deferred to later slices):**
- Hit resolution: flat roll `RNG(0,99) < accuracy` rather than OXCE's trajectory/
  deviation model. (Cover-through-gaps needs the trajectory model — later.)
- LOS/FOV: tile + MCD `height` + `blocksLOS`, **not** the 3D voxel `LOFTEMPS`
  model. Visually identical fog in the common case; voxel refinement later.
- Damage: no fatal wounds / stun / morale / per-body-part yet; health only.

## 5. `OpenXcom.Unity` — exact-X-COM iso renderer & input

Reads converted atlases + `tiles.json`; reproduces `Battlescape/Map.cpp`.

- **Projection & draw order** (port of `Camera.cpp` `convertMapToScreen` +
  `Map.cpp` draw loop): 32-px diamond tiles, per-z vertical offset; screen pos
  ≈ `((x−y)·16, (x+y)·8 − z·24)` (exact constants from `Camera.cpp`). Paint
  back-to-front: `z` low→high, within a level by increasing `(x+y)`, per tile
  floor→west wall→north wall→object→unit→items. In Unity:
  `SpriteRenderer.sortingOrder = f(x,y,z,part)` reproduces exact overlap. Palette
  baked into atlas PNGs → no palette shader.
- **Animation:** tiles cycle 8 MCD frames at ~2 fps; units index by
  `direction × walkPhase` from the `UNITS` atlas; projectiles/explosions from
  `UFOGRAPH`.
- **Input:** screen→tile picking = inverse iso projection with height resolution
  (`Camera.cpp`). Click tile → Pathfinding preview (TU cost) → confirm → walk
  along `UnitMoved` path. Select unit → action menu → target → animate resolved
  shot.
- **HUD:** bottom bar reproduced from `UFOGRAPH/ICONS.PCK` — TU/energy/health/
  morale bars, snap/aimed/auto, kneel, reload, end-turn, level up/down. Reads
  live `BattleState`.
- **Renderer is a dumb replay** of Core's event queue: no rules, no gameplay
  decisions.

Renderer choice: **SpriteRenderer per tile-part with computed `sortingOrder`**
first (simple, debuggable); move to a batch `Graphics.DrawMesh` renderer only if
profiling a full 50×50×4 map demands it.

## 6. Testing strategy

`Tests.Standalone` (xUnit), run on every change, no Editor:
- **Combat math** — deterministic per seed; assert exact accuracy/damage/armor.
- **Pathfinding** — reachable set per TU budget; diagonal cost; walls/occupants block.
- **TileEngine LOS** — walls/height block sight; fog reveal set correct.
- **TurnManager** — TU/energy refresh; faction switching; win when one side wiped.
- **AiModule** — fixed seed+layout → expected move+shoot event sequence.
- **Converter** — golden tests: known PCK frame → pixel bytes; known MCD record →
  parsed flags; small `.rul` snippet → emitted JSON. Guards format drift.
- **Event stream** — whole turns assertable as ordered `BattleEvent` sequences.

Unity rendering/input verified by eye in the Editor (the ~5% that needs pixels).

## 7. Build sequencing (always runnable)

1. **Converter MVP** — palette + one terrain (PCK/MCD) + one unit set → atlases +
   JSON. Golden-tested.
2. **Static map render** — `MapGenerator` builds a `TileGrid` from real mapblocks;
   iso renderer draws it. *Pan around a real X-COM map.*
3. **Units** — spawn from deployment; select; move (Pathfinding + TU). *Playable movement.*
4. **Combat** — shooting, damage, death, fog/LOS. *A fight you can win.*
5. **Enemy AI + turn/win-loss** — full skirmish loop. *Slice complete.*

Each step ends runnable. If spare time runs out mid-project, something always works.

## 8. Existing scaffold

A draft of `OpenXcom.Core` already exists under `unity/Assets/Scripts/Core/`
(Position, Rng, RuleItem/Armor/Unit, Tile, TileGrid, BattleUnit, Combat) plus a
`Tests.Standalone` combat-math suite. It is **unverified** (no .NET SDK installed
yet). The implementation plan will fold it in, adjusting to this design where they
diverge (e.g. adding the `BattleEvent` stream and `DataLoader`).

## 9. Out of scope (this slice)

Geoscape, Basescape, research/manufacturing, inventory management screens, item
ammo/clips, throwing arcs, psi, morale/panic, TFTD, mod support, save/load,
audio, night/lighting palette shifts, sprite recoloring, voxel `LOFTEMPS` LOS,
trajectory-based hit deviation. Each is a future slice or explicitly cut.
