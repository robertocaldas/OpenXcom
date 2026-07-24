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
        /// legs, then right arm, then torso, then left arm, then the held
        /// item on top. The held-item layer (sourced from HANDOB.PCK,
        /// converted since Phase 9 Task 2) is rendered whenever the unit
        /// carries a weapon - see UnitRenderer.cs's Item SpriteRenderer,
        /// added in Phase 9 Task 5.
        /// </summary>
        public enum UnitPartRank
        {
            Legs = 0,
            RightArm = 1,
            Torso = 2,
            LeftArm = 3,
            Item = 4,
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
        /// A tile/unit's anchor position in Unity world units (Y-up). MapToScreen
        /// returns screenY in the original engine's SDL convention (Y-DOWN: bigger
        /// screenY = further down the physical screen). Unity's world Y is Y-UP,
        /// so screenY must be negated when adopted as a world coordinate - every
        /// MonoBehaviour that positions a tile/unit/cursor/arrow at a map
        /// coordinate must go through this helper (not divide MapToScreen's raw Y
        /// by pixelsPerUnit directly), or the map renders as a vertical mirror of
        /// the original layout. Still self-consistent for raycasting/click-to-tile
        /// if every consumer shared the same missing negation, but pre-baked
        /// directional art (path-preview arrows) encodes a specific on-screen
        /// direction that only reads correctly in the original's orientation.
        /// </summary>
        public static (float WorldX, float WorldY) WorldPosition(int x, int y, int z, float pixelsPerUnit)
        {
            var (screenX, screenY) = MapToScreen(x, y, z);
            return (screenX / pixelsPerUnit, -screenY / pixelsPerUnit);
        }

        /// <summary>
        /// Voxel-precision equivalent of WorldPosition, for animating along a
        /// traced voxel path (Phase 8's ProjectileFiredEvent.Trajectory)
        /// rather than snapping to tile centers. Deliberately NOT a literal
        /// port of Camera::convertVoxelToScreen (Camera.cpp:487-498): that
        /// C++ formula recenters from convertMapToScreen's raw output via a
        /// fixed +spriteWidth/2,+spriteHeight/2 constant because the
        /// original blits a sprite's TOP-LEFT corner there (SDL convention).
        /// This project's own convention is different and already
        /// established (TileRenderer's floor diamond, every unit sprite):
        /// every consumer of WorldPosition/MapToScreen treats its output as
        /// a tile's BOTTOM/near diamond-tip anchor (pivot 0.5,0 on every
        /// sprite in this codebase), not a blit corner - porting the C++
        /// recentering constant on top of that mismatched convention placed
        /// a voxel's projected point BELOW a unit's own anchor instead of
        /// above it (confirmed live: a shot's origin voxel, roughly chest
        /// height, projected lower than the shooter's own feet). Voxel
        /// coordinates are therefore treated as plain fractional tile
        /// coordinates and fed through the exact same formula MapToScreen
        /// uses at float precision - consistent with how every other point
        /// in this project's coordinate system already behaves.
        /// </summary>
        public static (float WorldX, float WorldY) VoxelWorldPosition(float voxelX, float voxelY, float voxelZ, float pixelsPerUnit)
        {
            float tileX = voxelX / 16f;
            float tileY = voxelY / 16f;
            float tileZ = voxelZ / 24f;
            float screenX = (tileX - tileY) * (SpriteWidth / 2f);
            float screenY = (tileX + tileY) * (SpriteWidth / 4f) - tileZ * ((SpriteHeight + SpriteWidth / 4f) / 2f);
            return (screenX / pixelsPerUnit, -screenY / pixelsPerUnit);
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
        /// among units, and a further tileIndex*5+part sub-order so a
        /// single unit's 5 body-part layers (UnitPartRank, including the
        /// held-item layer) stack in the correct back-to-front order without
        /// colliding with a neighboring unit's layers (no two units ever
        /// share a tileIndex - one occupant per tile). Unity's
        /// Renderer.sortingOrder is stored internally as a 16-bit value even
        /// though its public type is int, so values outside
        /// short.MinValue..short.MaxValue silently wrap - confirmed live in
        /// the Editor, where an earlier x1000-offset version gave CULTA00's
        /// 10x10 grid a unit order of 100011, which Unity wrapped to
        /// -31061, putting units BEHIND every tile instead of in front of
        /// them (still invisible, just for a different reason). [KNOWN
        /// LIMITATIONS] (1) a unit always draws in front of tall
        /// walls/objects too, not just floors - fine for CULTA00 (zero
        /// walls/objects), wrong for a future terrain where a unit should be
        /// hidden behind a tall object; (2) this still overflows the 16-bit
        /// range for a single-z-layer grid bigger than roughly 32x32 tiles
        /// (the tileIndex*5 sub-order roughly halves the previous ~73x73
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
            return (int)(unitBand + tileIndex * 5 + (int)part);
        }

        // World-Z window that all sortable map content is packed into. The
        // camera sits at Z=-10 looking toward +Z (near clip 0.3), so the
        // visible range is roughly Z in (-9.7, +990); "closer to camera" =
        // smaller (more negative) Z = drawn on top. Content is confined to
        // (-DepthWindow, 0]; the always-front tiers sit just past it, still
        // comfortably inside the frustum.
        private const float DepthWindow = 6f;
        private const float AlwaysFrontClearance = 1.5f; // gap between content and the always-front tiers

        /// <summary>
        /// A tile part's continuous world-Z depth - the same back-to-front
        /// order as SortingOrder, but as a bounded float instead of an int
        /// that silently wraps once it leaves Unity's internal 16-bit
        /// sortingOrder storage (confirmed live on a multi-story map: a
        /// craft's upper decks and every unit vanished because their raw
        /// SortingOrder/UnitSortingOrder values overflowed that range).
        ///
        /// The raw sort value grows without bound with map size (up to
        /// ~area*height*5), so a fixed per-unit step would, on a large map,
        /// push far tiles and every unit past the camera plane entirely and
        /// out of view. Dividing by the map footprint (mapWidth*mapLength)
        /// makes the depth map-size-independent - it lands in a fixed
        /// (-DepthWindow, 0] band that always stays inside the frustum -
        /// while preserving the exact draw ORDER (division by a positive
        /// constant is monotonic). Both the tile's collider (needs a
        /// physical Z for Physics.Raycast) and its visual draw order (the
        /// camera's custom-axis transparency sort, set up in CameraController)
        /// read this same value, so they can never disagree.
        /// </summary>
        public static float PartDepth(int x, int y, int z, int mapWidth, int mapLength, PartRank part) =>
            NormalizedDepth(SortingOrder(x, y, z, mapWidth, mapLength, part), mapWidth, mapLength);

        // Scales a raw sort value into the (-DepthWindow, 0] band. The
        // divisor 10 is the approximate maximum of sortValue/(area) across
        // realistic maps (unit band contributes ~5, plus ~5 per vertical
        // level), so DepthWindow/10 keeps content within the band with room
        // for a few stacked floors.
        private static float NormalizedDepth(long sortValue, int mapWidth, int mapLength) =>
            -(DepthWindow / 10f) * (float)sortValue / (mapWidth * mapLength);

        /// <summary>
        /// The tile-collider Z (Object-rank depth). sortingOrder has no effect
        /// on Physics.Raycast, so the collider needs a matching physical Z or
        /// a raycast through two overlapping tile/unit colliders resolves via
        /// Unity's unspecified internal tie-break, not by what's drawn on top
        /// (this produced both the "clicks select the wrong unit" and "tile
        /// cursor lands on the wrong tile" bugs). A raycast from the camera
        /// returns the nearest hit = smallest Z = topmost, matching the sort.
        /// </summary>
        public static float RaycastDepth(int x, int y, int z, int mapWidth, int mapLength) =>
            PartDepth(x, y, z, mapWidth, mapLength, PartRank.Object);

        /// <summary>Unit body-part equivalent of PartDepth.</summary>
        public static float UnitPartDepth(int x, int y, int z, int mapWidth, int mapLength, UnitPartRank part) =>
            NormalizedDepth(UnitSortingOrder(x, y, z, mapWidth, mapLength, part), mapWidth, mapLength);

        /// <summary>Unit equivalent of RaycastDepth - closer to the camera than
        /// any tile's RaycastDepth in the same column, so a raycast prefers the
        /// unit standing on a tile over the tile itself.</summary>
        public static float UnitRaycastDepth(int x, int y, int z, int mapWidth, int mapLength) =>
            UnitPartDepth(x, y, z, mapWidth, mapLength, UnitPartRank.Torso);

        /// <summary>
        /// A depth closer to the camera than any real tile/unit depth - the
        /// continuous-depth replacement for the old "sortingOrder =
        /// short.MaxValue" always-on-top trick (a fired bullet, the
        /// tile-selector cursor, the path-preview arrows). Sits just past the
        /// content band (which bottoms out near -DepthWindow) yet still inside
        /// the camera frustum. AlwaysFrontDepth is the frontmost tier (bullet,
        /// cursor front layer); AlwaysNearFrontDepth is one tier behind it
        /// (cursor back layer over an empty tile, path-preview arrows) -
        /// mirroring the old short.MaxValue / short.MaxValue-1 relationship.
        /// </summary>
        public const float AlwaysNearFrontDepth = -(DepthWindow + AlwaysFrontClearance);       // -7.5
        public const float AlwaysFrontDepth = -(DepthWindow + AlwaysFrontClearance + 0.5f);    // -8.0

        /// <summary>
        /// The tile-selector cursor's back layer, when the hovered tile IS
        /// occupied, must render behind that unit's own sprite (the unit
        /// stands "inside" the cursor box) while still staying above every
        /// tile part - a hair farther from the camera than the unit's own
        /// lowest part (Legs), matching the old "UnitSortingOrder(...Legs) - 1"
        /// raw-order trick (TileCursorView.Update).
        /// </summary>
        public static float CursorBackDepthBehindUnit(int x, int y, int z, int mapWidth, int mapLength) =>
            UnitPartDepth(x, y, z, mapWidth, mapLength, UnitPartRank.Legs) + 0.01f;
    }
}
