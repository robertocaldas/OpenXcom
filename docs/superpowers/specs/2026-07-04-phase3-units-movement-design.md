# Phase 3 — Units & Movement — Design Addendum

**Date:** 2026-07-04
**Status:** Approved (author: Claude, operating autonomously per explicit user
delegation — see note below).
**Parent spec:** `docs/superpowers/specs/2026-07-04-battlescape-skirmish-design.md`
(§7, step 3: "Units — spawn from deployment; select; move (Pathfinding + TU).
*Playable movement.*")
**Builds on:** `docs/superpowers/specs/2026-07-04-phase2-static-map-render-design.md`
(Phase 2, merged: real `TileGrid` from a decoded mapblock, iso renderer).

> **Process note:** as with Phase 2, the user authorized autonomous execution
> for this session ("decide for yourself... do not stop to ask me anything").
> This addendum substitutes for interactive brainstorming Q&A.

## 1. Goal

Spawn a small squad of hardcoded X-COM soldiers onto the CULTA00 map (using
the real route nodes Phase 2 already decoded as spawn points), let the player
select one and click a destination tile, and have it walk there over one or
more turns' worth of TU spend — a real, testable A* pathfinding pass, not a
straight-line teleport. No combat, no AI, no fog/LOS yet (those are Phase 4/5
per the parent spec).

## 2. What already exists (Phase 1/2 scaffold, don't re-design)

- `BattleUnit` (`unity/Assets/Scripts/Core/Battle/BattleUnit.cs`) already has
  `Position`, `Direction`, `TimeUnits`, `CanSpend(tu)`, `Spend(tu)`,
  `RefreshForNewTurn()`. Reuse as-is.
- `Tile.Occupant` (`Battle/Tile.cs`) already exists as a `BattleUnit` field —
  Phase 3 is its first real user.
- `TileGrid.IsFree(Position)` already exists but currently only checks
  `Walkable`/`BlocksSight`/`Occupant` — and `Walkable`/`BlocksSight` are
  **not yet derived from real tile data** (Phase 2's `MapGenerator` only sets
  `Tile.Floor/WestWall/NorthWall/Object`, tracked as a follow-up). Task 1 of
  this plan fixes that.
- `Directions.Offsets`/`IsDiagonal(dir)` (`Common/Position.cs`) — 8-direction
  compass, already indexed N=0..NW=7 clockwise, odd indices are diagonals.
  Reuse for A* neighbor expansion.
- `RuleUnit.Soldier`/`RuleArmor.Personal`/`RuleItem.Rifle` presets already
  exist (`Rules/RuleUnit.cs` etc., from the original scaffold) — reuse for
  spawned units rather than inventing new stat blocks.

## 3. Verified formats/formulas (ported from the C++, this session)

### 3.1 Pathfinding cost model — `Pathfinding::getTUCost`, `src/Battlescape/Pathfinding.cpp:257-600`

The real function handles multi-tile unit sizes, stairs, ladders, falling,
flying, and diagonal corner-cutting checks — far beyond this slice's needs
(single-tile units, one flat level, `z=0` only, matching Phase 2's CULTA00
data which is `sizeZ=1`). **[SIMPLIFIED]**, deferred to a later slice: multi-
level movement (stairs/falling/flying), diagonal corner-blocking (checking
both orthogonal walls a diagonal move would "cut across"), and TFTD's
fire/smoke TU surcharge.

What we do port exactly:
- `DEFAULT_MOVE_COST = 4` (`Pathfinding.h:82`) — fallback TU cost when a
  floor's own walk cost is `0` (line 559-561: "some mods have broken tiles
  with 0 move cost... override to default").
- Base cost = destination tile's floor walk cost (`MapDataTile.TuWalk`,
  already decoded in Phase 2), falling back to `4` if `0`.
- Diagonal surcharge: `cost = cost * 3 / 2` for odd-indexed directions (`cost
  = (int)((double)cost * 1.5)`, `Pathfinding.cpp:570-573`) — matches
  `Directions.IsDiagonal(dir)`.
- Blocked (`INVALID_MOVE_COST`) if: destination out of grid bounds;
  destination already occupied (`Tile.Occupant != null` — the real engine
  only blocks on *known* units of a different faction or *any* same-faction
  unit, `Pathfinding.cpp:584-591`; we simplify to "any occupant blocks",
  documented below); destination has no floor at all (`Tile.Floor == null`).
- Wall blocking, orthogonal directions only **[SIMPLIFIED — diagonals skip
  wall checks entirely this slice]**: moving via a purely-N/S/E/W direction
  is blocked if the wall it crosses is present. Tile ownership: a tile's own
  `WestWall` sits between it and its western neighbor; its own `NorthWall`
  sits between it and its northern neighbor (`Tile.h`/`MapData.h` convention,
  confirmed via the O_WESTWALL/O_NORTHWALL naming and Phase 2's `MapGenerator`
  populating them per-tile). So: moving **W** or **N** crosses the *source*
  tile's own `WestWall`/`NorthWall`; moving **E** or **S** crosses the
  *destination* tile's `WestWall`/`NorthWall`. **[SIMPLIFIED]**: the real
  engine distinguishes passable "furniture-like" walls from fully-blocking
  ones via TU cost (a present wall can just add TU, not block); we treat any
  present wall on the crossed side as fully blocking this slice, since we
  don't yet have a clean signal to distinguish the two without deeper MCD
  semantics. Tracked as a follow-up for Phase 4 (LOS needs the same
  distinction for `blocksLOS` anyway).

### 3.2 A* algorithm

Standard grid A* (`open set ordered by f = g + h`, `h` = Chebyshev distance
to target since diagonal moves are allowed — matches `Position.ChebyshevDistance`
already in `Common/Position.cs`), `g` = accumulated TU cost via §3.1. This is
a from-scratch clean implementation of the *algorithm* (open/closed sets,
neighbor relaxation) — OXCE's own `PathfindingOpenSet`/`PathfindingNode`
classes are C++-specific priority-queue plumbing, not a formula to port
byte-for-byte the way MCD offsets are. Output: ordered list of `Position`
waypoints from start (exclusive) to goal (inclusive), or `null` if
unreachable.

### 3.3 Unit spawn points — `ROUTES/*.RMP` node `Type`/`Rank`

Real OXCE resolves spawn points via `AlienDeployment` ruleset data cross-
referenced with node `Rank`/`Type` — that requires `.rul` YAML parsing, out
of scope until the dedicated ruleset-conversion phase (parent spec §9). For
this slice: spawn the squad at whichever real `RouteNode`s exist in the
already-converted `mapblock-CULTA00.json`, in node order, up to the squad
size, with a flat "any node is a valid spawn" rule. **[SIMPLIFIED]** — real
deployment-based spawn-node filtering by rank/type is deferred to the phase
that adds `.rul` parsing. (Practical note: CULTA00 has exactly one route node,
per Phase 2's own verified data — so this slice's squad is one soldier
unless a bigger mapblock is swapped in later. That's fine; the mechanism is
what this phase proves, not squad size.)

### 3.4 `BattleEvent` stream — parent spec §4, first real implementation

Parent spec already designed this generically (`UnitMoved`, `TurnChanged`,
etc.) but Phase 2 never needed it (no state changes, static render only).
Phase 3 is its first real consumer:
```
UnitMoved(BattleUnit unit, IReadOnlyList<Position> path)
TurnChanged(Faction faction)
```
`BattleState` (new — the `SavedBattleGame`-equivalent container the parent
spec named but Phase 1/2 never needed) owns `TileGrid`, `List<BattleUnit>`,
current turn `Faction`, a seeded `Rng`, and an event queue (`Queue<BattleEvent>`,
drained by `DequeueEvents()`). Movement mutates `Tile.Occupant`/`BattleUnit.Position`
immediately and enqueues one `UnitMoved` per completed move action (not per
tile — matches the parent spec's "systems mutate state and push ordered
events" pattern at the action granularity, not the sub-step granularity).

## 4. Scope for this phase

**In scope:**
- `OpenXcom.Core`: fix `MapGenerator` to derive `Tile.Walkable`
  (`Floor != null && !Floor.NoFloor`) and `Tile.BlocksSight`
  (`WestWall?.StopLOS == true || NorthWall?.StopLOS == true || Object?.StopLOS == true`)
  from the resolved `MapDataTile`s (currently left at their `true`/`false`
  defaults — tracked Phase 2 follow-up).
- `OpenXcom.Core`: `BattleEvent` (base type + `UnitMoved`/`TurnChanged`
  records), `BattleState` (container + event queue + spawn-from-route-nodes),
  `Pathfinding` (A* per §3.1/3.2), `BattleState.TryMove(unit, target)` (runs
  Pathfinding, spends TU per step or fails/partial-moves if TU runs out
  partway, updates occupancy, enqueues `UnitMoved`). All fully unit-tested.
- `OpenXcom.Unity`: a `BattleController` MonoBehaviour — click a unit to
  select it (highight, e.g. tint), click a tile to path there (call
  `BattleState.TryMove`, drain events, animate `TileRenderer`/unit sprite
  position along the returned path). Same verification caveat as Phase 2
  Task 6: **cannot be compiled or run in this environment**, reviewed by
  reading only.

**Explicitly out of scope (later phases per parent spec §7):** combat,
LOS/fog-of-war, enemy AI, turn/win-loss flow beyond a bare `Faction` field,
multi-level movement (stairs/falling/flying), diagonal wall-corner blocking,
`.rul`-driven deployment/spawn rules, inventory, multiple mapblocks.

**Known limitation, called out explicitly (same as Phase 2):** this
environment cannot open the Unity Editor. The Core-side event stream,
`BattleState`, and `Pathfinding` are fully verified by `dotnet test`. The
`BattleController` MonoBehaviour cannot be visually verified this session —
correct-per-spec best-effort code, flagged at hand-off, not silently glossed
over.

## 5. Testing strategy

- `MapGenerator` (fix): real-data test — a tile with a decoded `NoFloor`
  floor record is not walkable; a tile whose wall/object has `StopLOS` blocks
  sight; a normal open tile is both walkable and sight-clear.
- `Pathfinding`: synthetic small grids (hand-built `TileGrid`s, not requiring
  real converted data) — straight line cost matches `4` per step; diagonal
  costs `6` (`4*3/2`); a full wall blocks the route around it; an occupied
  tile is untraversable; unreachable target returns `null`; a custom
  `TuWalk` value (e.g. `8`) is honored per-tile.
- `BattleState.TryMove`: partial move when TU insufficient for the full
  path (stops at the last affordable tile, spends exactly that much TU,
  `Position` matches where it actually stopped); full move spends the exact
  summed cost and enqueues one `UnitMoved` with the full path; blocked
  destination returns failure with no TU spent and no event.
- `BattleState` spawn: given a real converted `mapblock-CULTA00.json`
  (1 route node) and a 1-soldier squad, the unit's `Position` matches the
  node's real decoded `(X,Y,Z)`.
- `BattleController`: not unit-tested (requires the Editor); reviewed for
  spec compliance only.

## 6. Self-review

- Placeholder scan: none — every formula has a file:line citation.
- Consistency: `BattleUnit.Position/TimeUnits/CanSpend/Spend` reused exactly
  as they exist today (no renaming). `Tile.Occupant`, `TileGrid.IsFree`
  reused as-is.
- Scope check: matches parent spec §7 step 3 exactly ("spawn from
  deployment [simplified to route nodes]; select; move via Pathfinding+TU —
  playable movement"). No combat/LOS/AI creep.
- Ambiguity check: the one real design choice (not purely a C++ port) is
  collapsing "occupied by same-faction vs. known-hostile vs. unknown-hostile"
  into a flat "any occupant blocks" rule — resolved this way because faction
  visibility/spotting doesn't exist yet (that's Phase 4's LOS/fog work), so
  the real rule's distinctions have no data to key off yet.
