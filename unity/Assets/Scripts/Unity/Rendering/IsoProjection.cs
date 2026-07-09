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
        /// A unit's body-part draw rank, south-facing (direction 4) standing
        /// pose only - the back-to-front blit order UnitSprite.cpp's
        /// drawRoutine0 uses for direction 4 (UnitSprite.cpp:620):
        /// legs, then right arm, then torso, then left arm. No held-item
        /// layer this slice (HANDOB.PCK is not converted), so units render
        /// unarmed even though they carry a weapon in game logic.
        /// </summary>
        public enum UnitPartRank
        {
            Legs = 0,
            RightArm = 1,
            Torso = 2,
            LeftArm = 3,
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

        /// <summary>
        /// A unit body-part's sorting order: always above every tile part in
        /// a grid of this size, never sharing numeric space with tile parts.
        /// Unlike tile parts, a unit must never be occluded by ANY tile -
        /// including a neighboring tile whose sprite's full
        /// (non-diamond-trimmed) rectangular bounds extend into this tile's
        /// screen footprint (observed with CULTA00: its one shared floor
        /// sprite is tall enough to fully cover a unit standing one row
        /// toward the camera). SortingOrder's tile-index scheme can't
        /// express "in front of every tile," so units get their own band
        /// offset above the highest SortingOrder any tile in a
        /// mapWidth x mapLength grid could reach (tiles top out at
        /// tileIndex*5+3), with the same (z,y,x) ordering as a tiebreak
        /// among units, and a further tileIndex*4+part sub-order so a
        /// single unit's 4 body-part layers (UnitPartRank) stack in the
        /// correct back-to-front order without colliding with a
        /// neighboring unit's layers (no two units ever share a tileIndex -
        /// one occupant per tile). Unity's Renderer.sortingOrder is stored
        /// internally as a 16-bit value even though its public type is int,
        /// so values outside short.MinValue..short.MaxValue silently wrap -
        /// confirmed live in the Editor, where an earlier x1000-offset
        /// version gave CULTA00's 10x10 grid a unit order of 100011, which
        /// Unity wrapped to -31061, putting units BEHIND every tile instead
        /// of in front of them (still invisible, just for a different
        /// reason). [KNOWN LIMITATIONS] (1) a unit always draws in front of
        /// tall walls/objects too, not just floors - fine for CULTA00 (zero
        /// walls/objects), wrong for a future terrain where a unit should be
        /// hidden behind a tall object; (2) this still overflows the 16-bit
        /// range for a single-z-layer grid bigger than roughly 36x36 tiles
        /// (the tileIndex*4 sub-order roughly halves the previous ~73x73
        /// headroom) - fine for CULTA00 (10x10), would need revisiting (e.g.
        /// a real Unity sorting layer instead of a numeric offset) for a
        /// much larger map; (3) the band is sized from mapWidth*mapLength
        /// only, not mapHeight - SortingOrder's tileIndex includes z as its
        /// outermost factor, so ANY z>=1 tile already reaches
        /// tileIndex=mapWidth*mapLength, colliding with (or exceeding) this
        /// band regardless of how small the grid footprint is. Harmless for
        /// CULTA00 (Height=1) but this guarantee does not hold at all for a
        /// multi-story map - revisit before loading one.
        /// </summary>
        public static int UnitSortingOrder(int x, int y, int z, int mapWidth, int mapLength, UnitPartRank part)
        {
            long tileIndex = ((long)z * mapLength + y) * mapWidth + x;
            long unitBand = (long)mapWidth * mapLength * 5;
            return (int)(unitBand + tileIndex * 4 + (int)part);
        }
    }
}
