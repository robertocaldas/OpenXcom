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
        public void SpawnAtRouteNodes_SquadSmallerThanNodeCount_OnlySpawnsSquadCountUnitsAtTheirOwnNodes()
        {
            var grid = new TileGrid(10, 10, 1);
            var routeNodes = new List<DataLoader.RawRouteNode>
            {
                new() { X = 1, Y = 1, Z = 0 },
                new() { X = 5, Y = 5, Z = 0 },
                new() { X = 9, Y = 9, Z = 0 }, // never used - squad only has 2 units
            };
            var squad = new List<BattleUnit>
            {
                new(RuleUnit.Soldier, Faction.Player),
                new(RuleUnit.Soldier, Faction.Player),
            };

            var state = BattleState.SpawnAtRouteNodes(grid, routeNodes, squad);

            Assert.Equal(2, state.Units.Count);
            Assert.Equal(new Position(1, 1, 0), state.Units[0].Position);
            Assert.Equal(new Position(5, 5, 0), state.Units[1].Position);
            // The third node (9,9,0) must NOT have received a unit.
            Assert.Null(grid.At(9, 9, 0).Occupant);
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
