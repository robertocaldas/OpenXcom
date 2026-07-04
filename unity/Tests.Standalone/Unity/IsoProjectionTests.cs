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
    }
}
