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
