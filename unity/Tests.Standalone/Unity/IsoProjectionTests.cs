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
