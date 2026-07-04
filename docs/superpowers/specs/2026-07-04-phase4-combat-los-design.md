# Phase 4 — Combat & Line of Sight — Design Addendum

**Date:** 2026-07-05
**Status:** Approved (author: Claude, operating autonomously per explicit user
delegation — see note below).
**Parent spec:** `docs/superpowers/specs/2026-07-04-battlescape-skirmish-design.md`
(§7, step 4: "Combat — shooting, damage, death, fog/LOS. *A fight you can
win.*")
**Builds on:** Phase 2 (static map render) and Phase 3 (units & movement),
both merged.

> **Process note:** as with Phases 2-3, the user authorized autonomous
> execution for this session. This addendum substitutes for interactive
> brainstorming Q&A.

## 1. Goal

A unit can only fire at a target it can actually see (real line-of-sight
gating, not "anyone on the map"), firing spends TU and rolls to hit/damage
using the combat math that already exists from Phase 1, a killed unit is
removed from the tile grid, and the battle exposes a simple "is it over"
signal (one side wiped). No turn manager, no AI, no per-body-part damage,
no morale — those stay Phase 5 (per parent spec §7 step 5) and later slices.

## 2. What already exists — don't re-design

- `Combat.HitChance`/`RollDamage`/`HitSide`/`ResolveShot`
  (`unity/Assets/Scripts/Core/Battle/Combat.cs`) — the full firing-accuracy
  and damage formula from Phase 1, already unit-tested
  (`CombatMathTests.cs`). This phase **wires** it into a real action, it does
  not touch the math itself.
- `BattleUnit.FireTuCost(action, weapon)`, `CanSpend`/`Spend` — reuse as-is.
- `BattleState` (`Battle/BattleState.cs`, Phase 3) — event queue,
  `TileGrid`/`Units`/`CurrentTurn`/`Rng`. This phase adds new event types and
  a new `TryFire` method to it; does not restructure it.
- `Tile.BlocksSight` (Phase 2/3, derived from `MapDataTile.StopLOS` on
  `WestWall`/`NorthWall`/`Object`) — already computed per-tile. Reuse
  directly; this phase's LOS walk consumes it, doesn't recompute it.
- `Position.Distance` (Euclidean) — already exists (`Common/Position.cs`),
  used for firing drop-off. Reuse for the vision-range cap too.

## 3. Verified formulas/algorithms (ported from the C++, this session)

### 3.1 Line of sight — simplified from `TileEngine::calculateLineTile`, `src/Battlescape/TileEngine.cpp:4310-4347`

The real engine precomputes a per-tile, per-direction blocking cache and
walks a Bresenham path through it, with a separate 3D-voxel check for
unit-vs-unit spotting (`visible()`, `TileEngine.cpp:1847`) and a
directional view-cone sweep bounded by `Mod::_maxViewDistance` (default
**20** tiles, `Mod.cpp:424`). **[SIMPLIFIED]**, deferred: the voxel-density
unit-spotting check, darkness/night vision caps, view-cone/facing
restriction (this slice checks a full circle, not a facing-bounded cone),
and the diagonal-wall corner-case asymmetry the real engine has.

What we port: a standard integer Bresenham line walk between two tile
positions (not X-COM-specific, a generic algorithm) at `z` held constant
(single level, matching Phase 2/3's data). At each tile the line passes
through (excluding the origin, since you can always see out of your own
tile), if that tile's `BlocksSight` is `true` **and it is not the target
tile itself** (you can see a wall/blocking object — you just can't see past
it), the line is blocked. **[SIMPLIFIED]**: this checks each tile's own
`BlocksSight` flag along the path rather than the real engine's
direction-dependent per-edge wall ownership (the convention
`Pathfinding.StepCost` uses for movement) — LOS and movement are different
concerns and don't need the same granularity; a coarser per-tile check for
sight is a reasonable, explicitly-labeled simplification.

Range cap: `Position.Distance(other) <= MaxViewDistance` where
`MaxViewDistance = 20` (verified default, `Mod.cpp:424`).

### 3.2 `calculateFOV` → `ComputeVisibleTiles`

The real engine's `calculateTilesInFOV` (`TileEngine.cpp:1542`) sweeps a
direction-bounded region and marks tiles as both transiently "visible this
instant" (`Tile::setVisible`) and permanently "discovered"
(`Tile::setDiscovered`, `1644-1651`) — two separate flags, one resets each
recalculation, one never un-sets. We port the **two-flag distinction** (a
permanent `Tile.Discovered` plus a freshly-computed visible set returned
each call) but simplify the sweep shape to "every in-bounds tile within
`MaxViewDistance`" (a full circle) rather than the real engine's
view-cone-by-facing region, since this slice has no facing-dependent combat
yet.

### 3.3 Shot resolution sequencing — simplified from `ProjectileFlyBState.cpp`/`BattlescapeGame::checkForCasualties`, `BattlescapeGame.cpp:716`

The real engine: spends TU once a trajectory is confirmed
(`ProjectileFlyBState.cpp:402`), resolves the hit later via a flying
projectile, and on death runs a multi-frame `UnitDieBState` collapse
animation before finally clearing the tile's occupant reference
(`UnitDieBState.cpp:276-301`). **[SIMPLIFIED]**: no projectile flight, no
death animation state machine — TU is spent immediately when firing is
attempted (after the LOS gate passes), the shot resolves synchronously via
the existing `Combat.ResolveShot`, and a killed unit's tile occupancy is
cleared immediately in the same call. This matches the established
Core→Unity event-stream pattern (systems mutate immediately, events are
pushed for Unity to animate at its own pace) rather than modeling the death
animation as game-logic state.

### 3.4 Win/lose check — simplified from `BattlescapeGame::tallyUnits`/`autoEndBattle`, `BattlescapeGame.cpp:2985,3340-3361`

The real engine ends the mission when `liveAliens == 0 || liveSoldiers == 0`
(ignoring VIP-escort/must-destroy objective exceptions, which don't apply to
a plain skirmish). We port exactly this rule as a computed property:
`BattleState.IsBattleOver` is true when no living `Faction.Player` unit
remains, or no living `Faction.Hostile` unit remains. **[SIMPLIFIED]**: no
full turn manager or mission-end state machine this phase (that's parent
spec §7 step 5) — just the boolean signal.

## 4. Scope for this phase

**In scope:**
- `OpenXcom.Core`: `TileEngine` (new — `HasLineOfSight`, `ComputeVisibleTiles`,
  `MaxViewDistance` constant), `Tile.Discovered` (new field), new
  `BattleEvent` subtypes (`ProjectileFiredEvent`, `UnitHitEvent`,
  `UnitDiedEvent`), `BattleState.TryFire` (LOS-gated, TU-gated, calls
  existing `Combat.ResolveShot`, clears occupancy on death, enqueues
  events), `BattleState.IsBattleOver`. All fully unit-tested.
- `OpenXcom.Unity`: extend `BattleController` with a fire input path
  (e.g. right-click a visible enemy to fire the selected unit's weapon) and
  drain the 3 new event types to animate/log them. Same verification caveat
  as Phases 2-3's Unity tasks: **cannot be compiled or run in this
  environment**, reviewed by reading only.

**Explicitly out of scope (later phases per parent spec §7):** turn
management/IGOUGO flow, enemy AI, per-body-part damage/fatal wounds/stun/
morale, projectile flight animation, view-cone/facing-restricted FOV,
darkness/night vision, multiple weapons/ammo, throwing.

**Known limitation, called out explicitly (same as Phases 2-3):** this
environment cannot open the Unity Editor. All Core-side work is fully
verified by `dotnet test`. The `BattleController` extension cannot be
visually verified this session.

## 5. Testing strategy

- `TileEngine.HasLineOfSight`: synthetic small grids — open line succeeds;
  a `BlocksSight` tile strictly between source and target blocks it; the
  target tile's own `BlocksSight` does NOT block seeing it; seeing your own
  tile always succeeds; a diagonal line correctly walks the Bresenham path
  (assert the exact set of intervening tiles for a known non-trivial
  line, not just the boolean result, to avoid a repeat of this session's
  vacuous-test pattern in the wall-blocking case specifically).
- `TileEngine.ComputeVisibleTiles`: a target beyond `MaxViewDistance` is
  excluded even with a clear line; a target within range and unblocked is
  included and its tile becomes `Discovered`; a tile that goes out of the
  currently-visible set on a later call **stays** `Discovered` (permanent
  reveal, tested by calling twice with different positions).
- `BattleState.TryFire`: no line of sight → `NoLineOfSight`, no TU spent, no
  event; insufficient TU (with LOS present) → `InsufficientTu`, no TU spent,
  no event (use a deterministic seeded `Rng` plus a guaranteed-hit weapon
  setup, or assert on the miss/hit-agnostic fields only, so the test doesn't
  depend on RNG); a hit that kills the defender clears `Tile.Occupant` at
  the defender's position and enqueues `UnitDiedEvent`; a non-lethal hit
  enqueues `UnitHitEvent` but leaves occupancy untouched; every fired shot
  (hit or miss) enqueues exactly one `ProjectileFiredEvent`.
- `BattleState.IsBattleOver`: false with living units on both sides; true
  when all `Player` units are dead; true when all `Hostile` units are dead;
  false when only some units on one side are dead (must check `IsAlive` per
  unit, not just unit-list membership — a unit list doesn't shrink on death
  in this design, only `Occupant`/`Health` change).
- `BattleController` (Unity extension): not unit-tested; reviewed for spec
  compliance only.

## 6. Self-review

- Placeholder scan: none — every algorithm has a file:line citation, and
  every simplification is explicitly labeled `[SIMPLIFIED]` with the reason.
- Consistency: reuses `Combat.ResolveShot`/`HitSide` and `BattleState`'s
  existing `Enqueue`/event-queue machinery exactly as they exist today —
  no renaming, no parallel reimplementation.
- Scope check: matches parent spec §7 step 4 exactly ("shooting, damage,
  death, fog/LOS... a fight you can win") without absorbing step 5's turn
  manager/AI.
- Ambiguity check: the one real design choice here (not purely a C++ port)
  is per-tile (not per-edge) `BlocksSight` for the LOS walk, chosen for
  simplicity over matching `Pathfinding`'s edge-based wall convention exactly
  — documented above as a deliberate, asymmetric-with-movement
  simplification, not an oversight.
- Given this session's pattern (3 of 5 Phase 3 tasks needed fix rounds for
  test-quality issues, specifically tests that pass regardless of whether
  the logic under test is correct), the testing strategy above explicitly
  calls out asserting the exact intervening-tile set for the Bresenham walk
  (not just a boolean), and calls out using unit-list membership + `IsAlive`
  correctly for `IsBattleOver` (a plausible bug is checking `Units.Count`
  instead of counting living units, since dead units stay in the list).
