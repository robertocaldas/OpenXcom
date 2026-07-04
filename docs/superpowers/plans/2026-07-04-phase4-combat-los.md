# Phase 4 (Combat & Line of Sight) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A unit can only fire at a target it can see (real LOS gating), a
fired shot spends TU and rolls hit/damage via the existing Phase 1 combat
math, a killed unit is removed from the tile grid, and `BattleState` exposes
a simple win/lose signal.

**Architecture:** A new `TileEngine` (LOS/FOV), a `Tile.Discovered` fog flag,
three new `BattleEvent` subtypes, `BattleState.TryFire` wiring
`Combat.ResolveShot` behind an LOS+TU gate, `BattleState.IsBattleOver`, and a
terminal Unity `BattleController` extension (unverified, no Editor).

**Tech Stack:** .NET 8, xUnit, a from-scratch integer Bresenham line walk
(generic algorithm, not X-COM-specific).

## Global Constraints

- This plan builds on Phases 1-3 (merged to `oxce-plus`): `Combat.ResolveShot`/
  `HitChance`/`HitSide` (`Battle/Combat.cs`), `BattleUnit.FireTuCost`/
  `CanSpend`/`Spend`/`Health`/`IsAlive` (`Battle/BattleUnit.cs`),
  `BattleState`/`BattleEvent` (`Battle/BattleState.cs`, `BattleEvent.cs`),
  `Tile.BlocksSight`/`Occupant` (`Battle/Tile.cs`), `TileGrid` indexer/`At`/
  `Width`/`Length` (`Battle/TileGrid.cs`), `Position.Distance` (Euclidean,
  `Common/Position.cs`), `Rng.Percent` (`Common/Rng.cs` — `chance<=0` always
  false, `chance>=100` always true, otherwise a d100 roll: this determinism
  at the extremes is how this plan's tests avoid RNG-seed guessing) — all
  reuse exactly as-is, do not rename or re-declare.
- `OpenXcom.Core` (`unity/Assets/Scripts/Core/`) must have zero `UnityEngine`
  references.
- LOS range cap: `MaxViewDistance = 20` (verified default,
  `src/Mod/Mod.cpp:424`). LOS blocking: per-tile `BlocksSight`, walked via a
  standard integer Bresenham line (from `(x0,y0)` to `(x1,y1)`, `z` held
  constant — single level only, matching Phase 2/3 data). This is
  deliberately coarser than `Pathfinding`'s per-edge wall-ownership
  convention — LOS and movement are different concerns, not required to
  match granularity. Do not "fix" this to match `Pathfinding` in this plan.
  You CAN always see your own tile and the target tile itself (a blocking
  wall/object on the target tile doesn't stop you from seeing it, only from
  seeing past it) — only strictly-intervening tiles block the line.
- No turn manager, no enemy AI, no per-body-part damage/fatal wounds/stun/
  morale, no projectile flight animation, no view-cone/facing-restricted
  FOV, no darkness/night vision — all explicitly deferred to later phases.
- TU is spent immediately once the LOS gate passes (not after a simulated
  projectile flight); a killed unit's tile occupancy is cleared
  synchronously in the same call (no death-animation state machine).
- Environment: the .NET SDK is at `~/.dotnet`; every `dotnet` command must be
  run as `export PATH="$HOME/.dotnet:$PATH" && dotnet ...`, from
  `unity/Tests.Standalone`.
- **Test-quality note, given this session's pattern (3 of 5 Phase 3 tasks
  needed fix rounds for tests that pass regardless of whether the logic is
  correct):** every test in this plan is designed to avoid that pattern —
  read each test's comment explaining WHY it can't pass vacuously before
  implementing it, and preserve that property if you adjust anything.
- This session's environment cannot open the Unity Editor. Tasks 1-3 are
  fully verified by `dotnet test`. Task 4 (`BattleController` extension)
  cannot be compiled or run here — its implementer and reviewer must say so
  explicitly rather than claim verification that didn't happen.

---

### Task 1: `TileEngine` — line of sight + fog-of-war reveal

**Files:**
- Create: `unity/Assets/Scripts/Core/Battle/TileEngine.cs`
- Modify: `unity/Assets/Scripts/Core/Battle/Tile.cs` (add one field)
- Test: `unity/Tests.Standalone/TileEngineTests.cs`

**Interfaces:**
- Consumes: `TileGrid` (indexer, `At`, `Width`, `Length`), `Tile.BlocksSight`
  (existing), `Position` (`Distance`, equality) — all existing.
- Produces (used by Task 2): `Tile.Discovered` (new `bool` field, default
  `false`), `OpenXcom.Core.Battle.TileEngine.MaxViewDistance` (`const int` =
  `20`), `TileEngine.HasLineOfSight(TileGrid grid, Position from, Position to) -> bool`,
  `TileEngine.ComputeVisibleTiles(TileGrid grid, Position from) -> HashSet<Position>`.

- [ ] **Step 1: Write the failing tests**

Create `unity/Tests.Standalone/TileEngineTests.cs`:

```csharp
using OpenXcom.Core.Battle;
using OpenXcom.Core.Common;
using Xunit;

namespace OpenXcom.Core.Tests
{
    public class TileEngineTests
    {
        [Fact]
        public void HasLineOfSight_OpenGrid_IsTrue()
        {
            var grid = new TileGrid(5, 5, 1);
            Assert.True(TileEngine.HasLineOfSight(grid, new Position(0, 0, 0), new Position(4, 4, 0)));
        }

        [Fact]
        public void HasLineOfSight_SeeingYourOwnTile_IsAlwaysTrue()
        {
            var grid = new TileGrid(3, 3, 1);
            var pos = new Position(1, 1, 0);
            Assert.True(TileEngine.HasLineOfSight(grid, pos, pos));
        }

        [Fact]
        public void HasLineOfSight_BlockingTileOnTheExactTracedBresenhamPath_Blocks()
        {
            // Hand-traced integer Bresenham path from (0,0,0) to (4,2,0):
            // (0,0) -> (1,1) -> (2,1) -> (3,2) -> (4,2). Placing the blocker
            // at (2,1) - a genuine intervening tile on THIS path, not just
            // "somewhere in the grid" - proves the exact path is walked,
            // not merely that some blocking check exists.
            var grid = new TileGrid(5, 3, 1);
            grid.At(2, 1, 0).BlocksSight = true;

            Assert.False(TileEngine.HasLineOfSight(grid, new Position(0, 0, 0), new Position(4, 2, 0)));
        }

        [Fact]
        public void HasLineOfSight_BlockingTileOffTheTracedPath_DoesNotBlock()
        {
            // Same start/end as above, but the blocker is at (1,0,0) - NOT
            // one of the 5 tiles the traced path actually visits. If the
            // line-walk were wrong (e.g. walked a different path, or wasn't
            // walking a path at all and just checked a bounding box), this
            // "off path" blocker might incorrectly block the line too.
            var grid = new TileGrid(5, 3, 1);
            grid.At(1, 0, 0).BlocksSight = true;

            Assert.True(TileEngine.HasLineOfSight(grid, new Position(0, 0, 0), new Position(4, 2, 0)));
        }

        [Fact]
        public void HasLineOfSight_TargetTilesOwnBlocksSight_DoesNotBlockSeeingIt()
        {
            var grid = new TileGrid(5, 5, 1);
            grid.At(4, 4, 0).BlocksSight = true; // the target tile itself

            Assert.True(TileEngine.HasLineOfSight(grid, new Position(0, 0, 0), new Position(4, 4, 0)));
        }

        [Fact]
        public void ComputeVisibleTiles_WithinRangeAndClear_IsVisibleAndMarkedDiscovered()
        {
            var grid = new TileGrid(25, 1, 1);
            var visible = TileEngine.ComputeVisibleTiles(grid, new Position(0, 0, 0));

            var withinRange = new Position(19, 0, 0); // distance 19 <= 20
            Assert.Contains(withinRange, visible);
            Assert.True(grid.At(19, 0, 0).Discovered);
        }

        [Fact]
        public void ComputeVisibleTiles_BeyondMaxViewDistance_IsExcludedEvenWithAClearLine()
        {
            var grid = new TileGrid(30, 1, 1);
            var visible = TileEngine.ComputeVisibleTiles(grid, new Position(0, 0, 0));

            var beyondRange = new Position(25, 0, 0); // distance 25 > 20
            Assert.DoesNotContain(beyondRange, visible);
        }

        [Fact]
        public void ComputeVisibleTiles_DiscoveredFlagIsPermanent_SurvivesALaterCallThatCannotSeeItAnymore()
        {
            var grid = new TileGrid(25, 25, 1);
            // First call from (0,0,0): (19,0,0) is visible and discovered.
            TileEngine.ComputeVisibleTiles(grid, new Position(0, 0, 0));
            Assert.True(grid.At(19, 0, 0).Discovered);

            // Second call from far away, where (19,0,0) is now out of range
            // and NOT in the returned visible set - but Discovered must stay
            // true (permanent fog-of-war reveal), unlike the transient
            // "currently visible" result.
            var visibleFromFarAway = TileEngine.ComputeVisibleTiles(grid, new Position(24, 24, 0));
            Assert.DoesNotContain(new Position(19, 0, 0), visibleFromFarAway);
            Assert.True(grid.At(19, 0, 0).Discovered);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test --filter "FullyQualifiedName~TileEngineTests"`
Expected: FAIL to build — `TileEngine` and `Tile.Discovered` do not exist yet.

- [ ] **Step 3: Write the implementation**

Modify `unity/Assets/Scripts/Core/Battle/Tile.cs` — add one field (keep
everything else in the file exactly as-is):

```csharp
        /// <summary>Permanent fog-of-war reveal: has any unit ever seen this tile? Never un-set once true.</summary>
        public bool Discovered;
```//(add this field inside the `Tile` class body, alongside the existing `Occupant`/`Floor` fields)

Create `unity/Assets/Scripts/Core/Battle/TileEngine.cs`:

```csharp
using System;
using System.Collections.Generic;
using OpenXcom.Core.Common;

namespace OpenXcom.Core.Battle
{
    /// <summary>
    /// Simplified line-of-sight / fog-of-war. Ports the tile-space walk idea
    /// from TileEngine::calculateLineTile (src/Battlescape/TileEngine.cpp:4310-4347)
    /// using a generic integer Bresenham line (not X-COM-specific), checking
    /// each intervening tile's own BlocksSight flag rather than the real
    /// engine's direction-dependent per-edge wall cache (a deliberate
    /// simplification - LOS and movement are different concerns and don't
    /// need matching granularity). [SIMPLIFIED] single level (z held
    /// constant), no voxel/darkness/view-cone modeling.
    /// </summary>
    public static class TileEngine
    {
        /// <summary>Vision range cap in tiles. Ports Mod's default maxViewDistance (src/Mod/Mod.cpp:424).</summary>
        public const int MaxViewDistance = 20;

        /// <summary>
        /// True if a straight line from `from` to `to` (same z-level) is
        /// unobstructed. You can always see your own tile and the target
        /// tile itself, even if the target tile has a blocking wall/object -
        /// only tiles strictly between the two can block the line.
        /// </summary>
        public static bool HasLineOfSight(TileGrid grid, Position from, Position to)
        {
            foreach (var pos in WalkLine(from, to))
            {
                if (pos == from || pos == to)
                    continue;

                var tile = grid[pos];
                if (tile == null || tile.BlocksSight)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// All in-bounds tiles within MaxViewDistance of `from` that have a
        /// clear line of sight. Each returned tile's Discovered flag is set
        /// permanently (fog-of-war reveal); the returned set itself is the
        /// transient "visible this instant" result.
        /// </summary>
        public static HashSet<Position> ComputeVisibleTiles(TileGrid grid, Position from)
        {
            var visible = new HashSet<Position>();

            for (int y = 0; y < grid.Length; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    var to = new Position(x, y, from.Z);
                    if (from.Distance(to) > MaxViewDistance)
                        continue;
                    if (!HasLineOfSight(grid, from, to))
                        continue;

                    visible.Add(to);
                    grid[to].Discovered = true;
                }
            }

            return visible;
        }

        /// <summary>Standard integer Bresenham line walk between two tile positions (z held constant at `from.Z`).</summary>
        private static IEnumerable<Position> WalkLine(Position from, Position to)
        {
            int x0 = from.X, y0 = from.Y, x1 = to.X, y1 = to.Y;
            int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            int x = x0, y = y0;

            while (true)
            {
                yield return new Position(x, y, from.Z);
                if (x == x1 && y == y1)
                    break;

                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x += sx; }
                if (e2 <= dx) { err += dx; y += sy; }
            }
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test --filter "FullyQualifiedName~TileEngineTests"`
Expected: PASS (8/8).

- [ ] **Step 5: Run the full test suite (regression check)**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test`
Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add unity/Assets/Scripts/Core/Battle/TileEngine.cs unity/Assets/Scripts/Core/Battle/Tile.cs unity/Tests.Standalone/TileEngineTests.cs
git commit -m "feat(core): TileEngine line-of-sight + fog-of-war reveal"
```

---

### Task 2: `BattleState.TryFire` + `IsBattleOver`

**Files:**
- Modify: `unity/Assets/Scripts/Core/Battle/BattleEvent.cs` (add 3 new types)
- Modify: `unity/Assets/Scripts/Core/Battle/BattleState.cs` (add `TryFire`,
  `IsBattleOver`)
- Test: `unity/Tests.Standalone/BattleStateFireTests.cs` (new file, separate
  from the existing `BattleStateTests.cs` — a distinct concern, easier to
  review on its own)

**Interfaces:**
- Consumes: `TileEngine.ComputeVisibleTiles` (Task 1), `Combat.ResolveShot`/
  `HitSide` (existing, Phase 1), `BattleUnit.FireTuCost`/`CanSpend`/`Spend`
  (existing), `BattleState.Enqueue` (existing).
- Produces (used by Task 3):
  - `OpenXcom.Core.Battle.ProjectileFiredEvent(BattleUnit attacker, BattleUnit defender, bool hit)` : `BattleEvent`, properties `Attacker`/`Defender`/`Hit`
  - `OpenXcom.Core.Battle.UnitHitEvent(BattleUnit unit, int damage, UnitSide side)` : `BattleEvent`, properties `Unit`/`Damage`/`Side`
  - `OpenXcom.Core.Battle.UnitDiedEvent(BattleUnit unit)` : `BattleEvent`, property `Unit`
  - `OpenXcom.Core.Battle.FireOutcome { NoLineOfSight, InsufficientTu, Fired }`
  - `OpenXcom.Core.Battle.FireResult { FireOutcome Outcome; ShotResult Shot; }`
  - `BattleState.TryFire(BattleUnit attacker, BattleItem weapon, BattleActionType action, BattleUnit defender) -> FireResult`
  - `BattleState.IsBattleOver` (`bool`, computed property)

- [ ] **Step 1: Write the failing tests**

Create `unity/Tests.Standalone/BattleStateFireTests.cs`:

```csharp
using OpenXcom.Core.Battle;
using OpenXcom.Core.Common;
using OpenXcom.Core.Rules;
using Xunit;

namespace OpenXcom.Core.Tests
{
    public class BattleStateFireTests
    {
        // Firing=100 against Rifle's AccuracyAimed=110 clamps to 100 ->
        // Rng.Percent(100) is always true (Rng.cs: chance>=100 always
        // hits), so this attacker's shots are deterministically guaranteed
        // to hit regardless of seed.
        private static BattleUnit MakeGuaranteedHitAttacker(Position pos)
        {
            var stats = new UnitStats { TimeUnits = 50, Health = 100, Firing = 100 };
            var unit = new BattleUnit(new RuleUnit("ATTACKER", stats, RuleArmor.None), Faction.Player)
            {
                Position = pos,
            };
            unit.RightHand = new BattleItem(RuleItem.Rifle);
            return unit;
        }

        // Firing=0 -> HitChance is always 0 -> Rng.Percent(0) is always
        // false (Rng.cs: chance<=0 always misses), deterministic regardless
        // of seed.
        private static BattleUnit MakeGuaranteedMissAttacker(Position pos)
        {
            var stats = new UnitStats { TimeUnits = 50, Health = 100, Firing = 0 };
            var unit = new BattleUnit(new RuleUnit("ATTACKER", stats, RuleArmor.None), Faction.Player)
            {
                Position = pos,
            };
            unit.RightHand = new BattleItem(RuleItem.Rifle);
            return unit;
        }

        private static BattleUnit MakeDefender(Position pos, int health)
        {
            var stats = new UnitStats { Health = health };
            return new BattleUnit(new RuleUnit("DEFENDER", stats, RuleArmor.None), Faction.Hostile)
            {
                Position = pos,
            };
        }

        private static (TileGrid grid, BattleState state) MakeOpenBattle(int width = 25)
        {
            var grid = new TileGrid(width, 1, 1);
            return (grid, new BattleState(grid));
        }

        [Fact]
        public void TryFire_TargetBeyondMaxViewDistance_ReturnsNoLineOfSightAndSpendsNoTu()
        {
            var (grid, state) = MakeOpenBattle(width: 30);
            var attacker = MakeGuaranteedHitAttacker(new Position(0, 0, 0));
            var defender = MakeDefender(new Position(25, 0, 0), health: 30); // distance 25 > MaxViewDistance (20)
            int startingTu = attacker.TimeUnits;

            var result = state.TryFire(attacker, attacker.RightHand, BattleActionType.AimedShot, defender);

            Assert.Equal(FireOutcome.NoLineOfSight, result.Outcome);
            Assert.Equal(startingTu, attacker.TimeUnits);
            Assert.Empty(state.DequeueEvents());
        }

        [Fact]
        public void TryFire_InsufficientTu_ReturnsInsufficientTuAndSpendsNoTu()
        {
            var (grid, state) = MakeOpenBattle();
            var attacker = MakeGuaranteedHitAttacker(new Position(0, 0, 0));
            attacker.TimeUnits = 5; // Rifle aimed shot costs 55% of 50 max TU = 27; 5 is not enough
            var defender = MakeDefender(new Position(3, 0, 0), health: 30);

            var result = state.TryFire(attacker, attacker.RightHand, BattleActionType.AimedShot, defender);

            Assert.Equal(FireOutcome.InsufficientTu, result.Outcome);
            Assert.Equal(5, attacker.TimeUnits);
            Assert.Empty(state.DequeueEvents());
        }

        [Fact]
        public void TryFire_GuaranteedMiss_SpendsTuAndEnqueuesOnlyAMissEvent()
        {
            var (grid, state) = MakeOpenBattle();
            var attacker = MakeGuaranteedMissAttacker(new Position(0, 0, 0));
            var defender = MakeDefender(new Position(3, 0, 0), health: 30);
            int expectedTuCost = attacker.FireTuCost(BattleActionType.AimedShot, attacker.RightHand);

            var result = state.TryFire(attacker, attacker.RightHand, BattleActionType.AimedShot, defender);

            Assert.Equal(FireOutcome.Fired, result.Outcome);
            Assert.False(result.Shot.Hit);
            Assert.Equal(50 - expectedTuCost, attacker.TimeUnits); // TU spent even on a miss

            var events = state.DequeueEvents();
            Assert.Single(events);
            var fired = Assert.IsType<ProjectileFiredEvent>(events[0]);
            Assert.False(fired.Hit);
            Assert.Same(attacker, fired.Attacker);
            Assert.Same(defender, fired.Defender);
        }

        [Fact]
        public void TryFire_GuaranteedHitButHugeDefenderHealth_NeverKillsRegardlessOfDamageRoll()
        {
            // Max possible damage from Rifle (Power=30) is Generate(0,200)*30/100,
            // capped at 200*30/100 = 60. A defender with 10000 health can
            // never die to this hit no matter what the RNG rolls - this test
            // is deterministic without needing to know/guess a specific seed.
            var (grid, state) = MakeOpenBattle();
            var attacker = MakeGuaranteedHitAttacker(new Position(0, 0, 0));
            var defender = MakeDefender(new Position(3, 0, 0), health: 10000);

            var result = state.TryFire(attacker, attacker.RightHand, BattleActionType.AimedShot, defender);

            Assert.Equal(FireOutcome.Fired, result.Outcome);
            Assert.True(result.Shot.Hit);
            Assert.False(result.Shot.Killed);
            Assert.Same(defender, grid.At(3, 0, 0).Occupant); // occupancy untouched

            var events = state.DequeueEvents();
            Assert.Equal(2, events.Count); // ProjectileFired + UnitHit, no UnitDied
            Assert.IsType<ProjectileFiredEvent>(events[0]);
            var hitEvent = Assert.IsType<UnitHitEvent>(events[1]);
            Assert.Same(defender, hitEvent.Unit);
            Assert.Equal(result.Shot.AppliedDamage, hitEvent.Damage); // event carries the actual rolled/applied damage, not a copy that could drift
        }

        [Fact]
        public void TryFire_GuaranteedHitOnOneHealthDefender_AtLeastOneSeedKillsAndClearsOccupancy()
        {
            // RollDamage's minimum possible roll is 0 (Generate(0,200) can
            // return 0), so a single fixed seed can't be *proven* lethal by
            // construction alone - this loops many independent seeds (fresh
            // state each time, matching CombatMathTests.cs's existing
            // AppliedDamageNeverNegative pattern in this codebase) and
            // requires that AT LEAST ONE actually kills, so the test can't
            // pass vacuously if the death-handling branch is never reached.
            bool anyKillObserved = false;

            for (uint seed = 1; seed <= 100; seed++)
            {
                var freshGrid = new TileGrid(25, 1, 1);
                var freshState = new BattleState(freshGrid, new Rng(seed));
                var attacker = MakeGuaranteedHitAttacker(new Position(0, 0, 0));
                var defender = MakeDefender(new Position(3, 0, 0), health: 1);
                freshGrid.At(3, 0, 0).Occupant = defender;

                var result = freshState.TryFire(attacker, attacker.RightHand, BattleActionType.AimedShot, defender);

                Assert.True(result.Shot.Hit); // accuracy is still 100 regardless of seed
                if (result.Shot.Killed)
                {
                    anyKillObserved = true;
                    Assert.Null(freshGrid.At(3, 0, 0).Occupant);
                    var events = freshState.DequeueEvents();
                    Assert.Equal(3, events.Count); // ProjectileFired + UnitHit + UnitDied
                    Assert.IsType<UnitDiedEvent>(events[2]);
                }
                else
                {
                    Assert.Same(defender, freshGrid.At(3, 0, 0).Occupant); // not killed -> occupancy untouched
                }
            }

            Assert.True(anyKillObserved, "Expected at least one of 100 seeds to produce a lethal hit on a 1-health defender.");
        }

        [Fact]
        public void IsBattleOver_FalseWithLivingUnitsOnBothSides()
        {
            var (grid, state) = MakeOpenBattle();
            state.Units.Add(MakeGuaranteedHitAttacker(new Position(0, 0, 0)));
            state.Units.Add(MakeDefender(new Position(1, 0, 0), health: 10));

            Assert.False(state.IsBattleOver);
        }

        [Fact]
        public void IsBattleOver_TrueWhenAllPlayerUnitsAreDead()
        {
            var (grid, state) = MakeOpenBattle();
            var player = MakeGuaranteedHitAttacker(new Position(0, 0, 0));
            player.Health = 0; // dead, but still in the Units list (list membership alone must not be used)
            state.Units.Add(player);
            state.Units.Add(MakeDefender(new Position(1, 0, 0), health: 10));

            Assert.True(state.IsBattleOver);
        }

        [Fact]
        public void IsBattleOver_TrueWhenAllHostileUnitsAreDead()
        {
            var (grid, state) = MakeOpenBattle();
            state.Units.Add(MakeGuaranteedHitAttacker(new Position(0, 0, 0)));
            var hostile = MakeDefender(new Position(1, 0, 0), health: 10);
            hostile.Health = 0;
            state.Units.Add(hostile);

            Assert.True(state.IsBattleOver);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test --filter "FullyQualifiedName~BattleStateFireTests"`
Expected: FAIL to build — `TryFire`, `IsBattleOver`, `FireResult`,
`FireOutcome`, and the 3 new event types do not exist yet.

- [ ] **Step 3: Write the implementation**

In `unity/Assets/Scripts/Core/Battle/BattleEvent.cs`, add these 3 classes
after the existing `TurnChangedEvent` (same file, same namespace, keep
everything already there unchanged):

```csharp
    public sealed class ProjectileFiredEvent : BattleEvent
    {
        public BattleUnit Attacker { get; }
        public BattleUnit Defender { get; }
        public bool Hit { get; }

        public ProjectileFiredEvent(BattleUnit attacker, BattleUnit defender, bool hit)
        {
            Attacker = attacker;
            Defender = defender;
            Hit = hit;
        }
    }

    public sealed class UnitHitEvent : BattleEvent
    {
        public BattleUnit Unit { get; }
        public int Damage { get; }
        public UnitSide Side { get; }

        public UnitHitEvent(BattleUnit unit, int damage, UnitSide side)
        {
            Unit = unit;
            Damage = damage;
            Side = side;
        }
    }

    public sealed class UnitDiedEvent : BattleEvent
    {
        public BattleUnit Unit { get; }

        public UnitDiedEvent(BattleUnit unit)
        {
            Unit = unit;
        }
    }
```

(Note: `UnitSide` is `OpenXcom.Core.Rules.UnitSide`, already `using`'d at the
top of `BattleEvent.cs` via `using OpenXcom.Core.Rules;` from the existing
`TurnChangedEvent`'s `Faction` reference — no new `using` needed.)

In `unity/Assets/Scripts/Core/Battle/BattleState.cs`, add these two types
above the `BattleState` class (alongside the existing `MoveOutcome`/
`MoveResult`, same file, same namespace):

```csharp
    public enum FireOutcome
    {
        NoLineOfSight,
        InsufficientTu,
        Fired,
    }

    public sealed class FireResult
    {
        public FireOutcome Outcome;
        public ShotResult Shot;
    }

```

Then add these two members inside the `BattleState` class, after `TryMove`:

```csharp
        /// <summary>
        /// Attempts to fire `weapon` from `attacker` at `defender`. Gated by
        /// line of sight (TileEngine.ComputeVisibleTiles) and TU budget, in
        /// that order - LOS is checked first since it costs nothing to check
        /// and shouldn't consume TU on a doomed attempt. TU is spent
        /// immediately once both gates pass; a kill clears the defender's
        /// tile occupancy synchronously (no death-animation state machine
        /// this phase). Always enqueues one ProjectileFiredEvent when a shot
        /// is actually fired (hit or miss), plus UnitHitEvent/UnitDiedEvent
        /// as applicable.
        /// </summary>
        public FireResult TryFire(BattleUnit attacker, BattleItem weapon, BattleActionType action, BattleUnit defender)
        {
            var visibleTiles = TileEngine.ComputeVisibleTiles(Grid, attacker.Position);
            if (!visibleTiles.Contains(defender.Position))
                return new FireResult { Outcome = FireOutcome.NoLineOfSight, Shot = ShotResult.Miss };

            int tuCost = attacker.FireTuCost(action, weapon);
            if (!attacker.CanSpend(tuCost))
                return new FireResult { Outcome = FireOutcome.InsufficientTu, Shot = ShotResult.Miss };

            attacker.Spend(tuCost);
            var shot = Combat.ResolveShot(Rng, attacker, weapon, action, defender);
            Enqueue(new ProjectileFiredEvent(attacker, defender, shot.Hit));

            if (shot.Hit)
            {
                var side = Combat.HitSide(attacker.Position, defender.Position);
                Enqueue(new UnitHitEvent(defender, shot.AppliedDamage, side));

                if (shot.Killed)
                {
                    Grid[defender.Position].Occupant = null;
                    Enqueue(new UnitDiedEvent(defender));
                }
            }

            return new FireResult { Outcome = FireOutcome.Fired, Shot = shot };
        }

        /// <summary>
        /// True when either faction has no living units left. Simplified
        /// port of BattlescapeGame::tallyUnits's core rule
        /// (src/Battlescape/BattlescapeGame.cpp:3361, "liveAliens == 0 ||
        /// liveSoldiers == 0"), ignoring the VIP-escort/must-destroy
        /// objective exceptions (not applicable to a plain skirmish). Checks
        /// each unit's IsAlive - dead units stay in the Units list, so list
        /// membership/count alone would be wrong here.
        /// </summary>
        public bool IsBattleOver =>
            !Units.Exists(u => u.Faction == Faction.Player && u.IsAlive) ||
            !Units.Exists(u => u.Faction == Faction.Hostile && u.IsAlive);
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test --filter "FullyQualifiedName~BattleStateFireTests"`
Expected: PASS (8/8).

- [ ] **Step 5: Run the full test suite (regression check)**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test`
Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add unity/Assets/Scripts/Core/Battle/BattleEvent.cs unity/Assets/Scripts/Core/Battle/BattleState.cs unity/Tests.Standalone/BattleStateFireTests.cs
git commit -m "feat(core): BattleState.TryFire (LOS+TU gated) and IsBattleOver"
```

---

### Task 3: `OpenXcom.Unity` — extend `BattleController` with fire input

**⚠️ This task cannot be compiled, run, or visually verified in this
development environment** — same reason as Phases 2-3's Unity tasks: no
Unity Editor, no UnityEngine assemblies available. Write this code to the
same standard of care as the rest of the plan; the implementer and reviewer
must both report this limitation explicitly. There is no test step in this
task for that reason — review is spec-compliance-by-reading only.

**Files:**
- Modify: `unity/Assets/Scripts/Unity/BattleController.cs`

**Interfaces:**
- Consumes: `OpenXcom.Core.Battle.{BattleState.TryFire, FireOutcome,
  ProjectileFiredEvent, UnitHitEvent, UnitDiedEvent}` (Task 2), existing
  `BattleController` fields (`_state`, `_selected`, `_unitTransforms`).
- Produces: nothing consumed by a later task (terminal task of this plan).

- [ ] **Step 1: Read the current file**

`unity/Assets/Scripts/Unity/BattleController.cs` currently has: a
`_selected` field for the clicked-and-selected unit, an `Update()` method
that left-clicks a unit to select it or a tile to move the selected unit
there via `_state.TryMove`, and a `DrainAndAnimate()` method that only knows
about `UnitMovedEvent`. This task adds a **right-click-to-fire** path
alongside the existing left-click-to-move path, and extends event draining
to also log/react to the 3 new combat events.

- [ ] **Step 2: Write the implementation**

In `unity/Assets/Scripts/Unity/BattleController.cs`, add a right-click
branch to `Update()` — insert this block immediately after the existing
`if (_selected == null) return;` check and before the existing
`var targetTile = TileUnderCursor(hit);` line (so right-click-to-fire is
checked before falling through to the existing left-click-to-move logic;
the existing `Input.GetMouseButtonDown(0)` gate at the top of `Update()`
must be loosened to also allow button 1 through — see the full replacement
below):

Replace the existing `Update()` method body with:

```csharp
        private void Update()
        {
            if (_animatingTransform != null)
            {
                AdvanceAnimation();
                return; // don't accept new input mid-animation
            }

            if (_state == null)
                return;

            if (Input.GetMouseButtonDown(1)) // right-click: fire at a targeted unit
            {
                HandleFireClick();
                return;
            }

            if (!Input.GetMouseButtonDown(0)) // left-click: select / move
                return;

            var ray = raycastCamera.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out var hit))
                return;

            var clickedUnit = FindUnitAt(hit.transform);
            if (clickedUnit != null)
            {
                _selected = clickedUnit;
                return;
            }

            if (_selected == null)
                return;

            var targetTile = TileUnderCursor(hit);
            var result = _state.TryMove(_selected, targetTile);
            if (result.Outcome == MoveOutcome.Failed)
                return;

            DrainAndAnimate();
        }

        private void HandleFireClick()
        {
            if (_selected == null || _selected.RightHand == null)
                return;

            var ray = raycastCamera.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out var hit))
                return;

            var target = FindUnitAt(hit.transform);
            if (target == null || target == _selected)
                return;

            _state.TryFire(_selected, _selected.RightHand, BattleActionType.AimedShot, target);
            DrainAndAnimate();
        }
```

Then extend `DrainAndAnimate()`'s event loop to also handle the 3 new event
types (keep the existing `UnitMovedEvent` branch unchanged, add the new
`else if` branches after it):

```csharp
        private void DrainAndAnimate()
        {
            foreach (var evt in _state.DequeueEvents())
            {
                if (evt is UnitMovedEvent moved && _unitTransforms.TryGetValue(moved.Unit, out var t))
                {
                    _animatingTransform = t;
                    _animationQueue = new Queue<Position>(moved.Path);
                    _animationTarget = t.localPosition;
                    AdvanceAnimation();
                }
                else if (evt is ProjectileFiredEvent fired)
                {
                    Debug.Log(fired.Hit
                        ? $"{fired.Attacker.Name} hits {fired.Defender.Name}"
                        : $"{fired.Attacker.Name} misses {fired.Defender.Name}");
                }
                else if (evt is UnitHitEvent hitEvent)
                {
                    Debug.Log($"{hitEvent.Unit.Name} takes {hitEvent.Damage} damage ({hitEvent.Side})");
                }
                else if (evt is UnitDiedEvent died && _unitTransforms.TryGetValue(died.Unit, out var deadTransform))
                {
                    deadTransform.gameObject.SetActive(false);
                    _unitTransforms.Remove(died.Unit);
                }
            }
        }
```

Add `using OpenXcom.Core.Rules;` to the top of the file if not already
present (needed for `BattleActionType.AimedShot`) — check the existing
`using` block first, since `Common.Position` is already imported and
`Rules` may not be.

- [ ] **Step 3: Note the verification gap (no code step — this is the record of it)**

There is no `dotnet test` step for this task. Record in the task report,
verbatim: "Task 3 could not be compiled or executed — no Unity Editor /
UnityEngine assemblies available in this environment. Reviewed for
spec-compliance by reading only."

- [ ] **Step 4: Commit**

```bash
git add unity/Assets/Scripts/Unity/BattleController.cs
git commit -m "feat(unity): BattleController fire input (right-click, unverified, no Editor access)"
```
