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
            var grid = new TileGrid(3, 1, 1);
            var floor = new MapDataTile { TuWalk = 4 };
            for (int x = 0; x < 3; x++)
                grid.At(x, 0, 0).Floor = floor;
            grid.At(1, 0, 0).Occupant = new BattleUnit(RuleUnit.Soldier, Faction.Hostile);

            var path = Pathfinding.FindPath(grid, new Position(0, 0, 0), new Position(2, 0, 0));

            Assert.Null(path);
        }

        [Fact]
        public void FindPath_WestWallOnSourceTile_BlocksMovingWest()
        {
            // 3x1 corridor; place west wall on (1,0,0) so moving from (1,0,0) to (0,0,0) is blocked.
            // Can't go around in a 1-row grid.
            var grid = new TileGrid(3, 1, 1);
            var floor = new MapDataTile { TuWalk = 4 };
            for (int x = 0; x < 3; x++)
                grid.At(x, 0, 0).Floor = floor;
            grid.At(1, 0, 0).WestWall = new MapDataTile { TuWalk = 255 }; // solid wall blocks moving west

            var path = Pathfinding.FindPath(grid, new Position(1, 0, 0), new Position(0, 0, 0));

            Assert.Null(path);
        }

        [Fact]
        public void FindPath_SolidObjectBlocksTheTile()
        {
            // 3x1 corridor with a solid object (e.g. a tree, TuWalk=255) on the
            // middle tile; no way around it in a 1-row grid.
            var grid = new TileGrid(3, 1, 1);
            var floor = new MapDataTile { TuWalk = 4 };
            for (int x = 0; x < 3; x++)
                grid.At(x, 0, 0).Floor = floor;
            grid.At(1, 0, 0).Object = new MapDataTile { TuWalk = 255 };

            var path = Pathfinding.FindPath(grid, new Position(0, 0, 0), new Position(2, 0, 0));

            Assert.Null(path);
        }

        [Fact]
        public void FindPath_PassableObjectAddsItsWalkCost()
        {
            // A bush (passable object, TuWalk=6) on the destination tile costs
            // floor(4) + object(6) = 10 to enter.
            var grid = MakeOpenGrid();
            grid.At(1, 0, 0).Object = new MapDataTile { TuWalk = 6 };

            int? cost = Pathfinding.StepCost(grid, new Position(0, 0, 0), 2 /* East */);

            Assert.Equal(10, cost);
        }

        [Fact]
        public void StepCost_BlockBigWall_StopsDiagonalCornerCutButNotCardinalEntry()
        {
            // A BLOCK big wall (bigWall=1) sitting on (1,0,0) can be walked onto
            // cardinally, but a diagonal step from (0,0,0) to (1,1,0) may not cut
            // the corner around it.
            var grid = MakeOpenGrid();
            grid.At(1, 0, 0).Object = new MapDataTile { BigWall = 1, TuWalk = 4 };

            Assert.Null(Pathfinding.StepCost(grid, new Position(0, 0, 0), 3 /* SE, cuts the corner */));
            Assert.NotNull(Pathfinding.StepCost(grid, new Position(0, 0, 0), 2 /* East, straight onto it */));
        }

        [Fact]
        public void StepCost_WestEdgeBigWall_BlocksOnlyItsOwnEdge()
        {
            // A west-edge big wall (bigWall=4) on (1,2,0) seals the boundary
            // between it and the tile to its west - so a step west out of it (and,
            // symmetrically, a step east into it) is blocked - but its east edge
            // is open, so stepping further east is fine.
            var grid = MakeOpenGrid();
            grid.At(1, 2, 0).Object = new MapDataTile { BigWall = 4 };

            Assert.Null(Pathfinding.StepCost(grid, new Position(1, 2, 0), 6 /* West, crosses the sealed edge */));
            Assert.Null(Pathfinding.StepCost(grid, new Position(0, 2, 0), 2 /* East into the sealed edge */));
            Assert.NotNull(Pathfinding.StepCost(grid, new Position(1, 2, 0), 2 /* East out the open edge */));
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
