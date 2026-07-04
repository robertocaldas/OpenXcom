# Phase 5 (Enemy AI & Turn/Win-Loss) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ending the player's turn runs a full, simplified enemy AI turn
(each living hostile unit fires at or approaches the nearest visible player
unit), control returns to the player, and the battle reports a win/lose
signal. This is the last phase of the parent spec's first vertical slice.

**Architecture:** `BattleState.EndTurn()` (turn switching + refresh +
win/lose check), a new `AiModule` (one unit's shoot-or-approach decision,
plus looping all hostile units), and `BattleState.EndPlayerTurn()` (drives
the full Player→Hostile→Player round trip). A terminal Unity
`BattleController` extension (unverified, no Editor).

**Tech Stack:** .NET 8, xUnit. No new external dependencies.

## Global Constraints

- This plan builds on Phases 1-4 (merged to `oxce-plus`): `BattleState`
  (`Grid`, `Units`, `CurrentTurn`, `Enqueue`/`DequeueEvents`, `TryMove`,
  `TryFire`, `IsBattleOver` — `Battle/BattleState.cs`), `BattleUnit`
  (`Position`, `TimeUnits`, `IsAlive`, `Faction`, `RefreshForNewTurn()`,
  `WeaponFor(action)`, `RightHand` — `Battle/BattleUnit.cs`), `TileEngine`
  (`Battle/TileEngine.cs`), `Pathfinding`/`MoveResult`/`MoveOutcome`,
  `Combat`, `TileGrid`/`Tile`, `Position` (`+` operator, `Distance`),
  `Directions.Offsets[8]` (`Common/Position.cs`) — all reuse exactly as-is,
  do not rename or re-declare.
- `OpenXcom.Core` (`unity/Assets/Scripts/Core/`) must have zero
  `UnityEngine` references.
- Turn cycle is a **2-way Player↔Hostile cycle** — `[SIMPLIFIED]`, no
  Neutral/civilian phase (this slice has no civilian units). Only units of
  the faction whose turn is *starting* get `RefreshForNewTurn()` called.
- Win/lose (`IsBattleOver`, already built in Phase 4) is checked exactly
  once per turn switch, matching `BattlescapeGame::endTurn`'s once-per-turn
  timing (verified this session, `src/Battlescape/BattlescapeGame.cpp:652`)
  — do not check it after every individual kill.
- AI decision (`AiModule`): a hostile unit fires at the nearest visible
  enemy if it can (reusing `TryFire`'s existing LOS+TU gate exactly, no
  duplicate range/LOS logic), otherwise moves toward one of the target's 4
  **orthogonal neighbor tiles** (never the target's own tile —
  `Pathfinding` always treats an occupied tile as blocked, including the
  final destination, so `TryMove(unit, target.Position)` can never
  succeed). `FindApproachTile` must exclude any occupied tile, which
  includes the *acting* unit's own current tile (it is always that tile's
  occupant) — this is what guarantees the per-unit action loop
  terminates (every progress-reporting action spends TU > 0; see Task 2's
  code comment for the full reasoning, already verified during design, not
  something to re-derive from scratch while implementing).
- No civilian/neutral units, no per-unit AI action-count cap, no patrol/
  ambush/escape AI behaviors, no grenades/psi/melee AI choices — all
  explicitly deferred.
- Environment: the .NET SDK is at `~/.dotnet`; every `dotnet` command must
  be run as `export PATH="$HOME/.dotnet:$PATH" && dotnet ...`, from
  `unity/Tests.Standalone`.
- This session's environment cannot open the Unity Editor. Tasks 1-2 are
  fully verified by `dotnet test`. Task 3 (`BattleController` extension)
  cannot be compiled or run here — its implementer and reviewer must say so
  explicitly rather than claim verification that didn't happen.

---

### Task 1: `BattleState.EndTurn` + `BattleOverEvent`

**Files:**
- Modify: `unity/Assets/Scripts/Core/Battle/BattleEvent.cs` (add 2 new types)
- Modify: `unity/Assets/Scripts/Core/Battle/BattleState.cs` (add `EndTurn`)
- Test: `unity/Tests.Standalone/BattleStateTurnTests.cs` (new file)

**Interfaces:**
- Consumes: `BattleState.CurrentTurn`/`Units`/`Enqueue`/`IsBattleOver`
  (existing), `BattleUnit.Faction`/`IsAlive`/`RefreshForNewTurn()`
  (existing), `Faction` enum (existing).
- Produces (used by Task 2/3):
  - `OpenXcom.Core.Battle.BattleOutcome { PlayerVictory, HostileVictory, Draw }`
  - `OpenXcom.Core.Battle.BattleOverEvent(BattleOutcome outcome)` : `BattleEvent`, property `Outcome`
  - `BattleState.EndTurn()`

- [ ] **Step 1: Write the failing tests**

Create `unity/Tests.Standalone/BattleStateTurnTests.cs`:

```csharp
using OpenXcom.Core.Battle;
using OpenXcom.Core.Common;
using OpenXcom.Core.Rules;
using Xunit;

namespace OpenXcom.Core.Tests
{
    public class BattleStateTurnTests
    {
        [Fact]
        public void EndTurn_SwitchesPlayerToHostile()
        {
            var grid = new TileGrid(5, 5, 1);
            var state = new BattleState(grid);
            Assert.Equal(Faction.Player, state.CurrentTurn);

            state.EndTurn();

            Assert.Equal(Faction.Hostile, state.CurrentTurn);
        }

        [Fact]
        public void EndTurn_OnlyRefreshesUnitsOfTheNewCurrentFaction()
        {
            var grid = new TileGrid(5, 5, 1);
            var state = new BattleState(grid);
            var player = new BattleUnit(RuleUnit.Soldier, Faction.Player);
            var hostile = new BattleUnit(RuleUnit.Sectoid, Faction.Hostile);
            player.TimeUnits = 1;
            hostile.TimeUnits = 1;
            state.Units.Add(player);
            state.Units.Add(hostile);

            state.EndTurn(); // Player -> Hostile

            Assert.Equal(1, player.TimeUnits); // untouched - not the new current faction
            Assert.Equal(hostile.Stats.TimeUnits, hostile.TimeUnits); // refreshed to max
        }

        [Fact]
        public void EndTurn_EnqueuesExactlyOneTurnChangedEvent()
        {
            var grid = new TileGrid(3, 3, 1);
            var state = new BattleState(grid);

            state.EndTurn();

            var events = state.DequeueEvents();
            Assert.Single(events);
            var turnChanged = Assert.IsType<TurnChangedEvent>(events[0]);
            Assert.Equal(Faction.Hostile, turnChanged.Faction);
        }

        [Fact]
        public void EndTurn_WhenHostileSideIsAlreadyWiped_EnqueuesPlayerVictoryBattleOverEvent()
        {
            var grid = new TileGrid(3, 3, 1);
            var state = new BattleState(grid);
            var player = new BattleUnit(RuleUnit.Soldier, Faction.Player);
            var deadHostile = new BattleUnit(RuleUnit.Sectoid, Faction.Hostile);
            deadHostile.Health = 0;
            state.Units.Add(player);
            state.Units.Add(deadHostile);

            state.EndTurn(); // Player -> Hostile; Hostile side is already fully wiped

            var events = state.DequeueEvents();
            Assert.Equal(2, events.Count); // TurnChanged + BattleOver
            Assert.IsType<TurnChangedEvent>(events[0]);
            var over = Assert.IsType<BattleOverEvent>(events[1]);
            Assert.Equal(BattleOutcome.PlayerVictory, over.Outcome);
        }

        [Fact]
        public void EndTurn_WhenNeitherSideWiped_NoBattleOverEvent()
        {
            var grid = new TileGrid(3, 3, 1);
            var state = new BattleState(grid);
            state.Units.Add(new BattleUnit(RuleUnit.Soldier, Faction.Player));
            state.Units.Add(new BattleUnit(RuleUnit.Sectoid, Faction.Hostile));

            state.EndTurn();

            var events = state.DequeueEvents();
            Assert.Single(events); // only TurnChanged
            Assert.IsType<TurnChangedEvent>(events[0]);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test --filter "FullyQualifiedName~BattleStateTurnTests"`
Expected: FAIL to build — `BattleState.EndTurn`, `BattleOverEvent`,
`BattleOutcome` do not exist yet.

- [ ] **Step 3: Write the implementation**

In `unity/Assets/Scripts/Core/Battle/BattleEvent.cs`, add these 2 types
after the existing `UnitDiedEvent` (same file, same namespace, keep
everything already there unchanged):

```csharp
    public enum BattleOutcome
    {
        PlayerVictory,
        HostileVictory,
        Draw,
    }

    public sealed class BattleOverEvent : BattleEvent
    {
        public BattleOutcome Outcome { get; }

        public BattleOverEvent(BattleOutcome outcome)
        {
            Outcome = outcome;
        }
    }
```

In `unity/Assets/Scripts/Core/Battle/BattleState.cs`, add this method inside
the `BattleState` class, after `IsBattleOver`:

```csharp
        /// <summary>
        /// Switches CurrentTurn (Player&lt;-&gt;Hostile - [SIMPLIFIED] 2-way
        /// cycle, no Neutral/civilian phase since this slice has no
        /// civilian units), refreshes TU/energy for every living unit of
        /// the NEW current faction only (port of SavedBattleGame::endTurn's
        /// per-faction refresh, src/Battlescape/SavedBattleGame.cpp:1599-1602),
        /// enqueues one TurnChangedEvent, then checks IsBattleOver exactly
        /// once (matching the real engine's end-of-turn-only tally timing,
        /// BattlescapeGame.cpp:652) and enqueues one BattleOverEvent if the
        /// battle just ended.
        /// </summary>
        public void EndTurn()
        {
            CurrentTurn = CurrentTurn == Faction.Player ? Faction.Hostile : Faction.Player;

            foreach (var unit in Units)
                if (unit.Faction == CurrentTurn && unit.IsAlive)
                    unit.RefreshForNewTurn();

            Enqueue(new TurnChangedEvent(CurrentTurn));

            if (IsBattleOver)
            {
                bool playerAlive = Units.Exists(u => u.Faction == Faction.Player && u.IsAlive);
                bool hostileAlive = Units.Exists(u => u.Faction == Faction.Hostile && u.IsAlive);
                var outcome = playerAlive && !hostileAlive ? BattleOutcome.PlayerVictory
                    : !playerAlive && hostileAlive ? BattleOutcome.HostileVictory
                    : BattleOutcome.Draw;
                Enqueue(new BattleOverEvent(outcome));
            }
        }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test --filter "FullyQualifiedName~BattleStateTurnTests"`
Expected: PASS (5/5).

- [ ] **Step 5: Run the full test suite (regression check)**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test`
Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add unity/Assets/Scripts/Core/Battle/BattleEvent.cs unity/Assets/Scripts/Core/Battle/BattleState.cs unity/Tests.Standalone/BattleStateTurnTests.cs
git commit -m "feat(core): BattleState.EndTurn (turn switching, refresh, win/lose event)"
```

---

### Task 2: `AiModule` — minimal shoot-or-approach AI

**Files:**
- Create: `unity/Assets/Scripts/Core/Battle/AiModule.cs`
- Test: `unity/Tests.Standalone/AiModuleTests.cs`

**Interfaces:**
- Consumes: `BattleState.TryFire`/`TryMove`/`Units`/`Grid` (existing),
  `TileEngine.ComputeVisibleTiles` (existing), `BattleUnit.IsAlive`/
  `Faction`/`Position`/`WeaponFor(action)` (existing), `Position.Distance`/
  `+` operator, `Directions.Offsets[8]` (existing), `TileGrid` indexer
  (existing), `Tile.Occupant`/`Floor` (existing), `MapDataTile.NoFloor`
  (existing).
- Produces (used by Task 3): `OpenXcom.Core.Battle.AiModule.TakeTurn(BattleState state, BattleUnit unit) -> bool`,
  `OpenXcom.Core.Battle.AiModule.RunHostileTurn(BattleState state)`.

- [ ] **Step 1: Write the failing tests**

Create `unity/Tests.Standalone/AiModuleTests.cs`:

```csharp
using OpenXcom.Core.Battle;
using OpenXcom.Core.Common;
using OpenXcom.Core.Rules;
using Xunit;

namespace OpenXcom.Core.Tests
{
    public class AiModuleTests
    {
        private static TileGrid MakeCorridor(int width)
        {
            var grid = new TileGrid(width, 1, 1);
            var floor = new MapDataTile { TuWalk = 4 };
            for (int x = 0; x < width; x++)
                grid.At(x, 0, 0).Floor = floor;
            return grid;
        }

        [Fact]
        public void TakeTurn_VisibleEnemyWithEnoughTu_FiresAndReturnsTrue()
        {
            var grid = new TileGrid(10, 10, 1);
            var state = new BattleState(grid);
            var hostile = new BattleUnit(RuleUnit.Sectoid, Faction.Hostile) { Position = new Position(0, 0, 0) };
            hostile.RightHand = new BattleItem(RuleItem.Rifle);
            var player = new BattleUnit(RuleUnit.Soldier, Faction.Player) { Position = new Position(3, 0, 0) };
            grid.At(0, 0, 0).Occupant = hostile;
            grid.At(3, 0, 0).Occupant = player;
            state.Units.Add(hostile);
            state.Units.Add(player);

            bool result = AiModule.TakeTurn(state, hostile);

            Assert.True(result);
            var events = state.DequeueEvents();
            Assert.Contains(events, e => e is ProjectileFiredEvent);
        }

        [Fact]
        public void TakeTurn_NoVisibleEnemy_ReturnsFalseAndEnqueuesNothing()
        {
            var grid = new TileGrid(50, 50, 1);
            var state = new BattleState(grid);
            var hostile = new BattleUnit(RuleUnit.Sectoid, Faction.Hostile) { Position = new Position(0, 0, 0) };
            var player = new BattleUnit(RuleUnit.Soldier, Faction.Player) { Position = new Position(49, 0, 0) }; // distance 49 > MaxViewDistance (20)
            state.Units.Add(hostile);
            state.Units.Add(player);

            bool result = AiModule.TakeTurn(state, hostile);

            Assert.False(result);
            Assert.Empty(state.DequeueEvents());
        }

        [Fact]
        public void TakeTurn_CannotAffordToFire_MovesToOrthogonalApproachTileInstead()
        {
            // Hostile starts EAST of the target at x=8; target at x=5.
            // FindApproachTile checks the target's N/E/S/W neighbors in that
            // order - N/S are out of bounds in this 1-row grid, so it picks
            // E=(6,0,0) first. That tile is reachable from x=8 without
            // passing through the target's own occupied tile (8->7->6).
            var grid = MakeCorridor(10);
            var state = new BattleState(grid);
            var hostile = new BattleUnit(RuleUnit.Sectoid, Faction.Hostile) { Position = new Position(8, 0, 0) };
            hostile.RightHand = new BattleItem(RuleItem.Rifle);
            hostile.TimeUnits = 10; // enough for 2 move steps (4 each) but not an aimed shot (Sectoid TimeUnits=54 * TuAimed 55% = 29)
            var player = new BattleUnit(RuleUnit.Soldier, Faction.Player) { Position = new Position(5, 0, 0) };
            grid.At(8, 0, 0).Occupant = hostile;
            grid.At(5, 0, 0).Occupant = player;
            state.Units.Add(hostile);
            state.Units.Add(player);

            bool result = AiModule.TakeTurn(state, hostile);

            Assert.True(result);
            Assert.Equal(new Position(6, 0, 0), hostile.Position); // walked 8->7->6, 2 steps * 4 TU = 8
            Assert.Equal(2, hostile.TimeUnits); // 10 - 8
        }

        [Fact]
        public void TakeTurn_UnreachableApproachTile_ReturnsFalseWithNoChange()
        {
            // Hostile at x=0 (no weapon - skips straight to the move
            // fallback), target at x=5 in a 1-row corridor. FindApproachTile
            // picks E=(6,0,0) first (N/S out of bounds) - but reaching x=6
            // from x=0 requires stepping through x=5, which is occupied by
            // the target itself. There is no way around it in a 1-row grid,
            // so the move must fail entirely.
            var grid = MakeCorridor(10);
            var state = new BattleState(grid);
            var hostile = new BattleUnit(RuleUnit.Sectoid, Faction.Hostile) { Position = new Position(0, 0, 0) };
            var player = new BattleUnit(RuleUnit.Soldier, Faction.Player) { Position = new Position(5, 0, 0) };
            grid.At(0, 0, 0).Occupant = hostile;
            grid.At(5, 0, 0).Occupant = player;
            state.Units.Add(hostile);
            state.Units.Add(player);
            int startingTu = hostile.TimeUnits;

            bool result = AiModule.TakeTurn(state, hostile);

            Assert.False(result);
            Assert.Equal(new Position(0, 0, 0), hostile.Position);
            Assert.Equal(startingTu, hostile.TimeUnits);
            Assert.Empty(state.DequeueEvents());
        }

        [Fact]
        public void RunHostileTurn_SkipsDeadUnits()
        {
            var grid = MakeCorridor(10);
            var state = new BattleState(grid);
            var deadHostile = new BattleUnit(RuleUnit.Sectoid, Faction.Hostile) { Position = new Position(0, 0, 0) };
            deadHostile.RightHand = new BattleItem(RuleItem.Rifle);
            deadHostile.Health = 0;
            int deadTu = deadHostile.TimeUnits;
            var player = new BattleUnit(RuleUnit.Soldier, Faction.Player) { Position = new Position(3, 0, 0) };
            grid.At(0, 0, 0).Occupant = deadHostile;
            grid.At(3, 0, 0).Occupant = player;
            state.Units.Add(deadHostile);
            state.Units.Add(player);

            AiModule.RunHostileTurn(state);

            Assert.Equal(deadTu, deadHostile.TimeUnits); // never acted
            Assert.Empty(state.DequeueEvents());
        }

        [Fact]
        public void RunHostileTurn_UnitWithEnoughTuForMultipleShots_FiresExactlyAsManyTimesAsAffordable()
        {
            var grid = new TileGrid(10, 10, 1);
            var state = new BattleState(grid);
            var hostile = new BattleUnit(RuleUnit.Sectoid, Faction.Hostile) { Position = new Position(0, 0, 0) };
            hostile.RightHand = new BattleItem(RuleItem.Rifle);
            hostile.TimeUnits = 100; // aimed shot costs a fixed 29 (Sectoid Stats.TimeUnits=54 * 55% = 29): 100->71->42->13, 3 affordable shots, 4th (13<29) is not
            var player = new BattleUnit(RuleUnit.Soldier, Faction.Player) { Position = new Position(3, 0, 0), Health = 1000 }; // health high enough that no plausible roll sequence kills it, keeping it a valid target throughout
            grid.At(0, 0, 0).Occupant = hostile;
            grid.At(3, 0, 0).Occupant = player;
            state.Units.Add(hostile);
            state.Units.Add(player);

            AiModule.RunHostileTurn(state);

            Assert.Equal(13, hostile.TimeUnits);
            var fireEvents = state.DequeueEvents();
            int fireCount = 0;
            foreach (var evt in fireEvents)
                if (evt is ProjectileFiredEvent) fireCount++;
            Assert.Equal(3, fireCount);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test --filter "FullyQualifiedName~AiModuleTests"`
Expected: FAIL to build — `AiModule` does not exist yet.

- [ ] **Step 3: Write the implementation**

Create `unity/Assets/Scripts/Core/Battle/AiModule.cs`:

```csharp
using OpenXcom.Core.Common;
using OpenXcom.Core.Rules;

namespace OpenXcom.Core.Battle
{
    /// <summary>
    /// Minimal hostile-unit AI. Simplified port of AIModule::think's
    /// reducible core (src/Battlescape/AIModule.cpp:422): spot the nearest
    /// visible enemy via the same TileEngine visibility the player uses,
    /// fire if possible, otherwise approach. [SIMPLIFIED] no patrol/ambush/
    /// escape behaviors, no melee, no per-unit action-count cap.
    /// </summary>
    public static class AiModule
    {
        /// <summary>
        /// Runs one decision cycle for one hostile unit: fire at the
        /// nearest visible enemy if possible, else move one step closer.
        /// Returns true if the unit made real progress (fired, or actually
        /// moved) - callers repeat this for the same unit until it returns
        /// false, then move on to the next unit (RunHostileTurn).
        /// </summary>
        public static bool TakeTurn(BattleState state, BattleUnit unit)
        {
            if (!unit.IsAlive)
                return false;

            var target = FindNearestVisibleEnemy(state, unit);
            if (target == null)
                return false;

            var weapon = unit.WeaponFor(BattleActionType.AimedShot);
            if (weapon != null)
            {
                var fireResult = state.TryFire(unit, weapon, BattleActionType.AimedShot, target);
                if (fireResult.Outcome == FireOutcome.Fired)
                    return true;
            }

            // Couldn't fire (no weapon, or insufficient TU) - approach
            // instead. Pathfinding always treats the target's own tile as
            // occupied/blocked, so we path to one of its orthogonal
            // neighbors, not the target's exact position.
            var approachTile = FindApproachTile(state.Grid, target.Position);
            if (approachTile == null)
                return false; // target has no free orthogonal neighbor - nothing to do

            var moveResult = state.TryMove(unit, approachTile.Value);
            return moveResult.Path.Count > 0;
        }

        /// <summary>
        /// Runs every living hostile unit's turn: repeats TakeTurn for one
        /// unit until it makes no more progress, then advances to the next
        /// living hostile unit, in Units list order. [SIMPLIFIED] no
        /// per-unit action-count cap (the real engine caps at 2 AI actions
        /// per think() call). Termination is guaranteed: every
        /// progress-reporting call spends TU > 0 (firing always costs a
        /// nonzero percentage of max TU; a real move step costs at least
        /// Pathfinding.DefaultMoveCost), and TU only decreases.
        /// </summary>
        public static void RunHostileTurn(BattleState state)
        {
            foreach (var unit in state.Units)
            {
                if (!unit.IsAlive || unit.Faction != Faction.Hostile)
                    continue;

                while (TakeTurn(state, unit)) { }
            }
        }

        private static BattleUnit FindNearestVisibleEnemy(BattleState state, BattleUnit unit)
        {
            var visibleTiles = TileEngine.ComputeVisibleTiles(state.Grid, unit.Position);
            BattleUnit nearest = null;
            double nearestDistance = double.MaxValue;

            foreach (var other in state.Units)
            {
                if (!other.IsAlive || other.Faction == unit.Faction)
                    continue;
                if (!visibleTiles.Contains(other.Position))
                    continue;

                double distance = unit.Position.Distance(other.Position);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = other;
                }
            }

            return nearest;
        }

        /// <summary>
        /// Finds the first free (unoccupied, walkable) orthogonal neighbor
        /// of targetPos, checking N/E/S/W in that order
        /// (Directions.Offsets indices 0,2,4,6). Excludes ANY occupied
        /// tile, including the calling unit's own current tile (it is
        /// always that tile's occupant while standing there) - this is
        /// what guarantees TryMove is never called with the acting unit's
        /// own position as the destination.
        /// </summary>
        private static Position? FindApproachTile(TileGrid grid, Position targetPos)
        {
            int[] orthogonalDirections = { 0, 2, 4, 6 }; // N, E, S, W
            foreach (int dir in orthogonalDirections)
            {
                var candidate = targetPos + Directions.Offsets[dir];
                var tile = grid[candidate];
                if (tile != null && tile.Occupant == null && tile.Floor != null && !tile.Floor.NoFloor)
                    return candidate;
            }
            return null;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test --filter "FullyQualifiedName~AiModuleTests"`
Expected: PASS (6/6).

- [ ] **Step 5: Run the full test suite (regression check)**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test`
Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add unity/Assets/Scripts/Core/Battle/AiModule.cs unity/Tests.Standalone/AiModuleTests.cs
git commit -m "feat(core): AiModule - minimal shoot-or-approach hostile AI"
```

---

### Task 3: `BattleState.EndPlayerTurn`

**Files:**
- Modify: `unity/Assets/Scripts/Core/Battle/BattleState.cs` (add `EndPlayerTurn`)
- Modify: `unity/Tests.Standalone/BattleStateTurnTests.cs` (add tests, keep
  the 5 existing ones from Task 1 unchanged)

**Interfaces:**
- Consumes: `BattleState.EndTurn()` (Task 1), `AiModule.RunHostileTurn`
  (Task 2), `IsBattleOver` (existing).
- Produces (used by Task 4): `BattleState.EndPlayerTurn()`.

- [ ] **Step 1: Write the failing tests**

Add these two test methods to the existing `BattleStateTurnTests` class in
`unity/Tests.Standalone/BattleStateTurnTests.cs` (alongside the 5 Task 1
tests — do not remove those):

```csharp
        [Fact]
        public void EndPlayerTurn_NoAiAction_ReturnsToPlayerTurn()
        {
            var grid = new TileGrid(50, 50, 1);
            var state = new BattleState(grid);
            state.Units.Add(new BattleUnit(RuleUnit.Soldier, Faction.Player) { Position = new Position(0, 0, 0) });
            state.Units.Add(new BattleUnit(RuleUnit.Sectoid, Faction.Hostile) { Position = new Position(49, 49, 0) }); // far apart, mutually invisible

            state.EndPlayerTurn();

            Assert.Equal(Faction.Player, state.CurrentTurn);
            var events = state.DequeueEvents();
            Assert.Equal(2, events.Count); // TurnChanged(Hostile), TurnChanged(Player) - no combat occurred
            var first = Assert.IsType<TurnChangedEvent>(events[0]);
            Assert.Equal(Faction.Hostile, first.Faction);
            var second = Assert.IsType<TurnChangedEvent>(events[1]);
            Assert.Equal(Faction.Player, second.Faction);
        }

        [Fact]
        public void EndPlayerTurn_WhenBattleEndsAfterFirstSwitch_StopsAtHostileTurnWithOnlyOneBattleOverEvent()
        {
            var grid = new TileGrid(10, 10, 1);
            var state = new BattleState(grid);
            var deadPlayer = new BattleUnit(RuleUnit.Soldier, Faction.Player) { Position = new Position(0, 0, 0) };
            deadPlayer.Health = 0; // already dead going into this turn-end - deterministic, no RNG needed
            var hostile = new BattleUnit(RuleUnit.Sectoid, Faction.Hostile) { Position = new Position(5, 5, 0) };
            state.Units.Add(deadPlayer);
            state.Units.Add(hostile);

            state.EndPlayerTurn();

            Assert.Equal(Faction.Hostile, state.CurrentTurn); // never switched back - AI turn and 2nd EndTurn were skipped
            var events = state.DequeueEvents();
            Assert.Equal(2, events.Count); // TurnChanged(Hostile) + BattleOverEvent only
            Assert.IsType<TurnChangedEvent>(events[0]);
            var over = Assert.IsType<BattleOverEvent>(events[1]);
            Assert.Equal(BattleOutcome.HostileVictory, over.Outcome);
        }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test --filter "FullyQualifiedName~BattleStateTurnTests"`
Expected: FAIL to build — `BattleState.EndPlayerTurn` does not exist yet.

- [ ] **Step 3: Write the implementation**

Add this method inside the `BattleState` class, after `EndTurn`:

```csharp
        /// <summary>
        /// Ends the player's turn: switches to Hostile, runs the full
        /// simplified AI turn for every living hostile unit, then switches
        /// back to Player - unless the battle already ended when switching
        /// to Hostile, in which case the AI turn and the second switch are
        /// both skipped.
        /// </summary>
        public void EndPlayerTurn()
        {
            EndTurn(); // Player -> Hostile
            if (IsBattleOver)
                return;

            AiModule.RunHostileTurn(this);

            EndTurn(); // Hostile -> Player
        }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test --filter "FullyQualifiedName~BattleStateTurnTests"`
Expected: PASS (7/7 — 5 from Task 1 + 2 new).

- [ ] **Step 5: Run the full test suite (regression check)**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test`
Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add unity/Assets/Scripts/Core/Battle/BattleState.cs unity/Tests.Standalone/BattleStateTurnTests.cs
git commit -m "feat(core): BattleState.EndPlayerTurn drives the full Player-Hostile-Player round trip"
```

---

### Task 4: `OpenXcom.Unity` — extend `BattleController` with end-turn input

**⚠️ This task cannot be compiled, run, or visually verified in this
development environment** — same reason as every prior phase's Unity task:
no Unity Editor, no UnityEngine assemblies available. Write this code to
the same standard of care as the rest of the plan; the implementer and
reviewer must both report this limitation explicitly. There is no test
step in this task for that reason — review is spec-compliance-by-reading
only.

**Files:**
- Modify: `unity/Assets/Scripts/Unity/BattleController.cs`

**Interfaces:**
- Consumes: `OpenXcom.Core.Battle.{BattleState.EndPlayerTurn, BattleOverEvent, BattleOutcome, TurnChangedEvent}`
  (Tasks 1/3), existing `BattleController` fields (`_state`,
  `DrainAndAnimate`).
- Produces: nothing consumed by a later task (terminal task of this plan
  and this phase).

- [ ] **Step 1: Read the current file**

`unity/Assets/Scripts/Unity/BattleController.cs` currently handles
left-click-to-move and right-click-to-fire, both gated by
`_animatingTransform == null`. This task adds a keyboard input for "end
turn" and extends `DrainAndAnimate`'s event loop to log the 2 new event
types.

- [ ] **Step 2: Write the implementation**

In `unity/Assets/Scripts/Unity/BattleController.cs`, add an end-turn check
to `Update()` — insert this block immediately after the existing
`if (_state == null) return;` line and before the existing
`if (Input.GetMouseButtonDown(1))` right-click check:

```csharp
            if (Input.GetKeyDown(KeyCode.Space))
            {
                _state.EndPlayerTurn();
                DrainAndAnimate();
                return;
            }
```

Then extend `DrainAndAnimate()`'s event loop with 2 more `else if` branches
(keep every existing branch from Phases 3-4 unchanged, add these after the
existing `UnitDiedEvent` branch):

```csharp
                else if (evt is TurnChangedEvent turnChanged)
                {
                    Debug.Log($"Turn changed: {turnChanged.Faction}");
                }
                else if (evt is BattleOverEvent battleOver)
                {
                    Debug.Log($"Battle over: {battleOver.Outcome}");
                }
```

- [ ] **Step 3: Note the verification gap (no code step — this is the record of it)**

There is no `dotnet test` step for this task. Record in the task report,
verbatim: "Task 4 could not be compiled or executed — no Unity Editor /
UnityEngine assemblies available in this environment. Reviewed for
spec-compliance by reading only."

- [ ] **Step 4: Commit**

```bash
git add unity/Assets/Scripts/Unity/BattleController.cs
git commit -m "feat(unity): BattleController end-turn input (Space, unverified, no Editor access)"
```
