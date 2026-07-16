using OpenXcom.Core.Battle;
using OpenXcom.Core.Common;
using OpenXcom.Core.Rules;
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

        [Fact]
        public void ComputeVisibleTiles_DiagonalPointBeyondEuclideanRange_IsExcluded_CatchesAChebyshevDistanceRegression()
        {
            // (20,20,0): Euclidean distance = sqrt(800) ~= 28.28 (> 20, out of range).
            // Chebyshev distance (max(|dx|,|dy|)) would be exactly 20 (in range) -
            // this point specifically distinguishes the two metrics.
            var grid = new TileGrid(25, 25, 1);
            var visible = TileEngine.ComputeVisibleTiles(grid, new Position(0, 0, 0));

            Assert.DoesNotContain(new Position(20, 20, 0), visible);
        }

        [Fact]
        public void ComputeVisibleTiles_DiagonalPointWithinEuclideanRange_IsIncluded_CatchesAManhattanDistanceRegression()
        {
            // (14,14,0): Euclidean distance = sqrt(392) ~= 19.80 (<= 20, in range).
            // Manhattan distance (|dx|+|dy|) would be 28 (out of range) -
            // this point specifically distinguishes the two metrics.
            var grid = new TileGrid(20, 20, 1);
            var visible = TileEngine.ComputeVisibleTiles(grid, new Position(0, 0, 0));

            Assert.Contains(new Position(14, 14, 0), visible);
        }

        private static ushort[] SolidLoftData()
        {
            // Template 0: empty (all bits clear, the default MapDataTile.Loft value).
            // Template 1: fully solid 16x16 at every row - any voxel inside it is "hit".
            var data = new ushort[32];
            for (int row = 0; row < 16; row++)
                data[16 + row] = 0xFFFF;
            return data;
        }

        private static MapDataTile SolidPart()
        {
            var part = new MapDataTile();
            for (int i = 0; i < 12; i++) part.Loft[i] = 1;
            return part;
        }

        [Fact]
        public void VoxelCheck_SolidTerrainLoftBlocksTheVoxel()
        {
            var grid = new TileGrid(2, 2, 1);
            grid.At(0, 0, 0).Object = SolidPart();

            var hit = TileEngine.VoxelCheck(grid, SolidLoftData(), new Position(4, 4, 4), excludeUnit: null);

            Assert.Equal(VoxelType.Object, hit.Type);
        }

        [Fact]
        public void VoxelCheck_EmptyLoftTemplateDoesNotBlock()
        {
            var grid = new TileGrid(2, 2, 1);
            grid.At(0, 0, 0).Floor = new MapDataTile(); // Loft defaults to all-zero -> template 0 -> empty

            var hit = TileEngine.VoxelCheck(grid, SolidLoftData(), new Position(4, 4, 4), excludeUnit: null);

            Assert.Equal(VoxelType.Empty, hit.Type);
        }

        [Fact]
        public void VoxelCheck_NegativeCoordinateIsOutOfBounds()
        {
            var grid = new TileGrid(2, 2, 1);
            var hit = TileEngine.VoxelCheck(grid, SolidLoftData(), new Position(-1, 0, 0), excludeUnit: null);
            Assert.Equal(VoxelType.OutOfBounds, hit.Type);
        }

        [Fact]
        public void VoxelCheck_LivingUnitWithinItsHeightBandIsHit()
        {
            var grid = new TileGrid(2, 2, 1);
            var armor = new RuleArmor("A", 0, 0, 0, 0, loftemps: 1);
            var unit = new BattleUnit(new RuleUnit("STR_TEST", UnitStats.Rookie, armor, standHeight: 22, kneelHeight: 14), Faction.Player)
            {
                Position = new Position(0, 0, 0),
            };
            grid.At(0, 0, 0).Occupant = unit;

            // tz = 0*24 + FloatHeight(0) - terrainLevel(0) = 0; unit.Height (standing) = 22, so
            // voxel.Z in (0, 22] is inside the unit's body.
            var hit = TileEngine.VoxelCheck(grid, SolidLoftData(), new Position(4, 4, 10), excludeUnit: null);

            Assert.Equal(VoxelType.Unit, hit.Type);
            Assert.Same(unit, hit.Unit);
        }

        [Fact]
        public void VoxelCheck_ExcludedUnitIsNotHit()
        {
            var grid = new TileGrid(2, 2, 1);
            var armor = new RuleArmor("A", 0, 0, 0, 0, loftemps: 1);
            var unit = new BattleUnit(new RuleUnit("STR_TEST", UnitStats.Rookie, armor, standHeight: 22, kneelHeight: 14), Faction.Player)
            {
                Position = new Position(0, 0, 0),
            };
            grid.At(0, 0, 0).Occupant = unit;

            var hit = TileEngine.VoxelCheck(grid, SolidLoftData(), new Position(4, 4, 10), excludeUnit: unit);

            Assert.Equal(VoxelType.Empty, hit.Type);
        }

        [Fact]
        public void VoxelCheck_VoxelAboveUnitsHeightBandMisses()
        {
            var grid = new TileGrid(2, 2, 1);
            var armor = new RuleArmor("A", 0, 0, 0, 0, loftemps: 1);
            var unit = new BattleUnit(new RuleUnit("STR_TEST", UnitStats.Rookie, armor, standHeight: 22, kneelHeight: 14), Faction.Player)
            {
                Position = new Position(0, 0, 0),
            };
            grid.At(0, 0, 0).Occupant = unit;

            // voxel.Z = 23 is above tz(0) + Height(22) = 22 -> outside the unit's body.
            var hit = TileEngine.VoxelCheck(grid, SolidLoftData(), new Position(4, 4, 23), excludeUnit: null);

            Assert.Equal(VoxelType.Empty, hit.Type);
        }

        [Fact]
        public void CalculateLine_ClearPathReachesExactTargetVoxel()
        {
            var grid = new TileGrid(3, 3, 1);
            var origin = new Position(8, 8, 10);
            var target = new Position(40, 8, 10);

            var hit = TileEngine.CalculateLine(grid, System.Array.Empty<ushort>(), origin, target, excludeUnit: null);

            Assert.Equal(VoxelType.Empty, hit.Type);
            Assert.Equal(target, hit.Voxel);
        }

        [Fact]
        public void CalculateLine_SolidWallOnThePathStopsBeforeTheTarget()
        {
            var grid = new TileGrid(3, 3, 1);
            grid.At(1, 0, 0).Object = SolidPart(); // tile x=1 -> voxel X range [16,32)

            var origin = new Position(8, 8, 10);
            var target = new Position(40, 8, 10);

            var hit = TileEngine.CalculateLine(grid, SolidLoftData(), origin, target, excludeUnit: null);

            Assert.Equal(VoxelType.Object, hit.Type);
            Assert.InRange(hit.Voxel.X, 16, 31);
        }

        [Fact]
        public void CalculateLine_UnitStandingBehindASolidWallIsNotReached()
        {
            var grid = new TileGrid(3, 3, 1);
            grid.At(1, 0, 0).Object = SolidPart(); // blocks tile x=1

            var armor = new RuleArmor("A", 0, 0, 0, 0, loftemps: 1);
            var defender = new BattleUnit(new RuleUnit("STR_TEST", UnitStats.Rookie, armor, standHeight: 22, kneelHeight: 14), Faction.Hostile)
            {
                Position = new Position(2, 0, 0),
            };
            grid.At(2, 0, 0).Occupant = defender;

            var origin = new Position(8, 8, 10);
            var target = new Position(new Position(2, 0, 0).X * 16 + 8, 8, 10); // aimed at the far unit's tile center

            var hit = TileEngine.CalculateLine(grid, SolidLoftData(), origin, target, excludeUnit: null);

            Assert.Equal(VoxelType.Object, hit.Type); // stops at the wall, never reaches the unit
        }

        [Fact]
        public void CalculateLine_UnobstructedShotHitsTheStandingUnit()
        {
            var grid = new TileGrid(3, 3, 1);
            var armor = new RuleArmor("A", 0, 0, 0, 0, loftemps: 1);
            var defender = new BattleUnit(new RuleUnit("STR_TEST", UnitStats.Rookie, armor, standHeight: 22, kneelHeight: 14), Faction.Hostile)
            {
                Position = new Position(2, 0, 0),
            };
            grid.At(2, 0, 0).Occupant = defender;

            var origin = new Position(8, 8, 10);
            var target = new Position(2 * 16 + 8, 8, 10);

            var hit = TileEngine.CalculateLine(grid, SolidLoftData(), origin, target, excludeUnit: null);

            Assert.Equal(VoxelType.Unit, hit.Type);
            Assert.Same(defender, hit.Unit);
        }

        [Fact]
        public void CalculateLine_FortyFiveDegreeDiagonalStopsAtASolidTileOnItsPath()
        {
            // A perfect 45-degree line (equal X/Y delta) exercises the drift
            // side-step branches every single step (driftXy/driftXz underflow on
            // every iteration when deltaX == deltaY), unlike an axis-aligned line
            // which never triggers them - this is the non-trivial diagonal case
            // the Phase 8 design spec's testing strategy calls out explicitly.
            var grid = new TileGrid(3, 3, 1);
            grid.At(1, 1, 0).Object = SolidPart();

            var origin = new Position(8, 8, 10);   // tile (0,0)
            var target = new Position(40, 40, 10); // tile (2,2), a 45-degree diagonal

            var hit = TileEngine.CalculateLine(grid, SolidLoftData(), origin, target, excludeUnit: null);

            Assert.Equal(VoxelType.Object, hit.Type);
            Assert.InRange(hit.Voxel.X, 16, 31);
            Assert.InRange(hit.Voxel.Y, 16, 31);
        }

        [Fact]
        public void CalculateLine_YDominantLineSwapsXyAndStopsAtASolidTileOnItsPath()
        {
            // |deltaY|=32 > |deltaX|=0 triggers the swapXy branch inside
            // CalculateLine. Origin and target share X=8,Z=10 (tile x=0,z=0)
            // and only Y varies, walking straight through tile row
            // (0,0,0) -> (0,1,0) -> (0,2,0). None of the other CalculateLine
            // tests have |deltaY| > |deltaX|, so this is the only coverage
            // of the swap-then-unswap-per-point logic on the Y axis.
            var grid = new TileGrid(3, 3, 1);
            grid.At(0, 1, 0).Object = SolidPart(); // tile y=1 -> voxel Y range [16,32)

            var origin = new Position(8, 8, 10);
            var target = new Position(8, 40, 10);

            var hit = TileEngine.CalculateLine(grid, SolidLoftData(), origin, target, excludeUnit: null);

            Assert.Equal(VoxelType.Object, hit.Type);
            Assert.InRange(hit.Voxel.Y, 16, 31);
        }

        [Fact]
        public void CalculateLine_ZDominantLineSwapsXzAndXyAndStopsAtASolidTileOnItsPath()
        {
            // deltaX=0, deltaY=16, deltaZ=40: |deltaZ| > |deltaX| and
            // |deltaZ| > |deltaY| makes Z the dominant axis, but deltaY > deltaX
            // ALSO triggers swapXy on top of swapXz - unlike a pure
            // deltaY=0 Z-dominant line (where swapXy's condition,
            // |deltaY| > |deltaX|, is 0 > 0 = false and only swapXz ever
            // fires), this is the only CalculateLine test where both un-swaps
            // apply to the same call. That matters: the two un-swap lines in
            // CheckPoint are independent ifs, so when only one of them is
            // ever true, their relative order is unobservable and a bug that
            // swaps that order would slip past every other test in this
            // file - it only shows up when both fire together, exactly as
            // here.
            //
            // Hand-simulated the integer Bresenham drift (deltaX=40,
            // deltaY=0, deltaZ=16 after both axis swaps; driftXz starts at
            // 20 and decrements by 16 each of the 40 outer-loop steps,
            // carrying the +40 wraparound on underflow): world Y first
            // reaches 16 (tile y=1) while world Z is still 23 (tile z=0,
            // not solid), then world Z reaches 24 (tile z=1) on the very
            // next step while world Y is still 16 (tile y=1) - so the first
            // hit against a solid tile at (0,1,1) lands at world voxel
            // (8, 16, 24).
            var grid = new TileGrid(3, 3, 2);
            grid.At(0, 1, 1).Object = SolidPart(); // tile (x=0,y=1,z=1) -> voxel X [0,15], Y [16,31], Z [24,47]

            var origin = new Position(8, 8, 5);
            var target = new Position(8, 24, 45);

            var hit = TileEngine.CalculateLine(grid, SolidLoftData(), origin, target, excludeUnit: null);

            Assert.Equal(VoxelType.Object, hit.Type);
            Assert.InRange(hit.Voxel.X, 0, 15);
            Assert.InRange(hit.Voxel.Y, 16, 31);
            Assert.InRange(hit.Voxel.Z, 24, 47);
        }

        [Fact]
        public void GetDirectionTo_EachCompassDirectionMapsToItsOwnIndex()
        {
            var origin = new Position(5, 5, 0);
            Assert.Equal(0, TileEngine.GetDirectionTo(origin, new Position(5, 0, 0)));  // north
            Assert.Equal(2, TileEngine.GetDirectionTo(origin, new Position(10, 5, 0))); // east
            Assert.Equal(4, TileEngine.GetDirectionTo(origin, new Position(5, 10, 0))); // south
            Assert.Equal(6, TileEngine.GetDirectionTo(origin, new Position(0, 5, 0)));  // west
        }

        [Fact]
        public void GetOriginVoxel_AddsShooterHeightAndTerrainLevelOffset()
        {
            var grid = new TileGrid(3, 3, 1);
            var shooter = new BattleUnit(new RuleUnit("STR_TEST", UnitStats.Rookie, RuleArmor.None,
                standHeight: 22, kneelHeight: 14), Faction.Player)
            {
                Position = new Position(1, 1, 0),
            };

            var origin = TileEngine.GetOriginVoxel(grid, shooter, new Position(1, 0, 0));

            // Base tile voxel origin (16,16,0) + Height(22) + FloatHeight(0) - TerrainLevel(0) - 4 = Z 18,
            // plus the north-direction (dir 0) shift (dirX=8, dirY=1) on top of tile*16.
            Assert.Equal(16 + 8, origin.X);
            Assert.Equal(16 + 1, origin.Y);
            Assert.Equal(0 + 22 + 0 - 4, origin.Z);
        }
    }
}
