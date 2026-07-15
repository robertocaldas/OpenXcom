# Phase 8 — Voxel Line-of-Fire — Design Spec

**Date:** 2026-07-15
**Status:** Approved
**Project:** OpenXcom Extended (C++) → C#/Unity clean-slate rewrite.
**Parent spec:** `2026-07-04-battlescape-skirmish-design.md`
**Builds on:** Phase 4 (`2026-07-04-phase4-combat-los-design.md`) — the
tile-based FOV/LOS gate this phase does **not** replace.
**Feeds into:** Phase 9 (animations, weapon visuals, and a real bullet-sprite
projectile riding this phase's traced path) — see
`2026-07-15-phase9-animations-weapons-design.md`.

## 1. Goal & context

Phase 4 shipped LOS as an explicitly-labeled `[SIMPLIFIED]` tile-level
Bresenham walk, and shot resolution as an abstract accuracy-percent roll with
no notion of *what* a shot actually travels through or hits. This phase
replaces the resolution half with the real engine's voxel-level trajectory
system: shots are aimed at a (possibly deviated) 3D point, actually traced
through space, and whatever they hit first — terrain, a wall, an unintended
bystander, or the intended target — is the real outcome. This is a gameplay
accuracy upgrade (misses can go somewhere, cover can block a shot that tile
LOS would allow), not a rendering feature, and Phase 9's bullet-sprite visual
is built to animate along the exact path this phase computes rather than
approximating one.

**Guiding principle, same as Phase 7:** this is a port. Every constant and
algorithm below is grounded in the C++ source, cited by file:line, not
guessed. Where the C++ system is more elaborate than this rewrite currently
needs (e.g. per-loft-layer unit spotting checks used by night vision), the
simplification is called out explicitly rather than silently dropped.

## 2. Scope boundary: what this phase does NOT touch

The original engine runs **two separate systems** and this phase only ports
one of them:

- **Tile-level FOV/spotting** (`TileEngine::calculateTilesInFOV`,
  `TileEngine.cpp:1542`) decides *who a unit can see well enough to target at
  all*. Phase 4 already ported a simplified version of this
  (`TileEngine.ComputeVisibleTiles`, tile-`BlocksSight` Bresenham) and it
  stays exactly as-is — `BattleState.TryFire`'s existing LOS gate (has the
  attacker actually spotted the defender) is unchanged.
- **Voxel-level shot resolution** (`TileEngine::calculateLine`/`voxelCheck`,
  `TileEngine.cpp:4310-4630`) decides, *given* a shot is being fired at an
  already-visible target, what it actually hits. This is what this phase
  ports.

This split matches the real engine's own architecture (`visible()` at
`TileEngine.cpp:1847` is a distinct code path from `calculateLine`) and means
Phase 8 is additive to `TryFire`'s resolution step, not a rewrite of Phase
4's spotting logic.

## 3. Voxel data — conversion + Core model

**Coordinate system** (ported as-is,
`TileEngine.cpp:2431,4501,4560,4619-4620`): 16 voxel units per tile in X/Y,
24 in Z (a tile's vertical space is 12 loft-template layers of 2 voxel-units
each). A world voxel position is `(tileX*16 + localX, tileY*16 + localY,
tileZ*24 + localZ)`.

**`LOFTEMPS.DAT`** (currently unconverted — new to this phase): a flat
binary blob of 16-bit rows, each bit representing one occupied X column at a
given Y row within one 16×16 Z-layer "loft" template
(`MapDataSet::loadLOFTEMPS`, `MapDataSet.cpp:272`). New `Xcom.Convert`
decoder reads this into a flat `ushort[]` (one row per `(template, y)` pair,
16 templates... actual template count is data-driven, read the file length),
written to `unity/Assets/GameData/` as a JSON/binary sidecar the same way
other converted tables are.

**`MapDataTile.Loft`** (Core, new field): each terrain-part MCD record
already gets parsed for the fields it needs (`TuWalk`, `StopLOS`, etc. —
`MapDataTile.cs`); this phase adds `int[] Loft = new int[12]`, one
loft-template index per Z-layer within that tile-part, read from the MCD
record's existing (but currently unread) loft-index bytes.

**Unit loft radius** (`BattleUnit`, new): units are hit-tested as a cylinder,
not by their sprite — port `BattleUnit::getLoftemps()`
(referenced at `TileEngine.cpp:2116,2200,4619`), a per-armor loft-template
index (from `RuleArmor`) representing the unit's body-width footprint at
each Z-layer of its own height.

## 4. Trajectory tracing — `TileEngine.CalculateLine`

New method alongside the existing `HasLineOfSight`/`ComputeVisibleTiles`.
Ports `TileEngine::calculateLine` (`TileEngine.cpp:4310+`) and its helper
`voxelCheck` (`TileEngine.cpp:4529`): steps voxel-by-voxel along the 3D line
from an origin voxel to a target voxel (standard 3D DDA/Bresenham, not
X-COM-specific), and at each step bit-tests the current voxel against:

1. The tile's occupying terrain parts' `Loft[zLayer]` template (does this
   terrain part's bitmask have a bit set at this voxel's local X/Y?).
2. Any unit standing in that tile, against that unit's loft-radius cylinder
   (skipping the shooter itself, and skipping units already known dead).

Returns the first hit: which kind (terrain part / unit), its position, and
(for a unit hit) which body-part Z-band was struck (needed later for
per-part damage, not resolved by this phase — see §6). An unobstructed line
returns "reached target."

**Origin/target voxel selection**: port `getOriginVoxel`
(`TileEngine.cpp:5826`, shooter's eye/weapon-height voxel, kneeling-aware)
and the equivalent target-voxel center-of-mass helper — both are small,
already-isolated helper functions in the C++, safe to port directly without
pulling in unrelated `BattleAction` state this rewrite doesn't have.

## 5. Accuracy deviation

Port the aim-point scatter: a shot's rolled accuracy (already computed by
`BattleUnit.GetFiringAccuracy` — Phase 1/4, unchanged) maps to a deviation
angle/radius applied to the target voxel *before* tracing, so a "miss" is
really "aimed at a randomly-offset point, then traced for real." This is
what allows a miss to have a real consequence (hits terrain, hits a
different unit) instead of silently doing nothing.

**[SIMPLIFIED]**, scoped down from the full C++ deviation model
(`BattleActionAttack`/`Projectile::calculateTrajectory`'s accuracy-to-angle
formula pulls in weapon-specific spread modifiers this rewrite doesn't model
yet, e.g. per-shot-type spread for autofire): port the core
accuracy-percent → deviation-angle relationship only, using the weapon's
already-existing single accuracy value (`RuleItem.AccuracyFor`), not a
per-autofire-pellet variant. Autofire multi-shot spread stays future work.

## 6. Integration — `BattleState.TryFire`

Resolution sequencing changes: after the existing LOS+TU gates pass (Phase 4,
unchanged) and accuracy is rolled, `TryFire` now calls
`TileEngine.CalculateLine` with a deviated-or-true target voxel and applies
damage to whatever the trace actually hit (which may not be the unit that
was aimed at). `ProjectileFiredEvent` gains the full traced voxel path (a
`List<Position>`-equivalent in voxel space) so Phase 9 can animate a sprite
along it instead of interpolating a straight tile-to-tile line.

`Combat.ResolveShot`'s existing damage-roll math (Phase 1) is reused
as-is once the trace determines *what* was hit — this phase changes *what
gets hit*, not how much damage a hit deals.

## 7. What this phase does NOT include

- **No FOV/spotting changes.** Phase 4's tile-based `ComputeVisibleTiles`
  gate is unchanged; this phase only affects shot resolution once a target
  is already valid to fire at.
- **No autofire pellet-by-pellet spread.** Single deviated trace per shot,
  even for weapons whose `RuleItem` supports multi-shot actions (the
  multi-shot loop itself, if any, stays whatever Phase 1/4 already does per
  shot).
- **No arcing/thrown-item trajectories.** Grenades/throwing are out of scope
  per the parent spec; this phase is direct-fire weapons only.
- **No per-body-part wound/damage location.** The trace identifies *which*
  Z-band of a unit was hit; wiring that into `FatalWounds` by body part is
  future work (today's `FatalWounds` stays a flat counter).
- **No explosion voxel effects** (blast radius line-of-effect tracing) —
  direct-fire hit resolution only.
- **No night vision / darkness-dependent spotting** — carried over as
  deferred from Phase 4, untouched here.
- **No Unity-visible change by itself.** This phase is Core-only
  (`TileEngine`, `MapDataTile`, `BattleUnit`, `BattleState`,
  `Xcom.Convert`); `BattleController`'s `ProjectileFiredEvent` handler stays
  a `Debug.Log` until Phase 9 gives it something to draw.

## 8. Testing strategy

All-Core, fully unit-testable (`Tests.Standalone`, same discipline as Phase
4):

- `TileEngine.CalculateLine`: open line reaches target; a solid terrain
  `Loft` bit strictly on the path stops the trace at that voxel, returning
  the terrain hit, not the target; a unit's loft cylinder standing on the
  path is hit instead of terrain behind it; the shooter's own voxel is
  excluded; a line that grazes a corner (adjacent-tile diagonal case) is
  asserted against a known intervening-voxel set, not just a boolean —
  same discipline Phase 4 called out for its Bresenham walk.
- Deviation: a 100%-accuracy shot has zero deviation (traces exactly to the
  aim point); a low-accuracy shot's deviated point is bounded (doesn't wander
  arbitrarily far) — assert against a seeded `Rng`, not real randomness.
- `BattleState.TryFire`: a deviated miss that crosses another unit's tile
  damages *that* unit, not the original target (this is the core new
  behavior — needs an explicit test, since it didn't exist before); a clear
  shot with no obstruction still hits the intended target as before (no
  regression on the common case); `ProjectileFiredEvent`'s voxel path is
  non-empty and ends at the actual hit position, not always the original
  target position.

## 9. Self-review

- **Placeholder scan**: none — every ported piece cites C++ file:line;
  every simplification is labeled `[SIMPLIFIED]` with what's cut and why.
- **Consistency**: `Combat.ResolveShot`'s damage math is explicitly reused
  unchanged (§6) — this phase only changes hit *selection*, avoiding a
  contradiction with Phase 1's already-tested damage formula.
- **Scope check**: bounded to trajectory/hit-resolution only, per §7;
  explicitly does not touch FOV (§2) or Phase 9's visuals (this spec has no
  Unity-side work at all, confirmed in §7's last bullet).
- **Ambiguity check**: §2's two-system split is the one real architectural
  decision here (vs. e.g. replacing Phase 4's LOS wholesale) — resolved
  explicitly with a citation to the C++ having the same split, not left
  implicit.
