using UnityEngine;

namespace OpenXcom.Unity.Rendering
{
    /// <summary>
    /// Draws one unit as a single sprite, positioned via the same
    /// IsoProjection math TileRenderer uses. Pure display: takes an
    /// already-resolved Sprite and a tile position, makes no gameplay
    /// decisions. [SIMPLIFIED] one static frame - no direction/walk-phase
    /// animation this slice (parent spec §5 names that as later work).
    /// </summary>
    public sealed class UnitRenderer : MonoBehaviour
    {
        private SpriteRenderer _renderer;
        private BoxCollider _collider;

        private void Awake()
        {
            _renderer = gameObject.AddComponent<SpriteRenderer>();
            _collider = gameObject.AddComponent<BoxCollider>();
            _collider.size = new Vector3(0.6f, 1f, 0.1f);
        }

        public void Setup(int x, int y, int z, int mapWidth, int mapLength, Sprite sprite)
        {
            var (screenX, screenY) = IsoProjection.MapToScreen(x, y, z);
            transform.localPosition = new Vector3(screenX / TileRenderer.PixelsPerUnit, screenY / TileRenderer.PixelsPerUnit, 0f);

            _renderer.sprite = sprite;
            _renderer.sortingOrder = IsoProjection.UnitSortingOrder(x, y, z, mapWidth, mapLength);
        }
    }
}
