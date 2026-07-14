using System.Collections.Generic;
using OpenXcom.Core.Battle;
using OpenXcom.Core.Common;
using OpenXcom.Unity.Rendering;
using UnityEngine;

namespace OpenXcom.Unity
{
    /// <summary>
    /// TU-cost-colored path arrows from the selected unit to the hovered
    /// tile - yellow (affordable) or red (not), per-step
    /// (Rendering.PathPreview.ComputeAffordability). [SIMPLIFIED]: one arrow
    /// sprite per step, tinted directly, rather than the original's two-pass
    /// neutral-base + colored-overlay compositing (Map.cpp:1288-1301,
    /// :1635-1640) - see CameraController's and the Phase 7 plan's Global
    /// Constraints for why. Frame index = compass direction from the
    /// previous step (Directions.IndexOf), matching the Pathfinding.png
    /// sheet's direction-ordered first 8 frames (0=N..7=NW).
    /// </summary>
    public sealed class PathPreviewView : MonoBehaviour
    {
        private static readonly Color Affordable = new(1f, 0.85f, 0.1f, 0.9f);  // yellow
        private static readonly Color Unaffordable = new(0.9f, 0.15f, 0.1f, 0.9f); // red

        private BattleController _battleController;
        private BattleState _state;
        private (Texture2D texture, List<Rect> frameRects) _pathAtlas;
        private readonly List<SpriteRenderer> _arrows = new();

        public void Setup(BattleController battleController, BattleState state,
            (Texture2D texture, List<Rect> frameRects) pathAtlas)
        {
            _battleController = battleController;
            _state = state;
            _pathAtlas = pathAtlas;
        }

        private void Update()
        {
            ClearArrows();

            var selected = _battleController.Selected;
            var hovered = _battleController.HoveredTile;
            if (selected == null || hovered == null || selected.Position == hovered.Value)
                return;

            var path = Pathfinding.FindPath(_state.Grid, selected.Position, hovered.Value);
            if (path == null || path.Count == 0)
                return;

            var affordability = PathPreview.ComputeAffordability(path, selected.TimeUnits);

            var previous = selected.Position;
            for (int i = 0; i < path.Count; i++)
            {
                var step = path[i];
                int dirIndex = Directions.IndexOf(step.Position - previous);
                previous = step.Position;
                if (dirIndex < 0)
                    continue; // shouldn't happen (Pathfinding only takes 8-dir steps), skip defensively

                var arrowGo = new GameObject($"Arrow_{i}");
                arrowGo.transform.SetParent(transform, worldPositionStays: false);
                var renderer = arrowGo.AddComponent<SpriteRenderer>();
                renderer.sortingOrder = short.MaxValue - 1; // below the tile cursor, above everything else
                renderer.sprite = Sprite.Create(_pathAtlas.texture, _pathAtlas.frameRects[dirIndex], new Vector2(0.5f, 0f), TileRenderer.PixelsPerUnit);
                renderer.color = affordability[i] == PathPreview.Affordability.Affordable ? Affordable : Unaffordable;

                var (worldX, worldY) = IsoProjection.WorldPosition(step.Position.X, step.Position.Y, step.Position.Z, TileRenderer.PixelsPerUnit);
                arrowGo.transform.localPosition = new Vector3(worldX, worldY, 0f);

                _arrows.Add(renderer);
            }
        }

        private void ClearArrows()
        {
            foreach (var arrow in _arrows)
                Destroy(arrow.gameObject);
            _arrows.Clear();
        }
    }
}
