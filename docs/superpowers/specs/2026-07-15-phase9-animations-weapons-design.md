# Phase 9 — Unit Animation & Weapon Visuals — Design Spec

**Date:** 2026-07-15
**Status:** Approved
**Project:** OpenXcom Extended (C++) → C#/Unity clean-slate rewrite.
**Parent spec:** `2026-07-04-battlescape-skirmish-design.md`
**Builds on:** Phase 7 (`2026-07-10-phase7-camera-hud-input-design.md`) —
camera/HUD/mouse are done; units render but never turn or animate.
**Requires:** Phase 8 (`2026-07-15-phase8-voxel-trajectory-design.md`) — the
projectile visual (§5 below) animates along the real traced voxel path Phase
8 computes; this phase does not itself compute trajectories.

## 1. Goal & context

Every unit today renders as 4 static body-part sprites frozen at a
hardcoded south-facing standing frame (`UnitRenderer.cs`,
`BattlescapeBootstrap.cs:43-47`) — units never turn, never animate walking,
and vanish instantly on death. There is also no weapon visual of any kind
(no hand sprite, no firing pose) despite weapon *data* (Rifle, Plasma
Pistol) already driving real accuracy/TU-cost math. This phase makes combat
readable: units turn to face where they're going/shooting, walk through a
real animation cycle, die visibly, and the two starter weapons render in
soldiers'/Sectoids' hands with a firing pose and a real sprite-based bullet
that flies along Phase 8's traced path.

**Guiding principle, same as Phase 7/8:** a port, grounded in C++
file:line citations, with every simplification explicitly labeled.

## 2. Facing (`OpenXcom.Core`)

`BattleUnit.Direction` (0-7) already exists and is already unused —
`Directions.IndexOf` (`Common/Position.cs:59`, built for Phase 7's path
arrows) already computes a compass index from a step delta. This phase just
wires it:

- `BattleState.TryMove` sets `unit.Direction = Directions.IndexOf(step -
  previous)` for each path step walked.
- `BattleState.TryFire` sets `attacker.Direction` to face the defender
  (compass index of `defender.Position - attacker.Position`, snapped to the
  nearest of the 8) before resolving the shot, so the firing pose faces the
  right way even if the unit didn't just move.

No new fields, no new helper — both pieces already exist and are unused
today.

## 3. Walk-cycle & standing animation (`OpenXcom.Unity`)

Ported from `UnitSprite.cpp:897`'s documented layout: **8 directions × 8
walk-cycle frames**, plus one standing frame per direction, per body part,
per `drawRoutine` (soldiers and Sectoids both use `drawRoutine0`).

- `UnitRenderer` gains `SetFrame(direction, walkPhase)` (walkPhase `-1` =
  standing), replacing today's one-shot `Setup(...)` sprite assignment.
  Frame index becomes `partBase + direction*8 + max(walkPhase, 0)` for
  walking, `partBase + direction` for standing — same atlas, no new
  conversion needed (the walk frames are already present in the converted
  `units-XCOM_0`/`units-SECTOID` atlases, just never indexed past the
  standing block).
- `BattleController`'s existing `UnitAnimation`/`AdvanceAnimations` loop
  (already ticking a per-unit tile-to-tile position queue,
  `BattleController.cs:44-49`) gains a walk-phase counter that advances once
  per tile step (not per-frame-of-Update — matches the original's
  per-tile-not-per-pixel walk-frame stepping) and calls `SetFrame` each
  step. On queue-empty, calls `SetFrame(unit.Direction, -1)` (standing).

## 4. Death animation

`UnitDiedEvent` today just `SetActive(false)`s the unit's `GameObject`
instantly (`BattleController.cs:234-236`) — no animation at all. This adds a
short death-frame sequence from the same atlas (the original's dedicated
death-sprite block, adjacent to the walk-cycle frames per `drawRoutine`),
played via the same "step through N frames over a fixed duration, then
finish" mechanism built for the walk-cycle in §3, before deactivating the
`GameObject`. **[SIMPLIFIED]**: a single death sequence regardless of damage
type/facing (the original varies some death poses by cause); no ragdoll, no
corpse-item drop (inventory/corpse-as-item is out of scope per the parent
spec).

## 5. Weapon visuals

**Hand sprite (new conversion + Core field):** `HANDOB.PCK` (confirmed,
`Mod.cpp:5641`) gets a new `Xcom.Convert` decoder step — same
`PckDecoder.Load`/`AtlasWriter.Build`/`.Save` pipeline already used for
`SECTOID.PCK` (Phase 6 Task 2). `RuleYamlDecoder.LoadWeapon` gains
`handSprite` (mirrors `RuleItem::_handSprite`, `RuleItem.h:390`); `RuleItem`
(Core) carries it through. Only **Rifle and Plasma Pistol** get converted
hand sprites this phase — the two weapons already parsed into Core data
(`ConvertJob.cs:146-159`); other weapon types stay data-only with no visual,
same as today.

**Compositing:** `UnitRenderer` gains a 5th `SpriteRenderer` layer (the held
item), positioned/sorted alongside the existing 4 body parts, sourced from
`unit.RightHand`'s `RuleItem.HandSprite` — only rendered when `RightHand` is
non-null (both starter squads already always carry a weapon, per
`BattlescapeBootstrap.Spawn`).

**Firing pose:** one extra frame per direction, adjacent to the walk-cycle
block in the same atlas (the original's aim/fire frame). Held for a fixed
short duration when `ProjectileFiredEvent` fires, then reverts to standing —
reuses the exact "play N frames over time, then settle" mechanism from §3,
no new state machine.

## 6. Projectile visual

Ported from the real engine's `Projectiles` sprite set (`Mod.cpp:5730`, a
3×3px multi-frame trailing-dot sheet, selected per-weapon via
`RuleItem.BulletSprite`, default offset 35 per `loadSpriteOffset(...,
"Projectiles", 35)` — `RuleItem.cpp:353`) and `Map.cpp:762-778`'s draw loop
(a short multi-frame trailing streak, `BULLET_SPRITES` frames, following the
projectile's position each tick).

- New `Xcom.Convert` step locates and converts the `Projectiles` sprite
  source (the exact source file/rul entry needs confirming during planning —
  same kind of discovery Phase 7 did for `Pathfinding.png`) plus
  `RuleItem.BulletSprite` parsing (default 35 if unset, matching the C++
  default).
- A new `ProjectileView` (Unity) reads `ProjectileFiredEvent`'s voxel
  path (Phase 8 §6) and steps a short sprite-trail streak along it over a
  fixed short duration, using `IsoProjection`'s existing voxel/tile-to-screen
  math (already used by `TileRenderer`/`UnitRenderer`/Phase 7's cursor/path
  views) — no new projection math.
- **Hard dependency on Phase 8**: without it, `ProjectileFiredEvent` only
  carries attacker/defender/hit-or-miss (`BattleEvent.cs:38-49`), not a
  path — there is nothing to animate along. This is why Phase 9 is
  sequenced after Phase 8, not bundled with it.

## 7. `Xcom.Convert` additions summary

- `HANDOB.PCK`/`.TAB` → hand-sprite atlas (§5), same pipeline as existing
  unit-sprite conversion.
- `Projectiles` sprite source → bullet-trail atlas (§6) — exact source file
  TBD during planning (discovery task, same pattern as Phase 7's
  Pathfinding.png).
- `RuleYamlDecoder.LoadWeapon` gains `handSprite`, `bulletSprite` fields.

## 8. What this phase does NOT include

- **No inventory/drop visuals.** Weapons only ever render in a unit's hand;
  no floor/ground sprite, no inventory screen (out of scope per parent
  spec).
- **No weapons beyond Rifle + Plasma Pistol.** Other `RuleItem` entries
  stay data-only, no visual, same as today.
- **No autofire multi-shot visual sequencing** beyond what Phase 8's
  per-shot trace already produces (§6 of Phase 8) — one trail per resolved
  shot.
- **No melee weapon animation** — melee stays whatever Phase 1/4 already
  do (accuracy/TU math only, no swing pose).
- **No facing-restricted reaction fire** or opportunity-fire animation
  triggers — facing (§2) only affects the acting unit's own turn actions.
- **No death-pose variation by damage type**, no corpse/ragdoll (§4).
- **General scope carried over from the parent spec, still deferred:**
  Geoscape, Basescape, inventory screens, throwing, psi, morale/panic, TFTD,
  mod support, save/load, audio, night/lighting, sprite recoloring.

## 9. Verification

Same live in-Editor discipline as Phases 6/7 (Coplay MCP):

1. `check_compile_errors` after every code change.
2. `play_game`; move a unit in each of the 8 directions and confirm it
   turns and visibly steps through a walk cycle, not a static slide.
3. Fire at a visible enemy; confirm the shooter's firing pose plays facing
   the target, and a visible bullet-trail sprite travels from shooter to
   the actual hit point (which — post-Phase-8 — may not be the original
   target tile on a deviated miss).
4. Kill a unit; confirm a death-frame sequence plays before it's removed,
   not an instant vanish.
5. Confirm Rifle/Plasma-Pistol-equipped units show the weapon in-hand at
   all 8 standing directions; confirm units with no converted-weapon
   equipped (none exist in the current squads, but noted for future
   weapons) render without a crash — hand sprite is conditionally rendered,
   not assumed present.

## 10. Self-review

- **Placeholder scan**: one open item flagged honestly rather than
  guessed — §6/§7's exact `Projectiles` source file is marked "TBD during
  planning," matching how Phase 7 handled the same kind of discovery for
  Pathfinding.png rather than inventing a path now.
- **Consistency**: §3/§4/§5's firing-pose all explicitly reuse the same
  "step through N frames over fixed duration" mechanism rather than each
  proposing its own timer — avoids three parallel near-identical animation
  systems.
- **Scope check**: hard dependency on Phase 8 stated explicitly (§6) so
  this spec can't be implemented out of order.
- **Ambiguity check**: §5 explicitly limits hand-sprite/visual work to
  Rifle + Plasma Pistol only, avoiding "a few guns" being reinterpreted
  mid-implementation as an open-ended weapon list.
