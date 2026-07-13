using OpenXcom.Core.Common;
using Xunit;

namespace OpenXcom.Core.Tests
{
    public class DirectionsIndexOfTests
    {
        [Theory]
        [InlineData(0, -1, 0)]  // N
        [InlineData(1, -1, 1)]  // NE
        [InlineData(1, 0, 2)]   // E
        [InlineData(1, 1, 3)]   // SE
        [InlineData(0, 1, 4)]   // S
        [InlineData(-1, 1, 5)]  // SW
        [InlineData(-1, 0, 6)]  // W
        [InlineData(-1, -1, 7)] // NW
        public void IndexOf_MatchesOffsetsArrayIndex(int dx, int dy, int expectedIndex)
        {
            Assert.Equal(expectedIndex, Directions.IndexOf(new Position(dx, dy)));
        }

        [Fact]
        public void IndexOf_NonCompassDelta_ReturnsMinusOne()
        {
            Assert.Equal(-1, Directions.IndexOf(new Position(2, 2)));
        }
    }
}
