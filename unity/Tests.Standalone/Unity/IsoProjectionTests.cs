using OpenXcom.Unity.Rendering;
using Xunit;

namespace OpenXcom.Core.Tests.Unity
{
    public class IsoProjectionTests
    {
        [Fact]
        public void MapToScreen_Origin_IsScreenOrigin()
        {
            var (sx, sy) = IsoProjection.MapToScreen(0, 0, 0);
            Assert.Equal(0, sx);
            Assert.Equal(0, sy);
        }

        [Fact]
        public void MapToScreen_MatchesCameraCppFormula()
        {
            // screenX = (x-y)*16, screenY = (x+y)*8 - z*24 (Camera.cpp:475-480, 32x40 sprites).
            var (sx, sy) = IsoProjection.MapToScreen(3, 1, 0);
            Assert.Equal((3 - 1) * 16, sx);
            Assert.Equal((3 + 1) * 8, sy);

            var (sx2, sy2) = IsoProjection.MapToScreen(2, 2, 1);
            Assert.Equal(0, sx2);
            Assert.Equal((2 + 2) * 8 - 1 * 24, sy2);
        }

        [Fact]
        public void WorldPosition_NegatesScreenY_ForUnitYUpWorld()
        {
            // MapToScreen's Y is SDL's Y-DOWN convention; Unity's world Y is
            // Y-UP, so WorldPosition must negate it (see IsoProjection.cs's
            // doc comment) - a tile further "south" (larger x+y, larger raw
            // screenY) must end up at a SMALLER Unity world Y, not larger.
            var (worldX, worldY) = IsoProjection.WorldPosition(3, 1, 0, pixelsPerUnit: 32f);
            Assert.Equal((3 - 1) * 16 / 32f, worldX);
            Assert.Equal(-(3 + 1) * 8 / 32f, worldY);
        }

        [Fact]
        public void WorldPosition_SouthTileEndsUpBelowNorthTile()
        {
            var (_, northY) = IsoProjection.WorldPosition(0, 0, 0, pixelsPerUnit: 32f);
            var (_, southY) = IsoProjection.WorldPosition(5, 5, 0, pixelsPerUnit: 32f);
            Assert.True(southY < northY);
        }

        [Fact]
        public void SortingOrder_HigherZAlwaysOutranksAnyLowerZTile()
        {
            int lowZLastTile = IsoProjection.SortingOrder(9, 9, 0, 10, 10, IsoProjection.PartRank.Object);
            int highZFirstTile = IsoProjection.SortingOrder(0, 0, 1, 10, 10, IsoProjection.PartRank.Floor);
            Assert.True(highZFirstTile > lowZLastTile);
        }

        [Fact]
        public void SortingOrder_WithinATile_PartsOrderFloorThenWallsThenObject()
        {
            int floor = IsoProjection.SortingOrder(5, 5, 0, 10, 10, IsoProjection.PartRank.Floor);
            int west = IsoProjection.SortingOrder(5, 5, 0, 10, 10, IsoProjection.PartRank.WestWall);
            int north = IsoProjection.SortingOrder(5, 5, 0, 10, 10, IsoProjection.PartRank.NorthWall);
            int obj = IsoProjection.SortingOrder(5, 5, 0, 10, 10, IsoProjection.PartRank.Object);
            Assert.True(floor < west);
            Assert.True(west < north);
            Assert.True(north < obj);
        }

        [Fact]
        public void SortingOrder_LaterYAtSameZOutranksEarlierY()
        {
            int earlier = IsoProjection.SortingOrder(9, 0, 0, 10, 10, IsoProjection.PartRank.Object);
            int later = IsoProjection.SortingOrder(0, 1, 0, 10, 10, IsoProjection.PartRank.Floor);
            Assert.True(later > earlier);
        }

        [Fact]
        public void SortingOrder_UnitRankIsAboveObjectButBelowTheNextTilesFloor()
        {
            int obj = IsoProjection.SortingOrder(5, 5, 0, 10, 10, IsoProjection.PartRank.Object);
            int unit = IsoProjection.SortingOrder(5, 5, 0, 10, 10, IsoProjection.PartRank.Unit);
            int nextTileFloor = IsoProjection.SortingOrder(6, 5, 0, 10, 10, IsoProjection.PartRank.Floor);
            Assert.True(unit > obj);
            Assert.True(unit < nextTileFloor);
        }

        [Fact]
        public void UnitSortingOrder_AlwaysExceedsAnyTilesSortingOrderInThatGrid()
        {
            int maxPossibleTileOrder = IsoProjection.SortingOrder(9, 9, 0, 10, 10, IsoProjection.PartRank.Object);
            int unitOrderAtOrigin = IsoProjection.UnitSortingOrder(0, 0, 0, 10, 10, IsoProjection.UnitPartRank.Legs);
            Assert.True(unitOrderAtOrigin > maxPossibleTileOrder);
        }

        [Fact]
        public void UnitSortingOrder_UsesZYXAsATiebreakAmongUnits()
        {
            int a = IsoProjection.UnitSortingOrder(1, 1, 0, 10, 10, IsoProjection.UnitPartRank.Legs);
            int b = IsoProjection.UnitSortingOrder(2, 1, 0, 10, 10, IsoProjection.UnitPartRank.Legs);
            Assert.True(b > a);
        }

        [Fact]
        public void UnitSortingOrder_WithinOneUnit_PartsOrderLegsThenRightArmThenTorsoThenLeftArm()
        {
            // Matches UnitSprite.cpp:620's direction-4 (south) blit order:
            // legs, rightArm, torso, leftArm, back-to-front.
            int legs = IsoProjection.UnitSortingOrder(5, 5, 0, 10, 10, IsoProjection.UnitPartRank.Legs);
            int rightArm = IsoProjection.UnitSortingOrder(5, 5, 0, 10, 10, IsoProjection.UnitPartRank.RightArm);
            int torso = IsoProjection.UnitSortingOrder(5, 5, 0, 10, 10, IsoProjection.UnitPartRank.Torso);
            int leftArm = IsoProjection.UnitSortingOrder(5, 5, 0, 10, 10, IsoProjection.UnitPartRank.LeftArm);
            Assert.True(legs < rightArm);
            Assert.True(rightArm < torso);
            Assert.True(torso < leftArm);
        }

        [Fact]
        public void UnitSortingOrder_NeverCollidesBetweenAdjacentUnits()
        {
            // No two units share a tileIndex (one occupant per tile), but the
            // per-part sub-order must still not let one unit's highest part
            // (LeftArm) collide with or exceed the next tile's lowest part (Legs).
            int highestPartAtEarlierTile = IsoProjection.UnitSortingOrder(4, 5, 0, 10, 10, IsoProjection.UnitPartRank.LeftArm);
            int lowestPartAtNextTile = IsoProjection.UnitSortingOrder(5, 5, 0, 10, 10, IsoProjection.UnitPartRank.Legs);
            Assert.True(highestPartAtEarlierTile < lowestPartAtNextTile);
        }

        [Fact]
        public void UnitSortingOrder_FiveDistinctPartRanksNeverCollideWithinOneUnit()
        {
            var ranks = new[]
            {
                IsoProjection.UnitPartRank.Legs, IsoProjection.UnitPartRank.RightArm,
                IsoProjection.UnitPartRank.Torso, IsoProjection.UnitPartRank.LeftArm,
                IsoProjection.UnitPartRank.Item,
            };
            var orders = new System.Collections.Generic.HashSet<int>();
            foreach (var rank in ranks)
                orders.Add(IsoProjection.UnitSortingOrder(3, 3, 0, mapWidth: 10, mapLength: 10, rank));

            Assert.Equal(5, orders.Count); // all 5 parts get distinct sort orders
        }

        [Fact]
        public void VoxelWorldPosition_OneTileOriginVoxel_MatchesWorldPositionAtTheEquivalentTile()
        {
            // Voxel (16,16,24) is exactly tile (1,1,1)'s origin corner (16 voxel
            // units/tile in X/Y, 24 in Z) - so VoxelWorldPosition at that voxel
            // must equal WorldPosition at tile (1,1,1).
            var (tileX, tileY) = IsoProjection.WorldPosition(1, 1, 1, pixelsPerUnit: 32f);
            var (voxelX, voxelY) = IsoProjection.VoxelWorldPosition(16f, 16f, 24f, pixelsPerUnit: 32f);
            Assert.Equal(tileX, voxelX, 3);
            Assert.Equal(tileY, voxelY, 3);
        }

        [Fact]
        public void VoxelWorldPosition_HalfTileVoxel_IsHalfwayBetweenTileOrigins()
        {
            var (x0, y0) = IsoProjection.VoxelWorldPosition(0f, 0f, 0f, pixelsPerUnit: 32f);
            var (x1, y1) = IsoProjection.WorldPosition(1, 0, 0, pixelsPerUnit: 32f);
            var (xHalf, yHalf) = IsoProjection.VoxelWorldPosition(8f, 0f, 0f, pixelsPerUnit: 32f);
            Assert.Equal((x0 + x1) / 2f, xHalf, 3);
            Assert.Equal((y0 + y1) / 2f, yHalf, 3);
        }

        [Fact]
        public void UnitSortingOrder_FitsWithinUnitySortingOrderInt16RangeForCulta00SizedGrids()
        {
            // Renderer.sortingOrder is stored internally as a 16-bit value even
            // though the public API type is int - values outside
            // short.MinValue..short.MaxValue silently wrap. Confirmed live in
            // the Editor: an earlier version of this formula gave CULTA00's
            // 10x10 grid a unit order of 100011, which Unity wrapped to
            // -31061 - putting units BEHIND every tile instead of in front,
            // leaving them just as invisible as before the fix.
            int order = IsoProjection.UnitSortingOrder(9, 9, 0, 10, 10, IsoProjection.UnitPartRank.LeftArm);
            Assert.InRange(order, short.MinValue, short.MaxValue);
        }
    }
}
