# Phase 3 (Units & Movement) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Spawn a squad of hardcoded X-COM soldiers onto the real CULTA00 map
(Phase 2), and let one be selected and moved to a clicked tile via a real A*
pathfinding pass on a TU budget — the "playable movement" milestone.

**Architecture:** Fix a Phase 2 gap (`MapGenerator` not deriving
`Tile.Walkable`/`BlocksSight`), add a `BattleEvent` stream + `BattleState`
container (owns `TileGrid` + units + turn + events), a from-scratch A*
`Pathfinding` using the simplified TU cost model from the design addendum,
wire `BattleState.TryMove` to spend TU per step and enqueue events, and add
a terminal Unity `BattleController` MonoBehaviour (unverified, no Editor).

**Tech Stack:** .NET 8, xUnit, `System.Collections.Generic.Dictionary`-based
A* (no external pathfinding library).

## Global Constraints

- This plan builds on Phase 2 (merged to `oxce-plus`): `TileGrid`, `Tile`
  (`Floor`/`WestWall`/`NorthWall`/`Object`/`Occupant`/`Walkable`/`BlocksSight`),
  `MapGenerator.Build(DataLoader.RawMapBlockData, RuleTerrain,
  IReadOnlyDictionary<string, List<MapDataTile>>) -> TileGrid`, `BattleUnit`
  (`Position`, `TimeUnits`, `CanSpend(tu)`, `Spend(tu)`), `MapDataTile`
  (`TuWalk`, `StopLOS`, `NoFloor`), `RuleUnit.Soldier`, `Position`
  (`+`/`-` operators, `ChebyshevDistance`), `Directions.Offsets[8]`/`IsDiagonal(dir)`
  — all already exist in `unity/Assets/Scripts/Core/`, reuse exactly as-is,
  do not rename or re-declare.
- `OpenXcom.Core` (`unity/Assets/Scripts/Core/`) must have zero `UnityEngine`
  references — its asmdef has `"noEngineReferences": true`.
- `DEFAULT_MOVE_COST = 4` (verified against `src/Battlescape/Pathfinding.h:82`).
  Diagonal surcharge: `cost = cost * 3 / 2` for odd-indexed directions
  (matches `Directions.IsDiagonal`), verified against
  `src/Battlescape/Pathfinding.cpp:570-573`.
- Wall blocking is orthogonal-only this phase (diagonals never check walls)
  — an explicitly documented simplification, not a bug to "complete" later
  in this same plan.
- No combat, no LOS/fog, no AI, no multi-level movement (stairs/falling/
  flying), no `.rul`-driven spawn/deployment rules, no multiple mapblocks.
  Squad spawns at whichever real route nodes exist in the converted
  `mapblock-CULTA00.json` (currently exactly one node, per Phase 2), in node
  order, one unit per node up to squad size — no rank/type filtering.
- Environment: the .NET SDK is at `~/.dotnet`; every `dotnet` command must be
  run as `export PATH="$HOME/.dotnet:$PATH" && dotnet ...`, from
  `unity/Tests.Standalone`.
- This session's environment cannot open the Unity Editor. Tasks 1-4 are
  fully verified by `dotnet test`. Task 5 (`BattleController`) cannot be
  compiled or run here — its implementer and reviewer must say so explicitly
  rather than claim verification that didn't happen.

---

### Task 1: Fix `MapGenerator` — derive `Tile.Walkable`/`BlocksSight`

**Files:**
- Modify: `unity/Assets/Scripts/Core/Battle/MapGenerator.cs`
- Modify: `unity/Tests.Standalone/MapGeneratorTests.cs` (add tests, keep
  the two existing ones unchanged)

**Interfaces:**
- Consumes: existing `Tile.Floor/WestWall/NorthWall/Object` (Phase 2),
  `MapDataTile.NoFloor`/`StopLOS` (Phase 2).
- Produces (used by Task 3's `Pathfinding`): `Tile.Walkable` and
  `Tile.BlocksSight` now reflect real per-tile data instead of their
  always-`true`/always-`false` defaults.

- [ ] **Step 1: Write the failing tests**

Add these two test methods to the existing
`unity/Tests.Standalone/MapGeneratorTests.cs` class (inside the existing
`MapGeneratorTests` class body, alongside the two Phase 2 tests — do not
remove or modify those two):

```csharp
        [Fact]
        public void Build_TileWithNoFloorRecordIsNotWalkable()
        {
            var (terrain, datasetTiles, block) = LoadReal();
            var grid = MapGenerator.Build(block, terrain, datasetTiles);

            bool foundNoFloorTile = false;
            bool foundNormalFloorTile = false;
            for (int y = 0; y < grid.Length; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    var tile = grid.At(x, y, 0);
                    if (tile.Floor == null) continue;

                    if (tile.Floor.NoFloor)
                    {
                        Assert.False(tile.Walkable);
                        foundNoFloorTile = true;
                    }
                    else
                    {
                        Assert.True(tile.Walkable);
                        foundNormalFloorTile = true;
                    }
                }
            }
            Assert.True(foundNormalFloorTile, "Expected at least one normal walkable floor tile in CULTA00.");
            // CULTA00 may or may not contain a NoFloor record; this assertion
            // only fires the NoFloor branch above if one exists, it does not
            // require one to exist.
        }

        [Fact]
        public void Build_TileWithoutAnyFloorIsNotWalkable()
        {
            var (terrain, datasetTiles, block) = LoadReal();
            var grid = MapGenerator.Build(block, terrain, datasetTiles);

            for (int y = 0; y < grid.Length; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    var tile = grid.At(x, y, 0);
                    if (tile.Floor == null)
                        Assert.False(tile.Walkable);
                }
            }
        }

        [Fact]
        public void Build_BlocksSightMatchesAnyPartsStopLOSFlag()
        {
            var (terrain, datasetTiles, block) = LoadReal();
            var grid = MapGenerator.Build(block, terrain, datasetTiles);

            for (int y = 0; y < grid.Length; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    var tile = grid.At(x, y, 0);
                    bool expected = (tile.WestWall?.StopLOS ?? false)
                        || (tile.NorthWall?.StopLOS ?? false)
                        || (tile.Object?.StopLOS ?? false);
                    Assert.Equal(expected, tile.BlocksSight);
                }
            }
        }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test --filter "FullyQualifiedName~MapGeneratorTests"`
Expected: the 3 new tests FAIL (`Walkable`/`BlocksSight` still at their
always-`true`/always-`false` defaults); the 2 pre-existing tests still pass.

- [ ] **Step 3: Write the implementation**

In `unity/Assets/Scripts/Core/Battle/MapGenerator.cs`, inside the `Build`
method's innermost loop, immediately after the four existing
`tile.Floor = ...` / `tile.WestWall = ...` / `tile.NorthWall = ...` /
`tile.Object = ...` assignment lines, add:

```csharp
                        tile.Walkable = tile.Floor != null && !tile.Floor.NoFloor;
                        tile.BlocksSight = (tile.WestWall?.StopLOS ?? false)
                            || (tile.NorthWall?.StopLOS ?? false)
                            || (tile.Object?.StopLOS ?? false);
```

(No other lines in the file change — the loop structure, `Resolve` helper,
and method signature are untouched.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test --filter "FullyQualifiedName~MapGeneratorTests"`
Expected: PASS (5/5 — 2 pre-existing + 3 new).

- [ ] **Step 5: Run the full test suite (regression check)**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test`
Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add unity/Assets/Scripts/Core/Battle/MapGenerator.cs unity/Tests.Standalone/MapGeneratorTests.cs
git commit -m "fix(core): MapGenerator derives Tile.Walkable/BlocksSight from real data"
```

---

### Task 2: `BattleEvent` + `BattleState` — event queue & route-node spawn

**Files:**
- Create: `unity/Assets/Scripts/Core/Battle/BattleEvent.cs`
- Create: `unity/Assets/Scripts/Core/Battle/BattleState.cs`
- Test: `unity/Tests.Standalone/BattleStateTests.cs`

**Interfaces:**
- Consumes: `TileGrid`, `Tile.Occupant` (Phase 1/2), `BattleUnit.Position`
  (Phase 1), `Rng` (Phase 1, `Common/Rng.cs`), `DataLoader.RawRouteNode`
  (Phase 2, has `X`,`Y`,`Z` int fields), `Faction` (Phase 1,
  `Rules/Enums.cs`).
- Produces (used by Task 4):
  - `OpenXcom.Core.Battle.BattleEvent` (abstract base)
  - `OpenXcom.Core.Battle.UnitMovedEvent(BattleUnit unit, IReadOnlyList<Position> path)` : `BattleEvent`, with `Unit`/`Path` properties
  - `OpenXcom.Core.Battle.TurnChangedEvent(Faction faction)` : `BattleEvent`, with `Faction` property
  - `OpenXcom.Core.Battle.BattleState { TileGrid Grid; List<BattleUnit> Units; Faction CurrentTurn; Rng Rng; }`
  - `BattleState.Enqueue(BattleEvent evt)`, `BattleState.DequeueEvents() -> IReadOnlyList<BattleEvent>`
  - `BattleState.SpawnAtRouteNodes(TileGrid grid, IReadOnlyList<DataLoader.RawRouteNode> routeNodes, IReadOnlyList<BattleUnit> squad, Rng rng = null) -> BattleState` (static factory)

- [ ] **Step 1: Write the failing tests**

Create `unity/Tests.Standalone/BattleStateTests.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using OpenXcom.Core.Battle;
using OpenXcom.Core.Common;
using OpenXcom.Core.Rules;
using Xunit;

namespace OpenXcom.Core.Tests
{
    public class BattleStateTests : System.IDisposable
    {
        private readonly string _outDir = Path.Combine(Path.GetTempPath(), "battlestate-" + System.Guid.NewGuid());

        public BattleStateTests() => Directory.CreateDirectory(_outDir);
        public void Dispose() => Directory.Delete(_outDir, recursive: true);

        private DataLoader.RawMapBlockData LoadRealCulta00Block()
        {
            Xcom.Convert.ConvertJob.Run(TestPaths.RawDataDir, _outDir);
            return DataLoader.LoadMapBlock(_outDir, "CULTA00");
        }

        [Fact]
        public void SpawnAtRouteNodes_PlacesUnitAtRealNodePosition()
        {
            var block = LoadRealCulta00Block();
            var grid = new TileGrid(block.Width, block.Length, block.Height);
            var squad = new List<BattleUnit> { new(RuleUnit.Soldier, Faction.Player) };

            var state = BattleState.SpawnAtRouteNodes(grid, block.RouteNodes, squad);

            Assert.Single(state.Units);
            var node = block.RouteNodes[0];
            var expectedPos = new Position(node.X, node.Y, node.Z);
            Assert.Equal(expectedPos, state.Units[0].Position);
            Assert.Same(state.Units[0], grid.At(node.X, node.Y, node.Z).Occupant);
        }

        [Fact]
        public void SpawnAtRouteNodes_SquadLargerThanNodeCount_OnlySpawnsUpToNodeCount()
        {
            var block = LoadRealCulta00Block(); // exactly 1 real route node
            var grid = new TileGrid(block.Width, block.Length, block.Height);
            var squad = new List<BattleUnit>
            {
                new(RuleUnit.Soldier, Faction.Player),
                new(RuleUnit.Soldier, Faction.Player),
                new(RuleUnit.Soldier, Faction.Player),
            };

            var state = BattleState.SpawnAtRouteNodes(grid, block.RouteNodes, squad);

            Assert.Single(state.Units); // only 1 node available
        }

        [Fact]
        public void EnqueueAndDequeueEvents_ReturnsInOrderAndClearsQueue()
        {
            var grid = new TileGrid(1, 1, 1);
            var state = new BattleState(grid);
            var unit = new BattleUnit(RuleUnit.Soldier, Faction.Player);

            state.Enqueue(new TurnChangedEvent(Faction.Player));
            state.Enqueue(new UnitMovedEvent(unit, new List<Position> { new(1, 0, 0) }));

            var drained = state.DequeueEvents();

            Assert.Equal(2, drained.Count);
            Assert.IsType<TurnChangedEvent>(drained[0]);
            Assert.IsType<UnitMovedEvent>(drained[1]);
            Assert.Empty(state.DequeueEvents()); // queue is now empty
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test --filter "FullyQualifiedName~BattleStateTests"`
Expected: FAIL to build — `BattleState`, `UnitMovedEvent`, `TurnChangedEvent` do not exist yet.

- [ ] **Step 3: Write the implementation**

Create `unity/Assets/Scripts/Core/Battle/BattleEvent.cs`:

```csharp
using System.Collections.Generic;
using OpenXcom.Core.Common;
using OpenXcom.Core.Rules;

namespace OpenXcom.Core.Battle
{
    /// <summary>
    /// Base type for ordered state-change notifications systems push while
    /// mutating BattleState immediately; Unity drains and animates from
    /// these independently of game-logic timing (parent design spec §4).
    /// </summary>
    public abstract class BattleEvent
    {
    }

    public sealed class UnitMovedEvent : BattleEvent
    {
        public BattleUnit Unit { get; }
        public IReadOnlyList<Position> Path { get; }

        public UnitMovedEvent(BattleUnit unit, IReadOnlyList<Position> path)
        {
            Unit = unit;
            Path = path;
        }
    }

    public sealed class TurnChangedEvent : BattleEvent
    {
        public Faction Faction { get; }

        public TurnChangedEvent(Faction faction)
        {
            Faction = faction;
        }
    }
}
```

Create `unity/Assets/Scripts/Core/Battle/BattleState.cs`:

```csharp
using System.Collections.Generic;
using OpenXcom.Core.Common;
using OpenXcom.Core.Rules;

namespace OpenXcom.Core.Battle
{
    /// <summary>
    /// The battle's mutable runtime container: the TileGrid, all units, whose
    /// turn it is, and the outbound BattleEvent queue. Mirrors OXCE's
    /// SavedBattleGame at slice-3 scope (no turn/win-loss flow yet — that's
    /// Phase 5).
    /// </summary>
    public sealed class BattleState
    {
        public TileGrid Grid { get; }
        public List<BattleUnit> Units { get; } = new();
        public Faction CurrentTurn { get; set; } = Faction.Player;
        public Rng Rng { get; }

        private readonly Queue<BattleEvent> _events = new();

        public BattleState(TileGrid grid, Rng rng = null)
        {
            Grid = grid;
            Rng = rng ?? new Rng(1);
        }

        public void Enqueue(BattleEvent evt) => _events.Enqueue(evt);

        public IReadOnlyList<BattleEvent> DequeueEvents()
        {
            var drained = new List<BattleEvent>(_events.Count);
            while (_events.Count > 0)
                drained.Add(_events.Dequeue());
            return drained;
        }

        /// <summary>
        /// Spawns units at the mapblock's route nodes, in node order, one
        /// unit per node up to whichever list is shorter. [SIMPLIFIED] no
        /// rank/type filtering — real deployment-based spawn rules need
        /// .rul ruleset data not parsed this phase.
        /// </summary>
        public static BattleState SpawnAtRouteNodes(
            TileGrid grid,
            IReadOnlyList<DataLoader.RawRouteNode> routeNodes,
            IReadOnlyList<BattleUnit> squad,
            Rng rng = null)
        {
            var state = new BattleState(grid, rng);
            int count = System.Math.Min(routeNodes.Count, squad.Count);

            for (int i = 0; i < count; i++)
            {
                var node = routeNodes[i];
                var unit = squad[i];
                var pos = new Position(node.X, node.Y, node.Z);

                unit.Position = pos;
                state.Grid.At(pos.X, pos.Y, pos.Z).Occupant = unit;
                state.Units.Add(unit);
            }

            return state;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test --filter "FullyQualifiedName~BattleStateTests"`
Expected: PASS (3/3).

- [ ] **Step 5: Run the full test suite (regression check)**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test`
Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add unity/Assets/Scripts/Core/Battle/BattleEvent.cs unity/Assets/Scripts/Core/Battle/BattleState.cs unity/Tests.Standalone/BattleStateTests.cs
git commit -m "feat(core): BattleEvent stream + BattleState (event queue, route-node spawn)"
```

---

### Task 3: `Pathfinding` — A* on a TU budget

**Files:**
- Create: `unity/Assets/Scripts/Core/Battle/Pathfinding.cs`
- Test: `unity/Tests.Standalone/PathfindingTests.cs`

**Interfaces:**
- Consumes: `TileGrid`, `Tile` (`Occupant`, `Floor`, `WestWall`,
  `NorthWall`), `MapDataTile.TuWalk`, `Position` (`+` operator,
  `ChebyshevDistance`), `Directions.Offsets[8]`/`IsDiagonal(dir)` — all
  existing.
- Produces (used by Task 4): `OpenXcom.Core.Battle.PathStep { Position Position; int StepCost; }`,
  `OpenXcom.Core.Battle.Pathfinding.DefaultMoveCost` (`const int` = `4`),
  `OpenXcom.Core.Battle.Pathfinding.StepCost(TileGrid grid, Position from, int direction) -> int?`,
  `OpenXcom.Core.Battle.Pathfinding.FindPath(TileGrid grid, Position start, Position goal) -> List<PathStep>`
  (empty list if `start == goal`; `null` if unreachable; otherwise ordered
  waypoints from start-exclusive to goal-inclusive, each with its own
  incremental `StepCost`).

- [ ] **Step 1: Write the failing tests**

Create `unity/Tests.Standalone/PathfindingTests.cs`:

```csharp
using OpenXcom.Core.Battle;
using OpenXcom.Core.Common;
using OpenXcom.Core.Rules;
using Xunit;

namespace OpenXcom.Core.Tests
{
    public class PathfindingTests
    {
        // A 5x5, single-level, all-open grid with a normal-cost floor
        // (TuWalk=4) everywhere, useful as a baseline before each test adds
        // its own walls/occupants/costs.
        private static TileGrid MakeOpenGrid(int size = 5, int tuWalk = 4)
        {
            var grid = new TileGrid(size, size, 1);
            var floor = new MapDataTile { TuWalk = tuWalk };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    grid.At(x, y, 0).Floor = floor;
            return grid;
        }

        [Fact]
        public void FindPath_StraightLine_CostsFourPerOrthogonalStep()
        {
            var grid = MakeOpenGrid();
            var path = Pathfinding.FindPath(grid, new Position(0, 0, 0), new Position(3, 0, 0));

            Assert.NotNull(path);
            Assert.Equal(3, path.Count);
            foreach (var step in path)
                Assert.Equal(4, step.StepCost);
        }

        [Fact]
        public void FindPath_DiagonalStep_CostsSixNotFour()
        {
            var grid = MakeOpenGrid();
            var path = Pathfinding.FindPath(grid, new Position(0, 0, 0), new Position(1, 1, 0));

            Assert.NotNull(path);
            Assert.Single(path);
            Assert.Equal(6, path[0].StepCost); // 4 * 3 / 2
        }

        [Fact]
        public void FindPath_SameStartAndGoal_ReturnsEmptyPath()
        {
            var grid = MakeOpenGrid();
            var path = Pathfinding.FindPath(grid, new Position(2, 2, 0), new Position(2, 2, 0));

            Assert.NotNull(path);
            Assert.Empty(path);
        }

        [Fact]
        public void FindPath_UnreachableTarget_ReturnsNull()
        {
            var grid = new TileGrid(3, 1, 1); // no floors anywhere -> nothing is walkable
            var path = Pathfinding.FindPath(grid, new Position(0, 0, 0), new Position(2, 0, 0));

            Assert.Null(path);
        }

        [Fact]
        public void FindPath_OccupiedTileBlocksThatRoute()
        {
            // 3x1 corridor; occupy the middle tile so (0,0,0)->(2,0,0) must fail
            // (there's no way around it in a 1-row grid).
            var grid = MakeOpenGrid(size: 3);
            grid.At(1, 0, 0).Occupant = new BattleUnit(RuleUnit.Soldier, Faction.Hostile);

            var path = Pathfinding.FindPath(grid, new Position(0, 0, 0), new Position(2, 0, 0));

            Assert.Null(path);
        }

        [Fact]
        public void FindPath_WestWallOnSourceTile_BlocksMovingWest()
        {
            var grid = MakeOpenGrid(size: 3);
            grid.At(1, 0, 0).WestWall = new MapDataTile(); // blocks the 1->0 step

            var path = Pathfinding.FindPath(grid, new Position(1, 0, 0), new Position(0, 0, 0));

            Assert.Null(path);
        }

        [Fact]
        public void FindPath_CustomTuWalkValue_IsHonoredPerTile()
        {
            var grid = MakeOpenGrid(tuWalk: 8);
            var path = Pathfinding.FindPath(grid, new Position(0, 0, 0), new Position(1, 0, 0));

            Assert.NotNull(path);
            Assert.Equal(8, path[0].StepCost);
        }

        [Fact]
        public void StepCost_ZeroTuWalk_FallsBackToDefaultMoveCost()
        {
            var grid = MakeOpenGrid(tuWalk: 0);
            int? cost = Pathfinding.StepCost(grid, new Position(0, 0, 0), 2 /* East */);

            Assert.Equal(Pathfinding.DefaultMoveCost, cost);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test --filter "FullyQualifiedName~PathfindingTests"`
Expected: FAIL to build — `Pathfinding`, `PathStep` do not exist yet.

- [ ] **Step 3: Write the implementation**

Create `unity/Assets/Scripts/Core/Battle/Pathfinding.cs`:

```csharp
using System.Collections.Generic;
using OpenXcom.Core.Common;

namespace OpenXcom.Core.Battle
{
    /// <summary>One waypoint of a found path, with the incremental TU cost to reach it.</summary>
    public sealed class PathStep
    {
        public Position Position;
        public int StepCost;
    }

    /// <summary>
    /// Grid A* pathfinding on a TU budget. Simplified port of
    /// Pathfinding::getTUCost (src/Battlescape/Pathfinding.cpp:257-600):
    /// single-tile units, one flat level (z is not traversed), no stairs/
    /// falling/flying, no diagonal wall-corner blocking (wall checks are
    /// orthogonal-direction only this phase).
    /// </summary>
    public static class Pathfinding
    {
        /// <summary>Fallback TU cost when a tile's own floor walk cost is 0 (Pathfinding.h:82).</summary>
        public const int DefaultMoveCost = 4;

        /// <summary>
        /// TU cost to move one step from `from` in compass direction `direction`
        /// (0..7, see Common.Directions), or null if that step is blocked.
        /// </summary>
        public static int? StepCost(TileGrid grid, Position from, int direction)
        {
            var offset = Directions.Offsets[direction];
            var to = from + offset;
            var toTile = grid[to];
            if (toTile == null) return null;
            if (toTile.Occupant != null) return null;
            if (toTile.Floor == null) return null;

            if (!Directions.IsDiagonal(direction))
            {
                var fromTile = grid[from];
                if (offset.X < 0 && fromTile.WestWall != null) return null;   // moving W: source's own west wall
                if (offset.Y < 0 && fromTile.NorthWall != null) return null;  // moving N: source's own north wall
                if (offset.X > 0 && toTile.WestWall != null) return null;     // moving E: dest's west wall
                if (offset.Y > 0 && toTile.NorthWall != null) return null;    // moving S: dest's north wall
            }

            int cost = toTile.Floor.TuWalk > 0 ? toTile.Floor.TuWalk : DefaultMoveCost;
            if (Directions.IsDiagonal(direction))
                cost = cost * 3 / 2;
            return cost;
        }

        /// <summary>
        /// A* search from start to goal. Returns the ordered waypoints from
        /// start (exclusive) to goal (inclusive) with each step's own TU
        /// cost, an empty list if start == goal, or null if unreachable.
        /// </summary>
        public static List<PathStep> FindPath(TileGrid grid, Position start, Position goal)
        {
            if (start == goal)
                return new List<PathStep>();

            var openSet = new List<Position> { start };
            var cameFrom = new Dictionary<Position, Position>();
            var gScore = new Dictionary<Position, int> { [start] = 0 };

            while (openSet.Count > 0)
            {
                Position current = openSet[0];
                int bestF = int.MaxValue;
                foreach (var candidate in openSet)
                {
                    int f = gScore[candidate] + candidate.ChebyshevDistance(goal);
                    if (f < bestF)
                    {
                        bestF = f;
                        current = candidate;
                    }
                }

                if (current == goal)
                    return ReconstructPath(cameFrom, gScore, current);

                openSet.Remove(current);

                for (int dir = 0; dir < 8; dir++)
                {
                    int? stepCost = StepCost(grid, current, dir);
                    if (stepCost == null)
                        continue;

                    var neighbor = current + Directions.Offsets[dir];
                    int tentativeG = gScore[current] + stepCost.Value;

                    if (!gScore.TryGetValue(neighbor, out int existingG) || tentativeG < existingG)
                    {
                        cameFrom[neighbor] = current;
                        gScore[neighbor] = tentativeG;
                        if (!openSet.Contains(neighbor))
                            openSet.Add(neighbor);
                    }
                }
            }

            return null;
        }

        private static List<PathStep> ReconstructPath(
            Dictionary<Position, Position> cameFrom,
            Dictionary<Position, int> gScore,
            Position current)
        {
            var reversed = new List<PathStep>();
            while (cameFrom.TryGetValue(current, out var prev))
            {
                reversed.Add(new PathStep { Position = current, StepCost = gScore[current] - gScore[prev] });
                current = prev;
            }
            reversed.Reverse();
            return reversed;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test --filter "FullyQualifiedName~PathfindingTests"`
Expected: PASS (8/8).

- [ ] **Step 5: Run the full test suite (regression check)**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test`
Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add unity/Assets/Scripts/Core/Battle/Pathfinding.cs unity/Tests.Standalone/PathfindingTests.cs
git commit -m "feat(core): A* Pathfinding on a TU budget"
```

---

### Task 4: `BattleState.TryMove` — spend TU, update occupancy, enqueue events

**Files:**
- Modify: `unity/Assets/Scripts/Core/Battle/BattleState.cs`
- Modify: `unity/Tests.Standalone/BattleStateTests.cs` (add tests, keep the
  three Task 2 tests unchanged)

**Interfaces:**
- Consumes: `Pathfinding.FindPath` (Task 3), `BattleUnit.CanSpend`/`Spend`
  (existing), `BattleState.Enqueue`/`UnitMovedEvent` (Task 2).
- Produces (used by Task 5): `OpenXcom.Core.Battle.MoveOutcome { Failed, Partial, Full }`,
  `OpenXcom.Core.Battle.MoveResult { MoveOutcome Outcome; IReadOnlyList<Position> Path; }`,
  `BattleState.TryMove(BattleUnit unit, Position target) -> MoveResult`.

- [ ] **Step 1: Write the failing tests**

Add these test methods to the existing `BattleStateTests` class in
`unity/Tests.Standalone/BattleStateTests.cs` (alongside the 3 Task 2 tests
— do not remove those):

```csharp
        [Fact]
        public void TryMove_FullBudget_MovesAllTheWayAndEnqueuesOneEvent()
        {
            var grid = new TileGrid(5, 1, 1);
            var floor = new MapDataTile { TuWalk = 4 };
            for (int x = 0; x < 5; x++) grid.At(x, 0, 0).Floor = floor;

            var unit = new BattleUnit(RuleUnit.Soldier, Faction.Player) { Position = new Position(0, 0, 0) };
            unit.TimeUnits = 100;
            grid.At(0, 0, 0).Occupant = unit;
            var state = new BattleState(grid);
            state.Units.Add(unit);

            var result = state.TryMove(unit, new Position(3, 0, 0));

            Assert.Equal(MoveOutcome.Full, result.Outcome);
            Assert.Equal(3, result.Path.Count);
            Assert.Equal(new Position(3, 0, 0), unit.Position);
            Assert.Equal(100 - 3 * 4, unit.TimeUnits);
            Assert.Same(unit, grid.At(3, 0, 0).Occupant);
            Assert.Null(grid.At(0, 0, 0).Occupant);

            var events = state.DequeueEvents();
            Assert.Single(events);
            var moved = Assert.IsType<UnitMovedEvent>(events[0]);
            Assert.Equal(3, moved.Path.Count);
        }

        [Fact]
        public void TryMove_InsufficientBudget_StopsAtLastAffordableTile()
        {
            var grid = new TileGrid(5, 1, 1);
            var floor = new MapDataTile { TuWalk = 4 };
            for (int x = 0; x < 5; x++) grid.At(x, 0, 0).Floor = floor;

            var unit = new BattleUnit(RuleUnit.Soldier, Faction.Player) { Position = new Position(0, 0, 0) };
            unit.TimeUnits = 9; // enough for 2 steps (8 TU), not 3 (12 TU)
            grid.At(0, 0, 0).Occupant = unit;
            var state = new BattleState(grid);
            state.Units.Add(unit);

            var result = state.TryMove(unit, new Position(3, 0, 0));

            Assert.Equal(MoveOutcome.Partial, result.Outcome);
            Assert.Equal(2, result.Path.Count);
            Assert.Equal(new Position(2, 0, 0), unit.Position);
            Assert.Equal(1, unit.TimeUnits); // 9 - 8
            Assert.Same(unit, grid.At(2, 0, 0).Occupant);

            var events = state.DequeueEvents();
            Assert.Single(events);
        }

        [Fact]
        public void TryMove_BlockedDestination_FailsWithNoTuSpentAndNoEvent()
        {
            var grid = new TileGrid(2, 1, 1); // no floors -> nothing walkable
            var unit = new BattleUnit(RuleUnit.Soldier, Faction.Player) { Position = new Position(0, 0, 0) };
            int startingTu = unit.TimeUnits;
            var state = new BattleState(grid);
            state.Units.Add(unit);

            var result = state.TryMove(unit, new Position(1, 0, 0));

            Assert.Equal(MoveOutcome.Failed, result.Outcome);
            Assert.Empty(result.Path);
            Assert.Equal(startingTu, unit.TimeUnits);
            Assert.Equal(new Position(0, 0, 0), unit.Position);
            Assert.Empty(state.DequeueEvents());
        }

        [Fact]
        public void TryMove_AlreadyAtTarget_ReturnsFullWithEmptyPath()
        {
            var grid = new TileGrid(1, 1, 1);
            grid.At(0, 0, 0).Floor = new MapDataTile { TuWalk = 4 };
            var unit = new BattleUnit(RuleUnit.Soldier, Faction.Player) { Position = new Position(0, 0, 0) };
            var state = new BattleState(grid);
            state.Units.Add(unit);

            var result = state.TryMove(unit, new Position(0, 0, 0));

            Assert.Equal(MoveOutcome.Full, result.Outcome);
            Assert.Empty(result.Path);
            Assert.Empty(state.DequeueEvents()); // no-op move enqueues nothing
        }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test --filter "FullyQualifiedName~BattleStateTests"`
Expected: FAIL to build — `MoveOutcome`, `MoveResult`, `BattleState.TryMove` do not exist yet.

- [ ] **Step 3: Write the implementation**

In `unity/Assets/Scripts/Core/Battle/BattleState.cs`, add these two new
types above the `BattleState` class (same file, same namespace):

```csharp
    public enum MoveOutcome
    {
        Failed,
        Partial,
        Full,
    }

    public sealed class MoveResult
    {
        public MoveOutcome Outcome;
        public IReadOnlyList<Position> Path;
    }

```

Then add this method inside the `BattleState` class, after
`SpawnAtRouteNodes`:

```csharp
        /// <summary>
        /// Finds a path from the unit's current position to target and walks
        /// as far as its TU budget allows, spending TU per step, updating
        /// occupancy, and enqueueing one UnitMovedEvent covering the tiles
        /// actually walked (never enqueued for a no-op or failed move).
        /// </summary>
        public MoveResult TryMove(BattleUnit unit, Position target)
        {
            if (unit.Position == target)
                return new MoveResult { Outcome = MoveOutcome.Full, Path = System.Array.Empty<Position>() };

            var fullPath = Pathfinding.FindPath(Grid, unit.Position, target);
            if (fullPath == null || fullPath.Count == 0)
                return new MoveResult { Outcome = MoveOutcome.Failed, Path = System.Array.Empty<Position>() };

            var walked = new List<Position>();
            var previous = unit.Position;

            foreach (var step in fullPath)
            {
                if (!unit.CanSpend(step.StepCost))
                    break;

                unit.Spend(step.StepCost);
                Grid[previous].Occupant = null;
                unit.Position = step.Position;
                Grid[step.Position].Occupant = unit;
                walked.Add(step.Position);
                previous = step.Position;
            }

            if (walked.Count == 0)
                return new MoveResult { Outcome = MoveOutcome.Failed, Path = System.Array.Empty<Position>() };

            Enqueue(new UnitMovedEvent(unit, walked));
            var outcome = walked.Count == fullPath.Count ? MoveOutcome.Full : MoveOutcome.Partial;
            return new MoveResult { Outcome = outcome, Path = walked };
        }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test --filter "FullyQualifiedName~BattleStateTests"`
Expected: PASS (7/7 — 3 from Task 2 + 4 new).

- [ ] **Step 5: Run the full test suite (regression check)**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test`
Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add unity/Assets/Scripts/Core/Battle/BattleState.cs unity/Tests.Standalone/BattleStateTests.cs
git commit -m "feat(core): BattleState.TryMove spends TU per step, enqueues UnitMovedEvent"
```

---

### Task 5: `OpenXcom.Unity` — `BattleController` (select + click-to-move)

**⚠️ This task cannot be compiled, run, or visually verified in this
development environment** — same reason as Phase 2's Task 6: no Unity
Editor, no UnityEngine assemblies available. Write this code to the same
standard of care as the rest of the plan; the implementer and reviewer must
both report this limitation explicitly. There is no test step in this task
for that reason — review is spec-compliance-by-reading only.

**Files:**
- Create: `unity/Assets/Scripts/Unity/BattleController.cs`

**Interfaces:**
- Consumes: `OpenXcom.Core.Battle.{BattleState, BattleUnit, MoveResult,
  MoveOutcome, UnitMovedEvent, TurnChangedEvent}` (Tasks 2/4),
  `OpenXcom.Unity.Rendering.{TileRenderer, IsoProjection}` (Phase 2),
  `OpenXcom.Unity.BattlescapeMapView` (Phase 2 — already loads a `TileGrid`
  and spawns `TileRenderer`s in its `Start()`).
- Produces: nothing consumed by a later task (terminal task of this plan).

- [ ] **Step 1: Write the implementation**

Create `unity/Assets/Scripts/Unity/BattleController.cs`:

```csharp
using System.Collections.Generic;
using OpenXcom.Core.Battle;
using OpenXcom.Core.Common;
using OpenXcom.Unity.Rendering;
using UnityEngine;

namespace OpenXcom.Unity
{
    /// <summary>
    /// Click a unit's GameObject to select it; click a tile to path there.
    /// Pure input + animation glue: all move legality/TU accounting lives in
    /// OpenXcom.Core.Battle.BattleState.TryMove — this class only translates
    /// mouse clicks into calls on it and drains/animates the resulting
    /// BattleEvents. Makes no gameplay decisions of its own.
    ///
    /// NOTE: this class cannot be compiled or run in the environment this
    /// was written in (no Unity Editor / UnityEngine assemblies available
    /// this session). It follows documented Unity API behavior but has not
    /// been visually verified — check it in the Editor before relying on it.
    /// </summary>
    public sealed class BattleController : MonoBehaviour
    {
        [SerializeField] private Camera raycastCamera;
        [SerializeField] private float tilesPerSecond = 4f;

        private BattleState _state;
        private BattleUnit _selected;
        private readonly Dictionary<BattleUnit, Transform> _unitTransforms = new();
        private Queue<Position> _animationQueue;
        private Transform _animatingTransform;
        private Vector3 _animationTarget;

        /// <summary>Wires this controller to an already-populated battle. Called by whichever scene bootstrap owns squad setup.</summary>
        public void Bind(BattleState state, IReadOnlyDictionary<BattleUnit, Transform> unitTransforms)
        {
            _state = state;
            _unitTransforms.Clear();
            foreach (var kv in unitTransforms)
                _unitTransforms[kv.Key] = kv.Value;
        }

        private void Update()
        {
            if (_animatingTransform != null)
            {
                AdvanceAnimation();
                return; // don't accept new input mid-animation
            }

            if (_state == null || !Input.GetMouseButtonDown(0))
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

        private BattleUnit FindUnitAt(Transform hitTransform)
        {
            foreach (var kv in _unitTransforms)
                if (kv.Value == hitTransform)
                    return kv.Key;
            return null;
        }

        private Position TileUnderCursor(RaycastHit hit)
        {
            // Inverse of IsoProjection.MapToScreen; left as a direct pixel/world
            // lookup against the hit tile's own TileRenderer name ("Tile_x_y_z"),
            // since BattlescapeMapView already names each tile GameObject that
            // way and this avoids re-deriving the iso inverse-projection math
            // for this phase (no input-picking formula was ported yet — parent
            // spec §5 names this as later work, "screen->tile picking").
            var parts = hit.transform.parent.name.Split('_');
            return new Position(int.Parse(parts[1]), int.Parse(parts[2]), int.Parse(parts[3]));
        }

        private void DrainAndAnimate()
        {
            foreach (var evt in _state.DequeueEvents())
            {
                if (evt is UnitMovedEvent moved && _unitTransforms.TryGetValue(moved.Unit, out var t))
                {
                    _animatingTransform = t;
                    _animationQueue = new Queue<Position>(moved.Path);
                    AdvanceAnimation();
                }
            }
        }

        private void AdvanceAnimation()
        {
            if (_animationQueue.Count == 0 && Vector3.Distance(_animatingTransform.localPosition, _animationTarget) < 0.01f)
            {
                _animatingTransform = null;
                return;
            }

            if (Vector3.Distance(_animatingTransform.localPosition, _animationTarget) < 0.01f)
            {
                var next = _animationQueue.Dequeue();
                var (sx, sy) = IsoProjection.MapToScreen(next.X, next.Y, next.Z);
                _animationTarget = new Vector3(sx / TileRenderer.PixelsPerUnit, sy / TileRenderer.PixelsPerUnit, 0f);
            }

            _animatingTransform.localPosition = Vector3.MoveTowards(
                _animatingTransform.localPosition, _animationTarget, tilesPerSecond * Time.deltaTime);
        }
    }
}
```

- [ ] **Step 2: Note the verification gap (no code step — this is the record of it)**

There is no `dotnet test` step for this task. Record in the task report,
verbatim: "Task 5 could not be compiled or executed — no Unity Editor /
UnityEngine assemblies available in this environment. Reviewed for
spec-compliance by reading only."

- [ ] **Step 3: Commit**

```bash
git add unity/Assets/Scripts/Unity/BattleController.cs
git commit -m "feat(unity): BattleController (select + click-to-move, unverified, no Editor access)"
```
