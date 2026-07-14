using System.Collections.Generic;
using OpenXcom.Unity.Rendering;
using UnityEngine;

namespace OpenXcom.Unity
{
    /// <summary>
    /// The tile-selector box under the mouse (CT_NORMAL cursor). Ported from
    /// Map.cpp's "Draw cursor back"/"Draw cursor front" blocks
    /// (Map.cpp:949-970, 1319-1334): the original draws TWO layers per tile
    /// (back, then front, with the tile/unit sprites drawn in between) to
    /// form a wireframe box outline - a single layer only shows half of that
    /// box. Frame selection also depends on tile occupancy: an EMPTY tile
    /// gets a static frame (back=0, front=3) - never animated; an OCCUPIED
    /// (visible) tile gets an animated frame that toggles every
    /// FlashIntervalSeconds (back=0/1, front=3/4, matching the original's
    /// halfAnimFrameRest = _animFrame % 2). The original never blinks over
    /// empty ground, only over a unit - opposite of this port's original
    /// (buggy) always-blink behavior. The front layer always renders above
    /// every tile/unit; the back layer renders above every tile part but
    /// BELOW the hovered tile's own unit (if any), so an occupied tile's
    /// cursor visually has the unit standing inside the box rather than the
    /// whole box drawn flatly on top of it.
    /// Only the move cursor is ported this phase - no aim/psi/throw/waypoint
    /// cursor variants (frame[] indices 11/13/15 in the original), since this
    /// project's mouse interaction is move + fire only.
    /// </summary>
    public sealed class TileCursorView : MonoBehaviour
    {
        private const float FlashIntervalSeconds = 0.4f;

        private const int BackStaticFrame = 0;
        private const int BackAnimFrameA = 0;
        private const int BackAnimFrameB = 1;
        private const int FrontStaticFrame = 3;
        private const int FrontAnimFrameA = 3;
        private const int FrontAnimFrameB = 4;

        private BattleController _battleController;
        private Transform _container;
        private SpriteRenderer _back;
        private SpriteRenderer _front;
        private (Texture2D texture, List<Rect> frameRects) _atlas;
        private int _mapWidth;
        private int _mapLength;

        public void Setup(BattleController battleController, (Texture2D texture, List<Rect> frameRects) cursorAtlas,
            int mapWidth, int mapLength)
        {
            _battleController = battleController;
            _atlas = cursorAtlas;
            _mapWidth = mapWidth;
            _mapLength = mapLength;

            var containerGo = new GameObject("TileCursor");
            containerGo.transform.SetParent(transform, worldPositionStays: false);
            _container = containerGo.transform;

            var backGo = new GameObject("Back");
            backGo.transform.SetParent(_container, worldPositionStays: false);
            _back = backGo.AddComponent<SpriteRenderer>();
            _back.sortingOrder = short.MaxValue - 1;

            var frontGo = new GameObject("Front");
            frontGo.transform.SetParent(_container, worldPositionStays: false);
            _front = frontGo.AddComponent<SpriteRenderer>();
            _front.sortingOrder = short.MaxValue;
        }

        private void Update()
        {
            var hovered = _battleController.HoveredTile;
            if (hovered == null)
            {
                _back.enabled = false;
                _front.enabled = false;
                return;
            }

            _back.enabled = true;
            _front.enabled = true;

            var (worldX, worldY) = IsoProjection.WorldPosition(
                hovered.Value.X, hovered.Value.Y, hovered.Value.Z, TileRenderer.PixelsPerUnit);
            _container.localPosition = new Vector3(worldX, worldY, 0f);

            bool occupied = _battleController.HoveredUnit != null;
            bool phase = Mathf.FloorToInt(Time.time / FlashIntervalSeconds) % 2 == 0;

            // Back layer must render BEHIND the unit standing on this tile (the
            // original's box appears to have the unit standing "inside" it),
            // not above it like the front layer and every tile part - one less
            // than the unit's own lowest part order guarantees that while
            // staying above every tile part (UnitSortingOrder always exceeds
            // any tile's SortingOrder in the same grid).
            _back.sortingOrder = occupied
                ? IsoProjection.UnitSortingOrder(hovered.Value.X, hovered.Value.Y, hovered.Value.Z,
                    _mapWidth, _mapLength, IsoProjection.UnitPartRank.Legs) - 1
                : short.MaxValue - 1;

            _back.sprite = FrameSprite(occupied ? (phase ? BackAnimFrameA : BackAnimFrameB) : BackStaticFrame);
            _front.sprite = FrameSprite(occupied ? (phase ? FrontAnimFrameA : FrontAnimFrameB) : FrontStaticFrame);
        }

        private Sprite FrameSprite(int frameIndex) =>
            Sprite.Create(_atlas.texture, _atlas.frameRects[frameIndex], new Vector2(0.5f, 0f), TileRenderer.PixelsPerUnit);
    }
}
