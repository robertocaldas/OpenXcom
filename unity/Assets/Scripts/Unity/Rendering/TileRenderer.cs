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
            var (screenX, screenY) = IsoProjection.MapToScreen(x, y, z);
            transform.localPosition = new Vector3(screenX / PixelsPerUnit, screenY / PixelsPerUnit, 0f);

            _floor.sprite = floorSprite;
            _floor.transform.localPosition = new Vector3(0f, -floorYOffsetPixels / PixelsPerUnit, 0f);
            _floor.sortingOrder = IsoProjection.SortingOrder(x, y, z, mapWidth, mapLength, IsoProjection.PartRank.Floor);

            _westWall.sprite = westWallSprite;
            _westWall.sortingOrder = IsoProjection.SortingOrder(x, y, z, mapWidth, mapLength, IsoProjection.PartRank.WestWall);

            _northWall.sprite = northWallSprite;
            _northWall.sortingOrder = IsoProjection.SortingOrder(x, y, z, mapWidth, mapLength, IsoProjection.PartRank.NorthWall);

            _object.sprite = objectSprite;
            _object.sortingOrder = IsoProjection.SortingOrder(x, y, z, mapWidth, mapLength, IsoProjection.PartRank.Object);
        }
    }
}
