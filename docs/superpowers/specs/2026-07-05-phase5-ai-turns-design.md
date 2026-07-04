# Phase 5 — Enemy AI & Turn/Win-Loss — Design Addendum

**Date:** 2026-07-05
**Status:** Approved (author: Claude, operating autonomously per explicit user
delegation — see note below).
**Parent spec:** `docs/superpowers/specs/2026-07-04-battlescape-skirmish-design.md`
(§7, step 5: "Enemy AI + turn/win-loss — full skirmish loop. *Slice
complete.*")
**Builds on:** Phases 1-4 (converter, static map render, units & movement,
combat & LOS), all merged.

> **Process note:** as with Phases 2-4, the user authorized autonomous
> execution for this session. This addendum substitutes for interactive
> brainstorming Q&A.

## 1. Goal

Ending the player's turn runs a full, simplified enemy AI turn (each living
hostile unit either fires at the nearest visible player unit or moves toward
it), control then returns to the player, and the battle reports a clear
win/lose signal. This is the parent spec's final milestone for the *first
vertical slice* — after this phase, "full squad-vs-squad skirmish, real
maps, LOS/fog, cover/terrain, enemy AI, win/lose" (parent spec §1) is
entirely built.

## 2. What already exists — don't re-design

- `BattleState.IsBattleOver` (Phase 4) — already the exact win/lose rule
  this phase needs. This phase **calls** it at the right time; does not
  redefine it.
- `BattleState.TryFire`/`TryMove` (Phases 3-4) — AI decisions are expressed
  entirely as calls to these two existing, already-tested methods. AI does
  not duplicate hit-chance, TU-spend, or occupancy logic.
- `TileEngine.ComputeVisibleTiles` (Phase 4) — AI target-spotting reuses
  this exactly, the same way the player's implicit LOS gate does.
- `BattleUnit.RefreshForNewTurn()`, `WeaponFor(action)`, `IsAlive` (Phase 1)
  — reuse as-is.
- `Directions.Offsets` (`Common/Position.cs`) — 8-direction compass, indices
  `0=N, 2=E, 4=S, 6=W` are the 4 orthogonal ones. Reused for approach-tile
  selection (§3.2).

## 3. Verified algorithms (ported from the C++, this session)

### 3.1 Turn switching — simplified from `SavedBattleGame::endTurn`, `src/Battlescape/SavedBattleGame.cpp:1479-1533`

The real engine cycles **Player → Hostile → Neutral → Player** (a 3-way
cycle; the Neutral/civilian phase always runs even without civilians on the
mission). **[SIMPLIFIED]**: this slice has no civilian/neutral units at all
(established scope since Phase 1's `Faction` enum — `Player`/`Hostile`/
`Neutral` exists but nothing spawns as `Neutral` yet), so we collapse this
to a **2-way Player ↔ Hostile cycle**. Refresh-on-turn-start is confirmed
per-faction, not global: only units of the faction whose turn is *starting*
get `RefreshForNewTurn()` (`SavedBattleGame.cpp:1599-1602`,
`if (bu->getFaction() == _side) bu->prepareNewTurn();`) — already exactly
`BattleUnit.RefreshForNewTurn()`'s existing behavior, just needs to be
called for the right subset of units at the right time.

### 3.2 Minimal AI decision — simplified from `AIModule::think`, `src/Battlescape/AIModule.cpp:422`

The real engine's per-unit decision is a large state machine (Patrol/
Ambush/Combat/Escape modes, grenades, psi, `evaluateAIMode`,
`AIModule.cpp:581`). **[SIMPLIFIED]**, this phase ports only the reducible
core fallback chain confirmed this session: the AI shares the *exact same*
`TileEngine` visibility mechanism as the player (`selectNearestTarget`,
`AIModule.cpp:1376`, calls the same `visible()`/FOV system), and its
simplest decision is **"if a visible enemy exists and I can fire (LOS+TU),
shoot; otherwise move toward it"** (`setupAttack`/`projectileAction`'s
range+TU check, `AIModule.cpp:2464-2516`, falling back to `setupPatrol`'s
pathing, `:732`, when no attack is possible). Everything else (patrol when
no enemy is known, ambush positioning, grenades, psi, melee-vs-ranged
choice) is deferred — a hostile unit with no visible target simply does
nothing this turn.

Because our `Combat.HitChance`/`TryFire` model has **no hard weapon-range
cutoff** (only an accuracy drop-off past `UpperLimit`/`LowerLimit` — a shot
at extreme range just has very low, even 0%, hit chance, but `TryFire`
still reports `Fired`), "in range" collapses to "visible" for our
simplified model: if `TileEngine` says the target tile is visible, `TryFire`
will succeed (mechanically fire, regardless of actual hit odds) as long as
TU allows. This is a genuine simplification versus the real engine's
explicit range checks, not an oversight — documented here so it isn't
"discovered" and re-litigated mid-implementation.

**Approach-tile fix (a design decision made this session, not a straight
C++ port):** `Pathfinding`/`TryMove`'s destination-occupancy check
(`Tile.Occupant != null` blocks a step, including the final destination —
Phase 3's `Pathfinding.StepCost`) means a hostile unit can **never** path
directly onto a living target's own tile — `TryMove(unit, target.Position)`
would always fail to find a path there. The AI must instead approach one of
the target's 4 orthogonal neighbor tiles (using `Directions.Offsets[0,2,4,6]`
= N/E/S/W) that is walkable and currently unoccupied, and move there
instead. If the unit is already standing on one of those tiles (already
adjacent) but couldn't fire from there, the "move" step must **not** count
as progress (see the loop-safety note below) — the unit simply does nothing
further this turn.

**Loop-safety design decision:** a hostile unit's turn (§3.3) repeatedly
retries its decision until it "does nothing." Whether a fire attempt counts
as progress is unambiguous (a fired shot always spends TU > 0). Whether a
*move* attempt counts as progress is **not** simply "did `TryMove` not
report `Failed`" — `TryMove`'s documented "already at target" short-circuit
(Phase 3) returns `Outcome.Full` with an **empty path and zero TU spent**
when the unit is already standing on the requested destination. If the loop
treated that as progress, a unit already adjacent to a target it can't fire
at (e.g., blocked by a wall specifically at that angle) would loop forever
— `TryMove` to its own position "succeeds" every time without ever changing
anything. The fix: progress is `moveResult.Path.Count > 0` (something was
actually walked), not `moveResult.Outcome != Failed`. This was caught and
fixed during this session's design step, before any code was written — it's
called out explicitly here so an implementer doesn't "simplify it back" to
the more obvious-looking `!= Failed` check.

### 3.3 One hostile unit's turn — simplified from `BattlescapeGame::handleAI`, `src/Battlescape/BattlescapeGame.cpp:303-422`

The real engine lets each unit take AI actions (spending its own TU) until
it's idle or out of budget, then advances to the next living unit of the
active faction (`selectNextPlayerUnit`, referenced at `:313`/`:452`), and
ends the turn once no unit has anything left to do. **[SIMPLIFIED]**: no
per-unit action-count cap (the real engine caps at 2 AI actions per
`think()` call, `AIActionCounter`, `BattlescapeGame.cpp:311`) — this slice
just repeats "try to act" for one unit until it reports no progress (see
§3.2's loop-safety fix), then moves to the next hostile unit in whatever
order `BattleState.Units` already holds them (no priority/sorting).

### 3.4 Win/lose check timing — confirmed from `BattlescapeGame::endTurn`, `src/Battlescape/BattlescapeGame.cpp:652`

Confirmed this session: the tally/battle-end check runs **once, at the end
of a turn** — not after every individual kill. This phase's
`BattleState.EndTurn()` calls the already-existing `IsBattleOver` exactly
once per turn switch (both the Player→Hostile and the Hostile→Player
switch), matching this timing precisely.

## 4. Scope for this phase

**In scope:**
- `OpenXcom.Core`: `BattleOverEvent`/`BattleOutcome` (new — `PlayerVictory`/
  `HostileVictory`/`Draw`), `BattleState.EndTurn()` (switches
  `CurrentTurn`, refreshes the new faction's living units, enqueues
  `TurnChangedEvent`, checks `IsBattleOver` and enqueues `BattleOverEvent`
  if so), `AiModule` (`TakeTurn(state, unit) -> bool` for one unit's one
  decision cycle per §3.2, `RunHostileTurn(state)` looping all living
  hostile units per §3.3), `BattleState.EndPlayerTurn()` (drives
  `EndTurn()` → `RunHostileTurn()` → `EndTurn()`, short-circuiting if the
  battle ends after the first switch). All fully unit-tested.
- `OpenXcom.Unity`: extend `BattleController` with an "end turn" input
  (e.g. a keybind) calling `BattleState.EndPlayerTurn()` and draining the
  new events. Same verification caveat as Phases 2-4's Unity tasks:
  **cannot be compiled or run in this environment**, reviewed by reading
  only.

**Explicitly out of scope (this was the parent spec's LAST phase, but a few
things remain legitimately deferred to future slices/polish, not silently
dropped):** civilian/neutral units and their turn phase, per-unit AI action
caps, patrol/ambush/escape AI behaviors, grenades/psi/melee AI choices,
multi-level (z) AI pathing beyond what `Pathfinding already supports,
mission objectives beyond "wipe the other side," any UI beyond a bare input
binding (health bars, turn counter display, etc. are presentation polish).

**Known limitation, called out explicitly (same as every prior phase's
Unity task):** this environment cannot open the Unity Editor. All Core-side
work is fully verified by `dotnet test`. The `BattleController` "end turn"
extension cannot be visually verified this session.

## 5. Testing strategy

- `BattleState.EndTurn`: switches `CurrentTurn` correctly (`Player`↔
  `Hostile`); only refreshes units of the *new* current faction (a
  low-TU unit of the *other* faction is untouched); enqueues exactly one
  `TurnChangedEvent`; when the switch results in one side having zero
  living units, also enqueues exactly one `BattleOverEvent` with the
  correct `BattleOutcome`; when neither side is wiped, no `BattleOverEvent`
  is enqueued (assert the event list precisely, not just "no exception").
- `AiModule.TakeTurn`: a unit with a visible, in-LOS enemy and enough TU
  fires (assert via `state.DequeueEvents()` containing a
  `ProjectileFiredEvent`, not just a `true` return value) and returns
  `true`; a unit with no visible enemy returns `false` and enqueues
  nothing; a unit that can't fire (insufficient TU for the shot) but *can*
  move approaches an orthogonal-adjacent tile of the target (assert the
  unit's new `Position` is genuinely one of the 4 orthogonal offsets from
  the target, not just "some tile changed"); a unit already standing on
  such an adjacent tile that still can't fire returns `false` (the
  loop-safety case from §3.2 — this is the specific regression the
  `Path.Count > 0` fix guards against, so test it directly with a
  hand-built scenario, not just trust the code).
- `AiModule.RunHostileTurn`: processes every living hostile unit (a dead
  one is skipped); a unit with enough TU for multiple actions this turn
  (e.g. two shots) actually takes more than one action (assert more than
  one `ProjectileFiredEvent` for that unit, proving the repeat-until-no-
  progress loop actually loops, not just executes once).
- `BattleState.EndPlayerTurn`: a full round trip (Player ends turn, AI
  acts, turn returns to Player) with `CurrentTurn` back to `Player`
  afterward, assuming the battle didn't end; when the AI's actions cause
  `IsBattleOver` to become true, `EndPlayerTurn` does **not** attempt the
  second `EndTurn()` back to Player (assert `CurrentTurn` stays `Hostile`
  in that case, and only one `BattleOverEvent` total was enqueued, not a
  spurious second one from a turn-switch that shouldn't have happened).
- `BattleController` (Unity extension): not unit-tested; reviewed for spec
  compliance only.

## 6. Self-review

- Placeholder scan: none — every simplification is labeled `[SIMPLIFIED]`
  with its file:line source and reasoning.
- Consistency: reuses `IsBattleOver`/`TryFire`/`TryMove`/
  `ComputeVisibleTiles`/`RefreshForNewTurn` exactly as they exist — no
  renaming, no parallel reimplementation of combat/movement/vision math.
- Scope check: matches parent spec §7 step 5 exactly ("enemy AI + turn/
  win-loss — full skirmish loop, slice complete") without absorbing
  unrelated future-phase concerns (civilians, objectives, UI polish).
- Ambiguity check: the two genuine design decisions in this addendum (not
  pure C++ ports) — the orthogonal-approach-tile fix and the
  `Path.Count > 0` loop-safety condition — are both flagged explicitly with
  the reasoning, specifically so an implementer or reviewer doesn't
  "simplify" either one back into the more obvious-looking but actually
  broken version (targeting the enemy's own tile directly; using
  `Outcome != Failed` for move-progress).
- Given this project's session-long pattern of caught test-quality issues,
  the testing strategy above explicitly calls for asserting exact event
  types/counts (not just booleans) and for directly testing the
  already-adjacent-but-can't-fire loop-safety case with a hand-built
  scenario, since that's exactly the kind of edge case a naive test suite
  would skip.
