# Phase 7 — Camera, HUD & Mouse Interaction — Design Spec

**Date:** 2026-07-10
**Status:** Approved
**Project:** OpenXcom Extended (C++) → C#/Unity clean-slate rewrite.
**Parent spec:** `2026-07-04-battlescape-skirmish-design.md`
**Builds on:** `2026-07-09-phase6-real-skirmish-design.md` (real data, `BattlescapeBootstrap`, first Editor run)

## 1. Goal & context

Phase 6 closed the loop end-to-end (real soldier/Sectoid data, a working
scene, `BattleController` driving select/move/fire/end-turn) but it plays
like a debug harness: a static camera, zero on-screen UI (all feedback is
`Debug.Log`), and no visual affordance for what's about to happen on a
click. This phase makes it feel like an actual Battlescape screen by
porting — not reinventing — three pieces of the original C++ engine:
camera movement (`src/Battlescape/Camera.cpp`), the icon bar
(`src/Battlescape/BattlescapeState.cpp`), and the pathfinding
cursor/preview (`src/Battlescape/Pathfinding.cpp`, `Map.cpp`).

**Guiding principle (user directive):** "this is a port, it should work
exactly the same way" — and specifically a port of **OpenXcom/OXCE's**
behavior (which itself supports arbitrary resolutions), not a pixel-locked
recreation of the original DOS game's fixed 320×200. Where OXCE has a
documented default (scroll speed, key bindings, drag-scroll), that default
is what gets ported, grounded in `Options.cpp`, not guessed.

## 2. Scope

One bundled phase, three subsystems, built and verified together in the
existing live Unity Editor session (Coplay MCP) the same way Phase 6 was:

1. **Camera** — edge-scroll, arrow-key scroll, center-on-unit, no zoom.
2. **HUD** — the icon bar (`ICONS.PCK`) at its exact original layout, plus
   a selected-unit info panel (name/TU/Health/Energy).
3. **Mouse interaction** — tile-selector cursor (`CURSOR.PCK`) and
   TU-cost-colored movement path preview (OXCE's separate bundled
   `Pathfinding.png` arrow sheet — see §5).

## 3. Camera — `CameraController`

New MonoBehaviour, ported from `Camera.cpp`. Ground truth for every
constant is `Options.cpp`'s **OXCE** defaults (the second `OptionInfo`
registration per key, which overrides vanilla OXC's):

| Behavior | Source | OXCE default |
|---|---|---|
| Edge-scroll trigger zone | `Camera::SCROLL_BORDER` | 5px |
| Edge-scroll diagonal zone (half-speed blend) | `Camera::SCROLL_DIAGONAL_EDGE` | 60px |
| Scroll speed | `Options::battleScrollSpeed` | 8 (px / `SCROLL_INTERVAL`) |
| Scroll tick interval | `Map::SCROLL_INTERVAL` | 15ms |
| Edge-scroll enabled | `Options::battleEdgeScroll` | `SCROLL_AUTO` (always on) |
| Drag-scroll button | `Options::battleDragScrollButton` | `0` (disabled) |
| Pan keys | `keyBattleLeft/Right/Up/Down` | Arrow keys |
| Center-on-unit key | `keyBattleCenterUnit` | Home |
| End-turn key | `keyBattleEndTurn` | Backspace |
| Zoom | — | none — the original Battlescape has no zoom |

Implementation notes:
- Runs every `Update()` (not a manual 15ms `Timer` like the original —
  Unity's frame loop replaces it), converting the original's
  px-per-tick constants into world-units-per-second via the existing
  `TileRenderer.PixelsPerUnit = 32` convention:
  `unitsPerSecond = (battleScrollSpeed / (SCROLL_INTERVAL / 1000f)) / PixelsPerUnit`.
- Edge-scroll: mirrors `Camera::mouseOver` — checks `Input.mousePosition`
  against the four screen edges (accounting for the icon bar's visible
  map height, i.e. edge-scroll only triggers within the map viewport, not
  over the icon bar), with the diagonal-zone half-speed blending ported
  as-is.
- Arrow keys: mirrors `Camera::keyboardPress`/`keyboardRelease` — held-key
  state, not single-press.
- Center-on-unit (Home key, and the HUD's Center button — see §4): calls
  a `CenterOnPosition(Position)` that snaps the camera's world position to
  the selected unit's tile, same as `Camera::centerOnPosition`.
- End Turn moves from `BattleController`'s current `Space` binding to
  `Backspace`, matching `keyBattleEndTurn`. This is a one-line fix to
  existing Phase 6 code, not new scope.
- Camera pan is clamped so the map never scrolls fully off-screen (ports
  `Camera::scrollXY`'s bounds clamp), using `BattlescapeMapView.Grid`'s
  `Width`/`Length` (already exposed since Phase 6 Task 5).
- No Options/settings screen: every constant above is hardcoded to the
  OXCE default. Making these user-configurable is future work.

## 4. HUD

**Canvas setup:** one `Canvas` (Render Mode: Screen Space - Overlay),
`CanvasScaler` mode **Constant Pixel Size** (not "Scale With Screen
Size") so the icon bar always renders at its native asset resolution —
this is how OpenXcom itself keeps UI elements pixel-native across
different configured resolutions, rather than stretching them.

**`IconBarView`:** anchored bottom-center. Background is the converted
`ICONS.PCK` (a single 320×56 frame — confirmed no companion `.TAB` file
exists, so `PckDecoder.Load` already takes the `nframes = 1` branch it
has since Phase 1/2). Every button is placed at its exact original pixel
rect, read directly from the hardcoded constructors in
`BattlescapeState.cpp` (all relative to the icon bar's own top-left
corner, e.g. `_btnKneel` at `(112, 16)` size `32×16`, `_btnEndTurn` at
`(240, 0)` size `32×16`). The map viewport (Camera's rendered area) fills
the remaining screen height above the icon bar, same relationship as the
original's `visibleMapHeight = screenHeight - iconsHeight`.

Buttons rendered (all at their real position/art), split by whether
Core has a real action to wire up:

- **Functional:** End Turn (`BattleState.EndPlayerTurn`), Center-on-unit
  (`CameraController.CenterOnPosition`).
- **Rendered but inert** (real icon art, non-interactive — no fake
  behavior stubbed in): Unit Up/Down, Map Up/Down, Show Map, Kneel,
  Inventory, Next Soldier, Show Layers, Help, Abort, Stats panel click
  target, reserve-TU-mode buttons. None of these have a corresponding
  `BattleState` action yet.

**Selected-unit info panel:** name text, TU/Health/Energy numeric
readouts and bar sprites, all bound to real `BattleUnit` fields
(`TimeUnits`, `Health`, `Energy` — all exist today, per
`Battle/BattleUnit.cs`). No Morale bar: `BattleUnit` has no morale field
(morale/panic is out of scope per the parent spec and Phase 6's deferred
list too) — the bar is omitted, not faked with a placeholder value.

## 5. Mouse interaction

The tile-selector cursor and the path-preview arrows come from **two
different** converted sprite sheets — corrected during planning after
tracing the actual draw calls in `Map.cpp` (an earlier draft of this spec
assumed both were `CURSOR.PCK`; they aren't):

- **Tile-selector cursor:** `CURSOR.PCK` (confirmed via `Mod.cpp:5718`,
  decoded at 32×40 — identical frame size to the unit sprites already
  handled by `PckDecoder.Load(..., 32, 40)`, so no new decoder path is
  needed). Highlights the tile under the mouse (ported from `Map.cpp`'s
  `CURSOR.PCK` frame draw at the hovered tile position — frames 0/1,
  flashing, `Map.cpp:1560`), using the same `IsoProjection.MapToScreen`
  math `TileRenderer`/`UnitRenderer` already use.
- **Path preview:** a *separate* sheet, OXCE's own bundled
  `Resources/Pathfinding/Pathfinding.png` (`Map.cpp:1298`'s
  `getSurfaceSet("Pathfinding")`, declared in `extraSprites.rul` as a
  384×80px, 12-column grid of 32×40 frames — already a plain true-color
  PNG, not an indexed `.PCK`, so no palette/PckDecoder involved, only a
  grid-slice conversion). While a unit is selected and the mouse hovers a
  reachable tile, draw one arrow sprite per step of the path from the unit
  to the cursor, colored by cumulative TU cost against the unit's
  remaining `TimeUnits` (yellow = affordable, red = not — a simplified
  two-color take on the original's red/yellow/green marker-color system,
  `Pathfinding::red`/`::yellow`, `Pathfinding.cpp:39-41`). **No Core
  changes required**: `Pathfinding.FindPath(grid, start, goal)`
  (`Battle/Pathfinding.cs:58`) is already a pure, non-mutating query that
  returns `List<PathStep>` (`Position` + `StepCost`) — `BattleState.TryMove`
  merely happens to call it and then commit the walk. The hover handler
  calls `FindPath` directly on every tile-hover change and renders the
  result; committing the move on click is unchanged from Phase 6.

## 6. `Xcom.Convert` additions

Three new sprite-sheet conversions in `ConvertJob`:

- `CURSOR.PCK`/`.TAB` (32×40) → `cursor.png` / `cursor.frames.json`, and
  `ICONS.PCK` (no `.TAB`, single 320×56 frame) → `icons.png` /
  `icons.frames.json` — both reuse the existing
  `PckDecoder.Load`/`AtlasWriter.Build`/`.Save` pipeline exactly as
  `SECTOID.PCK` was added in Phase 6 Task 2.
- `Resources/Pathfinding/Pathfinding.png` → `pathfinding.png` /
  `pathfinding.frames.json` — a new, much simpler path: since the source
  is already a true-color grid-laid-out PNG (not an indexed `.PCK`), this
  only copies the file and computes frame rects from the known 12×2 grid,
  no palette or `PckDecoder` involved. This file lives under a sibling
  data root (`unity/RawData/Resources/common/`, not the existing
  `unity/RawData/Resources/UFO/` `dataDir`), so `ConvertJob.Run` gains a
  third root parameter (`commonDir`) alongside the existing `dataDir`/
  `rulesDir`.

## 7. Verification

Same live, in-Editor discipline as Phase 6, via the connected Coplay MCP
Unity Editor session:

1. `mcp__coplay-mcp__check_compile_errors` after every code change.
2. `play_game`, then drive the scene: confirm the camera pans at all four
   screen edges and via arrow keys, Home centers on the selected unit,
   Backspace ends the turn (not Space).
3. Confirm the icon bar renders at the bottom in the correct layout, with
   only End Turn and Center clickable — clicking any other icon does
   nothing (not a crash, not a fake action).
4. Confirm the selected-unit panel's TU/Health/Energy numbers and bars
   track the real unit's stats as it moves/takes damage.
5. Confirm hovering a tile shows the selector cursor, and with a unit
   selected, hovering a reachable tile shows the path preview arrows in
   yellow (affordable) or red (not), matching `TimeUnits`.
6. `get_unity_logs` + `capture_scene_object`/screenshot to sanity-check
   visually.

## 8. What this slice does NOT include

Explicitly deferred, so a future session knows what's still stubbed:

- **No Options/settings menu.** Camera behavior constants (§3) are
  hardcoded OXCE defaults; no in-game way to change scroll speed, enable
  drag-scroll, or pick a resolution/UI-scale.
- **No functional Kneel, Inventory, Next/Prev Soldier, Show Layers, Show
  Map, Abort, reserve-TU-mode, or Map Up/Down.** These render with real
  icon art at the real position (per the user's explicit choice for HUD
  scope) but do nothing — `BattleState` has no corresponding action for
  any of them yet.
- **No Morale bar.** `BattleUnit` has no morale field; not faked.
- **No level up/down behavior.** `keyBattleLevelUp/Down` (PageUp/PageDown)
  aren't wired — moot this slice since CULTA00 is a single-Z-level map,
  and the HUD button for it is in the "rendered but inert" bucket.
- **No cursor mode switching** (walk cursor vs. aim cursor vs. throw
  cursor as distinct sprites) — the tile-selector cursor is a single
  mode; right-click-to-fire's targeting still has no visual cursor
  change of its own this slice.
- **General scope carried over from the parent spec, still deferred:**
  Geoscape, Basescape, research/manufacturing, inventory screens,
  throwing, psi, morale/panic, TFTD, mod support, save/load, audio,
  night/lighting, sprite recoloring, voxel LOS, trajectory-based hit
  deviation.
