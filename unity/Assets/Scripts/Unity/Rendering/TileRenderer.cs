using UnityEngine;

namespace OpenXcom.Unity.Rendering
{
    /// <summary>
    /// Draws one battlefield tile's up-to-4 parts (floor/west wall/north
    /// wall/object) as child SpriteRenderers, positioned and sorted per
    /// IsoProjection. Pure display: takes already-resolved Sprites and a
    /// tile position, makes no gameplay decisions.
    /// </summary>
    public sealed class TileRenderer : MonoBehaviour
    {
        /// <summary>
        /// Must match the pixelsPerUnit passed to Sprite.Create for these
        /// sprites (BattlescapeMapView.SpriteFor). IsoProjection.MapToScreen
        /// returns raw pixel offsets (e.g. 16, 8, 24...) sized for the
        /// original SDL-style 1-pixel-per-unit renderer; Unity's
        /// Transform.localPosition is in world units, where a sprite's
        /// on-screen size is (texture pixels / pixelsPerUnit). Without this
        /// conversion, tiles would be positioned ~32x farther apart than
        /// their sprites are wide/tall.
        /// </summary>
        public const float PixelsPerUnit = 32f;

        private SpriteRenderer _floor;
        private SpriteRenderer _westWall;
        private SpriteRenderer _northWall;
        private SpriteRenderer _object;

        private void Awake()
        {
            _floor = CreateChild("Floor");
            _westWall = CreateChild("WestWall");
            _northWall = CreateChild("NorthWall");
            _object = CreateChild("Object");

            // The flat floor diamond's true footprint (32x16px = 1x0.5 units)
            // is a rhombus, not a rectangle - its two diagonals differ in
            // length (1 vs 0.5), so no axis-aligned (or rotated) BoxCollider
            // can represent it exactly. A box sized to the diamond's full
            // bounding box overlaps up to half its area into every diagonal
            // neighbor's identical box (misattributing clicks well inside what
            // looks like the correct tile); a box shrunk to the diamond's
            // inscribed rectangle avoids overlap but leaves gaps near every
            // edge where no collider exists at all (hover/click silently hits
            // nothing there). A 2-triangle mesh matching the diamond exactly
            // is gapless AND non-overlapping between neighbors.
            var collider = gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = BuildDiamondMesh();
        }

        private static Mesh BuildDiamondMesh()
        {
            // Floor sprite is bottom-anchored (pivot 0.5,0), so its diamond's
            // bottom point sits at this transform's local origin, matching
            // TileRenderer.Setup's world-position placement.
            var mesh = new Mesh
            {
                vertices = new[]
                {
                    new Vector3(0f, 0f, 0f),       // bottom
                    new Vector3(0.5f, 0.25f, 0f),  // right
                    new Vector3(0f, 0.5f, 0f),     // top
                    new Vector3(-0.5f, 0.25f, 0f), // left
                },
                // Both windings per triangle so the raycast (traveling +Z from
                // the camera at Z=-10) hits regardless of face-culling direction.
                triangles = new[] { 0, 1, 2, 0, 2, 1, 0, 2, 3, 0, 3, 2 },
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private SpriteRenderer CreateChild(string childName)
        {
            var go = new GameObject(childName);
            go.transform.SetParent(transform, worldPositionStays: false);
            return go.AddComponent<SpriteRenderer>();
        }

        /// <summary>
        /// Positions this tile at (x,y,z) and assigns each part's sprite (null
        /// = nothing in that slot, matching Core's nullable MapDataTile refs).
        /// yOffsetPixels shifts the floor sprite per MapDataTile.YOffset
        /// (Map.cpp:939-946's "-tile->getYOffset(O_FLOOR)").
        /// </summary>
        public void Setup(int x, int y, int z, int mapWidth, int mapLength,
            Sprite floorSprite, int floorYOffsetPixels,
            Sprite westWallSprite, Sprite northWallSprite, Sprite objectSprite)
        {
            var (worldX, worldY) = IsoProjection.WorldPosition(x, y, z, PixelsPerUnit);
            float depth = IsoProjection.RaycastDepth(x, y, z, mapWidth, mapLength); // == PartDepth(..., Object)
            transform.localPosition = new Vector3(worldX, worldY, depth);

            // Children are positioned relative to this root, whose own Z is
            // fixed at the Object-rank depth above; each child's local Z is
            // the delta needed to reach ITS part's real depth, so the actual
            // world Z (root + local) matches PartDepth exactly for every
            // part, driving the camera's custom-axis transparency sort
            // (see CameraController) the same way sortingOrder used to -
            // just without its 16-bit overflow ceiling.
            _floor.sprite = floorSprite;
            // Original: screenPosition.y - yOffset (SDL Y-down: subtracting moves
            // the sprite UP the physical screen). In Unity's now-correctly-oriented
            // Y-up world, "up" is +Y, so the offset's sign flips to +.
            _floor.transform.localPosition = new Vector3(0f, floorYOffsetPixels / PixelsPerUnit, PartDepthOffset(x, y, z, mapWidth, mapLength, IsoProjection.PartRank.Floor));

            _westWall.sprite = westWallSprite;
            _westWall.transform.localPosition = new Vector3(0f, 0f, PartDepthOffset(x, y, z, mapWidth, mapLength, IsoProjection.PartRank.WestWall));

            _northWall.sprite = northWallSprite;
            _northWall.transform.localPosition = new Vector3(0f, 0f, PartDepthOffset(x, y, z, mapWidth, mapLength, IsoProjection.PartRank.NorthWall));

            _object.sprite = objectSprite;
            _object.transform.localPosition = new Vector3(0f, 0f, PartDepthOffset(x, y, z, mapWidth, mapLength, IsoProjection.PartRank.Object));
        }

        private static float PartDepthOffset(int x, int y, int z, int mapWidth, int mapLength, IsoProjection.PartRank part) =>
            IsoProjection.PartDepth(x, y, z, mapWidth, mapLength, part)
                - IsoProjection.PartDepth(x, y, z, mapWidth, mapLength, IsoProjection.PartRank.Object);
    }
}
