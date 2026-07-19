# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project overview

This is **OpenXcom Extended (OXCE)**, a fork of OpenXcom — an open-source
reimplementation of the 1994 game "UFO: Enemy Unknown" / "X-COM: UFO Defense"
(and its sequel "X-COM: Terror From the Deep"), written in C++17 with SDL 1.2.
The engine is fully data-driven: gameplay content (units, items, crafts, maps,
research trees, UI layout, etc.) is defined in YAML rulesets shipped as "mods"
rather than hardcoded, and the engine loads/merges these at runtime.

`src/version.h` defines the current version (`OPENXCOM_VERSION_ENGINE
"Extended"`, currently 8.6.x). The game requires the original X-COM/TFTD
data files to run (not included in this repo — see README.md).

## Build

Requires SDL 1.2, SDL_mixer 1.2, SDL_gfx 1.2 (>=2.0.22), SDL_image 1.2, and a
C++17 compiler.

**CMake (Linux/macOS, the common path):**
```sh
cmake . -DCMAKE_BUILD_TYPE=Release
make -j$(nproc)
```
The binary and mod/resource data land under `bin/` (`bin/UFO`, `bin/TFTD`,
`bin/common`, `bin/standard`). Running from the build tree picks up `bin/`
automatically; see README.md for the OS-specific search paths for user/config/
data directories otherwise, or pass `-data`/`-user`/`-config` on the command
line.

**Other build entry points:**
- `Makefile.simple` — minimal non-CMake Makefile.
- `src/OpenXcom.2010.sln` / `.vcxproj` — Visual Studio 2010+ solution (Windows).
- Xcode project under `src/apple` (macOS), needs `src/apple/SDLMain.m`.
- `scripts/build-openxcom` — helper script that clones/builds a fresh checkout.

There is no separate lint step; code style is enforced by convention (see
below), with `.clang-format` and `.astylerc` available for reformatting.

## Tests

There is no automated unit/integration test suite in this repo. The only
CI workflow (`.github/workflows/nightly.test`) builds release binaries for
Windows/Linux/macOS on pushes to the `test` branch — it does not run tests.
Verification is done by building and manually exercising the game (or using
`src/Menu/TestState.cpp` / `TestPaletteState.cpp`, in-engine debug screens
for rendering/palette checks). When changing gameplay logic, prefer reasoning
from the ruleset/save-game data model and, where possible, exercising the
relevant screen in a running build.

## Code style

- Brace style: **Allman** (opening brace on its own line).
- Indentation: **tabs**, width 4 (`.editorconfig`, `.astylerc`).
- Pointer/reference alignment: left (`int *foo`, not `int* foo`... actually
  see `.clang-format`: `PointerAlignment: Left`).
- Header guards use `#pragma once`.
- Every source file carries the GPLv3 header comment block — match it in new
  files.
- Public API members are documented with `///` Doxygen-style comments (see
  `docs/Doxyfile.in` for the `doxygen` CMake target).
- Everything lives in the `OpenXcom` namespace.

## Architecture

### Core loop and state stack

`src/Engine/Game.{h,cpp}` is the heart of the engine: it owns the SDL
`Screen`, `Cursor`, `Language`, current `SavedGame`, and current `Mod`, and
drives a **stack of `State` objects** (`src/Engine/State.h`). Each `State`
subclass represents one full UI screen (e.g. `GeoscapeState`,
`BasescapeState`, a specific popup window). `Game::run()` pumps SDL events,
dispatches them to the top state's `handle()`, calls `think()`, and `blit()`s
the visible states each frame. Screens navigate by `pushState`/`popState`/
`setState` rather than by any router. `main.cpp` bootstraps SDL and pushes
the initial `Menu/StartState`.

UI screens are grouped by game area, one directory per area, each holding
`*State` (screen), and supporting view/widget classes:
- `src/Geoscape` — strategic globe view, UFO tracking, base management flow.
- `src/Basescape` — base facilities, soldiers, crafts, manufacturing, research.
- `src/Battlescape` — tactical combat: `BattlescapeGame` drives turn logic,
  `BattlescapeGenerator` builds the map from terrain/deployment rules,
  `AIModule` is alien AI.
- `src/Ufopaedia` — in-game encyclopedia.
- `src/Menu` — main menu, options, load/save.
- `src/Interface` — reusable low-level widgets (buttons, text, windows) used
  across all the above.

### Rules vs. save data

Two parallel object families are the key architectural split to understand:

- **`src/Mod`** — static, read-only template data parsed from YAML ruleset
  files (`Rule*` classes, e.g. `RuleItem`, `RuleUnit`, `RuleCraft`,
  `MapBlock`, `AlienDeployment`...). `Mod` (`src/Mod/Mod.h`/`.cpp`, ~1100
  lines) is the central registry: `Mod::loadAll()` → `loadMod()` →
  `loadFile()` walks every active mod's `.rul` files (resolved through
  `Engine/FileMap`) and merges them into these rule objects. Rulesets are
  parsed with the vendored `libs/rapidyaml` library.
- **`src/Savegame`** — mutable runtime state that gets serialized to/from
  save files (`SavedGame`, `SavedBattleGame`, `Base`, `Craft`, `Soldier`,
  `BattleUnit`, etc.). These reference the immutable `Rule*` objects for
  their static properties and store per-instance mutable state (current HP,
  position, inventory contents, research progress, ...).

Mod content on disk lives under `bin/`:
- `bin/UFO`, `bin/TFTD` — original-game resource repackaging rulesets.
- `bin/common` — shared resources (fonts, palettes, base language strings,
  shaders) not tied to a specific mod.
- `bin/standard` — the built-in optional mods (one directory per mod, each
  with a `.rul` file plus assets), e.g. `Aliens_Pick_Up_Weapons`,
  `UFOextender_Gun_Melee`, `XcomUtil_*`. These are good reference examples
  for the ruleset YAML schema and for how OXCE options are typically
  packaged as toggleable mods.

At runtime, users' own mods live in the user data directory's `mods/`
subfolder (see README.md for per-OS paths) and are merged the same way.

There is a large documentation about the ruleset at https://www.ufopaedia.org/index.php/Ruleset_Reference_Nightly_(OpenXcom)
Use it.

### Scripting engine

`src/Engine/Script.h`/`.cpp` implements OXCE's custom mini scripting
language ("y-script") that mods can embed in YAML to customize formulas
(damage calculation, AI behavior hooks, sprite recoloring, etc.) without
recompiling the engine. `ScriptParser`/`ScriptParserEvents` register typed
hook points that `Mod` classes expose (see `ModScript` in `Mod.h`); actual
mod-authored scripts are parsed and JIT-interpreted through this system.

### Other notable Engine pieces (`src/Engine`)

- `CrossPlatform` — OS-specific paths, argv parsing, crash dumps.
- `FileMap` — resolves a logical resource path across the active mod stack.
- `Options` — the global user-configurable settings singleton.
- `Language` / `LocalizedText` / `LanguagePlurality` — i18n; strings come
  from the YAML files under `bin/common/Language` and per-mod language
  files (translations are managed via Transifex, see `.tx/config`).
- `Surface` / `InteractiveSurface` / `Action` — SDL-backed drawing primitives
  and input event wrapper that every UI widget and `State` builds on.
- `md5.cpp`, `lodepng.cpp`, `libs/miniz` — vendored third-party utility code
  (checksums, PNG decode, zip/mod-archive handling).

## Unity rewrite (`unity/` subdirectory)

`unity/` contains a from-scratch C#/Unity rewrite of this engine — not a
transpile. It reuses the original 1994 X-COM graphics/rulesets (converted
offline) but has clean-slate code, no mod support, no save-compat with this
C++ engine. Treat it as a separate project sharing this repo; `src/` is only
a reference spec for its ports (formulas, file formats).

**THE MOST IMPORTANT RULE for this subproject: this is a PORT, not a
reimplementation.** Every algorithm, formula, and constant that has an
equivalent in the original engine MUST come from the C/C++ source in `src/`,
cited by file:line — never invented, guessed, or "improved" independently,
even when the original's approach looks obscure, roundabout, or when a
simpler alternative seems like it should produce an equivalent result. If a
ported feature looks visibly wrong once running, the fix is almost always to
go re-read the C++ source more carefully (a citation was missed, a formula
was mis-transcribed, a branch/edge-case was skipped) — **not** to devise a
new mechanism that happens to produce a similar-looking result. A concrete
example from this project: Phase 9's held-item sprite rendered visibly
detached from a short alien race's body. The first fix was an empirically
tuned pixel offset, found by trial-and-error against live screenshots — it
looked plausible and materially improved the symptom, but was wrong. Only
going back to `UnitSprite.cpp` and actually reading the surrounding code
turned up the real, already-existing mechanism (`itemR.offY += (soldierHeight
- unit->getStandHeight())`, `UnitSprite.cpp:577-585`) — a different,
smaller, provably-correct value with a one-line comment in the original
explaining exactly why it exists. If you genuinely cannot find where (or
whether) the original handles some situation after a real search, say so
explicitly and record it as a documented `[SIMPLIFIED]`/gap — do not fill
the hole with your own invented logic. When a human reports something still
looks/behaves wrong after a fix, treat that as a signal to go find the real
cited mechanism, not to tune your own guess further.

- `unity/Xcom.Convert` — offline .NET CLI that decodes original UFO data
  (`unity/RawData/`, gitignored) into PNG atlases + JSON under
  `unity/Assets/GameData/` (also gitignored).
- `unity/Assets/Scripts/Core` (`OpenXcom.Core` namespace) — pure C#, zero
  UnityEngine references (`noEngineReferences: true` asmdef), so gameplay
  logic is testable without the Editor.
- `unity/Assets/Scripts/Unity` (`OpenXcom.Unity` namespace) — MonoBehaviours
  (rendering/input). One-way dependency on Core. **No Unity Editor is open by
  default in this dev environment** — treat files here as uncompiled/unrun
  until proven otherwise. Check `mcp__coplay-mcp__list_unity_project_roots`
  first: if it returns this project, a live Editor is connected via the
  Coplay MCP plugin and can be driven directly (create scenes/GameObjects,
  press Play, read compile errors and logs) instead of hand-authoring `.unity`
  files blind. If it returns empty, no Editor is open — ask the user to
  launch Unity on `unity/` with Coplay connected before relying on in-Editor
  verification, or fall back to a documented blind hand-off.
- `unity/Tests.Standalone` — xUnit project (`dotnet test`), compiles Core +
  Convert directly. Primary verification loop. Needs
  `export PATH="$HOME/.dotnet:$PATH"` in non-interactive shells (.NET SDK is
  at `~/.dotnet`, not on PATH by default there).

Status: Phases 1-5 (converter, static map render, units/movement, combat &
LOS, enemy AI + turn/win-loss) are complete and merged — the first vertical
slice (a full squad-vs-squad skirmish loop) is done end-to-end at the code
level, but nothing wires it into an actual Unity scene yet (no bootstrap
calling `BattleController.Bind`/`BattleState.SpawnAtRouteNodes`), so it has
never been run. Design/plan docs for each phase live under
`docs/superpowers/specs/` and `docs/superpowers/plans/`
(`2026-07-04-battlescape-skirmish-design.md` is the parent spec).

**When writing a slice's design spec, always include an explicit "what this
slice does NOT include" section** (deferred features, simplified formulas,
hardcoded stand-ins left in place, data/entries not converted), not just what
it adds. This project is built as a long sequence of incremental slices
across many sessions with no persistent Editor state to inspect — the spec
is often the only record of what's real versus stubbed, so a future session
must be able to read one spec and know exactly what's left to build.

## Notes for making changes

- New gameplay behavior driven by data (numbers, toggles, new unit/item
  properties) should generally be added as ruleset fields parsed in
  `Mod::loadFile`/the relevant `Rule*` class, not hardcoded — this is the
  established pattern throughout the codebase and is what lets existing
  `bin/standard` mods toggle behavior without engine changes.
- When adding a field to a `Rule*` class, mirror the existing pattern: YAML
  key parsed in the class's `load()` method, with a sensible default, plus
  Doxygen `///` comment on the member.
- `CHANGELOG.txt` and `Extended.txt` document the version history and
  OXCE-specific behavior differences from vanilla OpenXcom; consult them
  when unsure whether a behavior is an intentional OXCE extension.
