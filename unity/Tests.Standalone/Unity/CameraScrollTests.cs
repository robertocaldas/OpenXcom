using OpenXcom.Unity.Rendering;
using Xunit;

namespace OpenXcom.Core.Tests.Unity
{
    public class CameraScrollTests
    {
        // Viewport 300x200 throughout - big enough that the 60px diagonal
        // zone and 5px border zone don't overlap in the "no scroll" cases.
        private const int ViewportWidth = 300;
        private const int ViewportHeight = 200;
        private const int ScrollSpeed = 8;

        [Fact]
        public void EdgeScrollDirection_MouseInDeadZone_NoScroll()
        {
            var (dx, dy) = CameraScroll.EdgeScrollDirection(150, 100, ViewportWidth, ViewportHeight, ScrollSpeed);
            Assert.Equal(0, dx);
            Assert.Equal(0, dy);
        }

        [Fact]
        public void EdgeScrollDirection_ExactlyAtBorder_NoScroll()
        {
            // Camera::mouseOver's checks are strict "<"/">" against SCROLL_BORDER,
            // so a mouse position exactly AT the border (5) does not trigger.
            var (dx, dy) = CameraScroll.EdgeScrollDirection(5, 100, ViewportWidth, ViewportHeight, ScrollSpeed);
            Assert.Equal(0, dx);
            Assert.Equal(0, dy);
        }

        [Fact]
        public void EdgeScrollDirection_LeftEdgeVerticallyCentered_ScrollsRightOnly()
        {
            var (dx, dy) = CameraScroll.EdgeScrollDirection(2, 100, ViewportWidth, ViewportHeight, ScrollSpeed);
            Assert.Equal(ScrollSpeed, dx);
            Assert.Equal(0, dy);
        }

        [Fact]
        public void EdgeScrollDirection_RightEdgeVerticallyCentered_ScrollsLeftOnly()
        {
            var (dx, dy) = CameraScroll.EdgeScrollDirection(298, 100, ViewportWidth, ViewportHeight, ScrollSpeed);
            Assert.Equal(-ScrollSpeed, dx);
            Assert.Equal(0, dy);
        }

        [Fact]
        public void EdgeScrollDirection_TopEdgeHorizontallyCentered_ScrollsUpOnly()
        {
            var (dx, dy) = CameraScroll.EdgeScrollDirection(150, 2, ViewportWidth, ViewportHeight, ScrollSpeed);
            Assert.Equal(0, dx);
            Assert.Equal(ScrollSpeed, dy);
        }

        [Fact]
        public void EdgeScrollDirection_BottomEdgeHorizontallyCentered_ScrollsDownOnly()
        {
            var (dx, dy) = CameraScroll.EdgeScrollDirection(150, 198, ViewportWidth, ViewportHeight, ScrollSpeed);
            Assert.Equal(0, dx);
            Assert.Equal(-ScrollSpeed, dy);
        }

        [Fact]
        public void EdgeScrollDirection_LeftEdgeNearTop_ScrollsDiagonallyAtHalfSpeed()
        {
            // mouseY=30 is inside the 60px diagonal zone but not inside the 5px
            // top border, so only the X-branch's diagonal halving applies.
            var (dx, dy) = CameraScroll.EdgeScrollDirection(2, 30, ViewportWidth, ViewportHeight, ScrollSpeed);
            Assert.Equal(ScrollSpeed, dx);
            Assert.Equal(ScrollSpeed / 2, dy);
        }

        [Fact]
        public void EdgeScrollDirection_TopLeftCorner_ScrollsDiagonallyAtHalfSpeedBothWays()
        {
            // Both border zones trigger: X-branch sets (8,4), then the
            // Y-branch overrides dy=8 then halves it back to 4 and keeps dx=8
            // (Camera::mouseOver's up-branch's "upleft" case).
            var (dx, dy) = CameraScroll.EdgeScrollDirection(2, 2, ViewportWidth, ViewportHeight, ScrollSpeed);
            Assert.Equal(ScrollSpeed, dx);
            Assert.Equal(ScrollSpeed / 2, dy);
        }

        [Fact]
        public void UnitsPerSecond_MatchesScrollSpeedOverIntervalOverPixelsPerUnit()
        {
            // 8px / 15ms tick = 533.33 px/s; / 32 px-per-unit = 16.67 units/s.
            float result = CameraScroll.UnitsPerSecond(8f, 15f, 32f);
            Assert.Equal(16.6667f, result, 3);
        }
    }
}
