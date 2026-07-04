# OpenXcom → Unity (C#) — clean-slate rewrite

This folder is a **ground-up reimplementation** of the OXCE game in C#/Unity.
It is **not** a transpile of the C++ in `../src`. That C++ tree is used only as a
**reference spec** — we read it to get the real formulas (TU costs, hit chance,
damage) and reimplement them idiomatically in C#.

Decided constraints (see the conversation that started this):

- **Goal:** ship a real Unity game.
- **Fidelity:** clean slate. No mod support, no compatibility with original X-COM
  data files or existing save games. We keep the *good architecture ideas*, not
  the file formats.
- **Effort:** solo / spare time → we build in **vertical slices**, keeping a
  working, testable build at every step.

## Architecture

The single most important rule: **game logic has zero `UnityEngine` references.**

```
unity/
  Assets/Scripts/
    Core/            <- OpenXcom.Core: pure C#, NO UnityEngine. The actual game.
    Unity/           <- MonoBehaviours: rendering, input, UI. References Core.
  Tests.Standalone/  <- plain .NET test project. Compiles Core, runs xUnit.
                        Lives OUTSIDE Assets/ so Unity ignores it.
  docs/
    SPEC-battlescape.md  <- formulas distilled from the C++, with source citations.
```

Why: as a solo dev you cannot afford to boot the Unity Editor to check whether a
hit-chance tweak is right. Because `Core` is pure C#, you run `dotnet test` in
seconds and only open Unity to look at pixels.

We keep OXCE's best structural idea — the **Rules vs. Savegame split**:
- `Core/Rules/*`   — immutable templates (`RuleItem`, `RuleArmor`, `RuleUnit`).
  Later these get authored as Unity `ScriptableObject`s / JSON.
- `Core/Battle/*`  — mutable runtime state (`BattleUnit`, `BattleItem`, `BattleGame`).

## Current slice: Battlescape skirmish

A single playable tactical fight. Milestone checklist:

- [x] Core data model (grid, unit, rules) — pure C#
- [x] Combat math: firing accuracy + damage roll (unit-tested)
- [ ] A* pathfinding on a TU budget
- [ ] Line-of-sight / visibility
- [ ] Turn manager + minimal enemy AI (move + shoot)
- [ ] Unity glue: Tilemap render, click-to-move/shoot, end-turn UI

## Running the tests (no Unity needed)

```sh
cd unity/Tests.Standalone
dotnet test
```

Requires the .NET SDK (`dotnet`). Install via https://dotnet.microsoft.com or
`brew install --cask dotnet-sdk`.

## Opening in Unity

1. Install Unity Hub + a recent Unity 6 (or 2022 LTS) editor.
2. In Unity Hub: **Add project from disk** → select this `unity/` folder.
   (Unity generates `ProjectSettings/`, `Packages/`, and `.meta` files on first
   open. Those aren't committed yet — first open will create them.)
3. The `Core` scripts compile as an assembly definition (`OpenXcom.Core.asmdef`);
   `Unity` scripts reference it.
