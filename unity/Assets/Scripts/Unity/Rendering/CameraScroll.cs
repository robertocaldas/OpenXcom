namespace OpenXcom.Unity.Rendering
{
    /// <summary>
    /// Pure-math port of Camera::mouseOver's edge-scroll direction/diagonal-
    /// blend logic (src/Battlescape/Camera.cpp:119-224), using OXCE's default
    /// SCROLL_AUTO mode (always-on edge scroll, no click-drag trigger needed
    /// - Options::battleEdgeScroll's OXCE default, Options.cpp:143).
    /// Intentionally stateless: the original accumulates _scrollMouseX/Y
    /// across calls, but a MonoBehaviour can just call this fresh every
    /// Update() with the current mouse position, since the original's own
    /// per-call logic already fully recomputes the scroll vector from
    /// scratch (the only cross-call state it has - the "else if (posX) reset
    /// to 0" branches - exists purely to leave a value untouched when this
    /// frame's position is exactly 0, which a fresh stateless call already
    /// defaults to 0 for anyway).
    /// </summary>
    public static class CameraScroll
    {
        public const int ScrollBorder = 5;
        public const int ScrollDiagonalEdge = 60;

        /// <summary>
        /// mouseX/mouseY and viewportWidth/viewportHeight must all be in the
        /// same coordinate space: the MAP VIEWPORT only (i.e. already
        /// excluding the icon bar) with (0,0) at the viewport's top-left -
        /// matches Camera.cpp's own _screenWidth/_screenHeight, which are the
        /// Map surface's own dimensions, not the full screen
        /// (Camera.cpp:41-42: "_screenWidth(map->getWidth())").
        /// </summary>
        public static (int dx, int dy) EdgeScrollDirection(
            int mouseX, int mouseY, int viewportWidth, int viewportHeight, int scrollSpeed)
        {
            int dx = 0, dy = 0;

            // Left / right (Camera::mouseOver's first if/else-if block).
            if (mouseX < ScrollBorder && mouseX >= 0)
            {
                dx = scrollSpeed;
                if (mouseY < ScrollDiagonalEdge && mouseY >= 0) dy = scrollSpeed / 2;
                else if (mouseY > viewportHeight - ScrollDiagonalEdge) dy = -scrollSpeed / 2;
            }
            else if (mouseX > viewportWidth - ScrollBorder)
            {
                dx = -scrollSpeed;
                if (mouseY <= ScrollDiagonalEdge && mouseY >= 0) dy = scrollSpeed / 2;
                else if (mouseY > viewportHeight - ScrollDiagonalEdge) dy = -scrollSpeed / 2;
            }

            // Up / down (Camera::mouseOver's second if/else-if block - can
            // override dx/dy set above, exactly like the original).
            if (mouseY < ScrollBorder && mouseY >= 0)
            {
                dy = scrollSpeed;
                if (mouseX < ScrollDiagonalEdge && mouseX >= 0) { dx = scrollSpeed; dy /= 2; }
                else if (mouseX > viewportWidth - ScrollDiagonalEdge) { dx = -scrollSpeed; dy /= 2; }
            }
            else if (mouseY > viewportHeight - ScrollBorder)
            {
                dy = -scrollSpeed;
                if (mouseX < ScrollDiagonalEdge && mouseX >= 0) { dx = scrollSpeed; dy /= 2; }
                else if (mouseX > viewportWidth - ScrollDiagonalEdge) { dx = -scrollSpeed; dy /= 2; }
            }

            return (dx, dy);
        }

        /// <summary>
        /// Converts the original's px-per-tick scroll speed into world
        /// units per second for a MonoBehaviour driven by Time.deltaTime
        /// instead of a 15ms Timer (Map::SCROLL_INTERVAL, Map.h:63).
        /// </summary>
        public static float UnitsPerSecond(float scrollSpeedPxPerTick, float scrollIntervalMs, float pixelsPerUnit)
        {
            float pxPerSecond = scrollSpeedPxPerTick / (scrollIntervalMs / 1000f);
            return pxPerSecond / pixelsPerUnit;
        }
    }
}
