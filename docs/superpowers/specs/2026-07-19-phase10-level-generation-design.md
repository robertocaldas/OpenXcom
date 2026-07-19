# Phase 10 — Procedural Level Generation — Design Spec

**Date:** 2026-07-19
**Status:** Approved (design); pending implementation plan
**Project:** OpenXcom Extended (C++) → C#/Unity clean-slate rewrite, Phase 10.

## 1. Goal & constraints

Today the Unity project's only playable level is a single hardcoded
mapblock (`CULTA00`) with soldiers and Sectoids spawned at hardcoded
coordinates (`BattlescapeBootstrap.Start`). This phase replaces that with a
**real, randomly-assembled multi-block level**, generated the same way the
original engine builds one — a small terrain-scripting interpreter picks
and places pieces of terrain according to weighted rules, fitting them
together into a full map — run for real against the original farmland data
this project already has fully converted (`CULTA00`–`CULTA18`, all 19
blocks + their route files), plus two more pieces of real original data
worth converting for this scenario: a small UFO and the player's dropship.

Established constraints (carried over from the parent spec and confirmed by
the user this session):
- **Full interpreter, not a narrow one-script port.** The user chose the
  broader option: build the actual general-purpose interpreter (the real
  command dispatcher + data model for every command type the original
  supports), not a hand-rolled version of just the one script this phase
  happens to run. Two rarely-used command behaviors are explicitly deferred
  (§4) — parsed and represented, but not executed yet — since no data this
  phase converts would ever trigger them.
- **Squad composition stays as-is.** Per the user's explicit steer: 4 of the
  already-implemented soldiers vs. Sectoids (not deployment-driven alien
  variety, not deployment-driven squad size). This phase is about the
  *environment*, not the roster.
- **One fixed scenario, real data throughout.** Terrain = farmland
  (`CULTA`), UFO = the small scout, player craft = the Skyranger — the
  original engine's own "farm crash site" scenario, picked because every
  piece of it is real, already-available original data, not because the
  interpreter is limited to it.
- **Communication:** per the newly-added `CLAUDE.md` rule, chat updates
  during this phase describe things in plain terms ("the level-generation
  algorithm", "the block-picking logic") rather than naming C/C++ source
  symbols. This spec document itself still cites file:line — per the
  project's own porting-fidelity rule, that citation trail is how a future
  session verifies the port was done faithfully, and belongs in written
  design docs even though it doesn't belong in chat prose.

## 2. What "generate a level" means in the original engine

Reduced to its essentials, the original level-generation algorithm works
like this: a terrain (like farmland) names a **script** — an ordered list of
placement instructions. Each instruction says things like "place a piece of
terrain of about this size, up to N times, chosen at random from this
weighted pool" or "keep placing random pieces until there's no room left."
The generator tracks which grid cells are already filled, finds the empty
spots a given piece would actually fit into, and picks one at random. Doing
this for every instruction in order fills the whole map.

Farmland's real script is three instructions: place the UFO, place the
player's craft, then fill everything else with random farmland pieces
(weighted so some pieces show up more than others, each capped at 3 uses).
That's the exact scenario this phase generates.

The interpreter also understands several other instruction types the
original engine supports for other terrains (drawing a road/river-shaped
line of pieces, checking whether a piece was placed before deciding what to
do next, removing already-placed pieces, resizing the map mid-generation,
skipping an instruction some percentage of the time). Building the real
instruction-dispatch engine — not a special case for just the one script
above — means all of those are represented and (mostly) executable, so a
future phase that converts a second terrain's data has a real, working
interpreter to run its script against, not a rewrite.

Two instruction types are the exception — **parsed but not executed this
phase**: one digs connecting tunnels between already-placed pieces (used
only by base-facility/alien-base terrains), and one stacks multiple pieces
vertically into one column (same). Neither farmland's script nor any data
this phase converts ever uses them, and supporting them roughly doubles the
work for zero visible effect on the level this phase actually produces.
Adding real support later is a contained follow-up against already-shaped
data structures, not a redesign — same pattern this project has used for
every other explicitly-deferred feature so far.

## 3. Data conversion additions

Two more original mapblocks get converted, alongside the farmland set
already done:

| Piece | Original file | Terrain data | Real ruleset source |
|---|---|---|---|
| Small Scout UFO | `UFO1A.MAP` | `UFO1` set | `ufos.rul`, `STR_SMALL_SCOUT` |
| Skyranger (player craft) | `PLANE.MAP` | `PLANE` set | `crafts.rul`, `STR_SKYRANGER` |

Both are single 10×10 (UFO) / 10×20 (craft) pieces — no new decoder work,
same MAP/RMP/MCD pipeline already built for farmland.

A new small decoder converts the terrain-script rules themselves
(`mapScripts.rul`'s YAML) into the same kind of JSON the rest of the game
data already loads as — instruction type, size, target area, the
piece/weight/max-uses pool, grouping, execution count/chance, and the
success/failure labels instructions can react to.

## 4. Core: the level-generation interpreter

A new interpreter class walks an ordered instruction list exactly like the
original (`BattlescapeGenerator::generateMap`'s dispatch loop,
`src/Battlescape/BattlescapeGenerator.cpp:2862-3263`), operating over the
same kind of "which grid cells are already filled" bookkeeping the original
keeps.

**Implemented for real, each a direct port of the cited original logic:**
- Weighted, uses-limited random piece selection
  (`MapScript::getBlockNumber`/`getGroupNumber`,
  `src/Mod/MapScript.cpp:345-429`).
- Finding every empty spot a piece would fit and picking one at random
  (`BattlescapeGenerator::selectPosition`, `BattlescapeGenerator.cpp:4204-4262`).
- Placing a single piece (`::addBlock`, `BattlescapeGenerator.cpp:4429-4476`),
  filling all remaining space with random pieces (the `fillArea`
  instruction, `BattlescapeGenerator.cpp:3167-3197`), drawing a line-shaped
  run of pieces (`::addLine`, `BattlescapeGenerator.cpp:4318-4429`), placing
  the UFO/craft plus a landing-zone footprint around it (`::addCraft`,
  `BattlescapeGenerator.cpp:4271-4310`, reused for the UFO), checking
  whether a placement condition holds (`BattlescapeGenerator.cpp:3198-3233`),
  removing previously-placed pieces (`::removeBlocks`,
  `BattlescapeGenerator.cpp:4618+`), and resizing the map before any piece
  has been placed (`BattlescapeGenerator.cpp:3237-3260`).
- Instruction success/failure labels gating later instructions, and
  per-instruction execution-chance rolls (both part of the same dispatch
  loop cited above).

**Deferred, parsed but not executed** (§2): the wall-connecting/tunnel
instruction (`::digTunnel`/`::drillModules`, `BattlescapeGenerator.cpp:4487+`)
and vertical (stacked-level) piece placement
(`::populateVerticalLevels`/`::loadVerticalLevels`, `BattlescapeGenerator.cpp:3551-3752`).

The existing single-block tile-resolution code (today's `MapGenerator`)
becomes the per-piece step this interpreter calls once per placed piece, at
its real placement offset, instead of always at the origin.

## 5. Fixed scenario for this phase

Terrain = farmland, script = farmland's real 3-instruction script, UFO =
Small Scout, craft = Skyranger. Map size fixed at 50×50 — the size the
original typically generates a crash site at — rather than pulled from
mission-deployment data, since deployment conversion is out of scope this
phase (§1).

## 6. Unit spawning

Every placed piece's real route-node file gets merged into one map-wide
list, positioned at that piece's real placement offset — not the single
hardcoded node the old one-block level had room for. The 4 soldiers spawn
near the placed Skyranger; Sectoids spawn at real route nodes scattered
across the generated farmland — not filtered by node rank/type the way the
original filters alien spawns per-mission, since that filtering lives in
mission-deployment data this phase doesn't convert. Flagged `[SIMPLIFIED]`
in code, same convention as every other tagged deviation so far.

## 7. Testing

`Tests.Standalone` gains:
- Deterministic-seed tests for the piece-selection/placement-fitting logic
  (same seeded-random pattern already used throughout this project).
- An end-to-end test running the real farmland script against the real
  converted data, asserting the whole map ends up filled with no pieces
  overlapping.
- A test confirming the two deferred instruction types are recognized
  (parsed) but produce a clear "not implemented yet" signal rather than
  silently doing nothing.

## 8. Out of scope, explicit

- The two deferred instruction types (§2/§4) and anything that would
  exercise them (base-facility/alien-base terrains).
- Any terrain besides farmland.
- Deployment-driven map size, terrain choice, script choice, or alien
  roster/spawn-rank filtering (no mission-deployment data conversion this
  phase).
- Civilians.
- Multi-mission/Geoscape integration — this is still a single fixed
  skirmish scenario, like every prior phase.
