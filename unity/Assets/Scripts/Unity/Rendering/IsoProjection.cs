namespace OpenXcom.Unity.Rendering
{
    /// <summary>
    /// Pure-math port of Camera::convertMapToScreen (src/Battlescape/Camera.cpp:475-480)
    /// and the per-tile-part draw order of Map::drawTerrain
    /// (src/Battlescape/Map.cpp:900-907, 939-1318). Intentionally has no
    /// UnityEngine dependency so it is unit-testable without the Editor;
    /// MonoBehaviours call into this for the actual pixel math.
    /// </summary>
    public static class IsoProjection
    {
        public const int SpriteWidth = 32;
        public const int SpriteHeight = 40;

        /// <summary>Tile-part draw rank within one tile: floor, west wall, north wall, object, unit.</summary>
        public enum PartRank
        {
            Floor = 0,
            WestWall = 1,
            NorthWall = 2,
            Object = 3,
            Unit = 4,
        }

        /// <summary>
        /// Tile-origin screen position (camera pan is added separately by the caller,
        /// matching Camera.cpp — convertMapToScreen itself never adds camera offset).
        /// </summary>
        public static (int ScreenX, int ScreenY) MapToScreen(int x, int y, int z)
        {
            int screenX = (x - y) * (SpriteWidth / 2);
            int screenY = (x + y) * (SpriteWidth / 4) - z * ((SpriteHeight + SpriteWidth / 4) / 2);
            return (screenX, screenY);
        }

        /// <summary>
        /// Monotonic back-to-front sorting order for one tile-part, reproducing
        /// the C++ draw loop's Z-outer, Y-middle, X-inner nesting plus the
        /// floor/west/north/object per-tile order (Map.cpp:900-907, 939-1318).
        /// </summary>
        public static int SortingOrder(int x, int y, int z, int mapWidth, int mapLength, PartRank part)
        {
            long tileIndex = ((long)z * mapLength + y) * mapWidth + x;
            return (int)(tileIndex * 5 + (int)part);
        }
    }
}
