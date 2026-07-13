using System.Collections.Generic;
using OpenXcom.Unity.Rendering;
using UnityEngine;

namespace OpenXcom.Unity
{
    /// <summary>
    /// The flashing tile-selector box under the mouse (CT_NORMAL cursor,
    /// Map.cpp:1560's frame[CT_NORMAL] = 0, animated + (_animFrame/4)%2 -
    /// frames 0 and 1 of CURSOR.PCK, toggling every 400ms per
    /// DEFAULT_ANIM_SPEED=100ms * 4 ticks, BattlescapeState.h:118). Only the
    /// move cursor is ported this phase - no aim/psi/throw/waypoint cursor
    /// variants (frame[] indices 11/13/15 in the original), since this
    /// project's mouse interaction is move + fire only.
    /// </summary>
    public sealed class TileCursorView : MonoBehaviour
    {
        private const float FlashIntervalSeconds = 0.4f;

        private BattleController _battleController;
        private SpriteRenderer _renderer;
        private Sprite _frame0;
        private Sprite _frame1;

        public void Setup(BattleController battleController, (Texture2D texture, List<Rect> frameRects) cursorAtlas)
        {
            _battleController = battleController;

            var go = new GameObject("TileCursor");
            go.transform.SetParent(transform, worldPositionStays: false);
            _renderer = go.AddComponent<SpriteRenderer>();
            _renderer.sortingOrder = short.MaxValue; // always drawn on top

            _frame0 = Sprite.Create(cursorAtlas.texture, cursorAtlas.frameRects[0], new Vector2(0.5f, 0f), TileRenderer.PixelsPerUnit);
            _frame1 = Sprite.Create(cursorAtlas.texture, cursorAtlas.frameRects[1], new Vector2(0.5f, 0f), TileRenderer.PixelsPerUnit);
        }

        private void Update()
        {
            var hovered = _battleController.HoveredTile;
            if (hovered == null)
            {
                _renderer.enabled = false;
                return;
            }

            _renderer.enabled = true;
            var (screenX, screenY) = IsoProjection.MapToScreen(hovered.Value.X, hovered.Value.Y, hovered.Value.Z);
            _renderer.transform.localPosition = new Vector3(screenX / TileRenderer.PixelsPerUnit, screenY / TileRenderer.PixelsPerUnit, 0f);

            bool phase = Mathf.FloorToInt(Time.time / FlashIntervalSeconds) % 2 == 0;
            _renderer.sprite = phase ? _frame0 : _frame1;
        }
    }
}
