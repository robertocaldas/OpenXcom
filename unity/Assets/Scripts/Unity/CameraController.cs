using OpenXcom.Unity.Rendering;
using OpenXcom.Unity.UI;
using UnityEngine;

namespace OpenXcom.Unity
{
    /// <summary>
    /// Ports Camera::mouseOver/keyboardPress/centerOnPosition
    /// (src/Battlescape/Camera.cpp) onto the Main Camera's transform, using
    /// OXCE's own defaults (Options.cpp): edge-scroll always on, arrow-key
    /// pan, Home centers on the selected unit, no zoom, drag-scroll off by
    /// default (not implemented this phase). [SIMPLIFIED] map-bounds
    /// clamping: the original keeps the exact screen-center map coordinate
    /// inside [0, mapSize-1] via an iterative inverse-projection check
    /// (Camera::scrollXY, Camera.cpp:326-350); this port instead clamps the
    /// camera's world position directly to the map's own screen-space
    /// bounding box (computed once from its 4 corners, no margin) -
    /// visually equivalent for CULTA00's flat 10x10 grid, cheaper, and
    /// doesn't need the original's per-frame inverse-projection iteration.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CameraController : MonoBehaviour
    {
        // Options.cpp / Camera.cpp / Map.h defaults - see the Phase 7 plan's
        // Global Constraints for the exact source lines these come from.
        private const int ScrollSpeed = 8;
        private const float ScrollIntervalMs = 15f;
        private const float IconBarHeightPixels = 56f;

        [SerializeField] private BattlescapeMapView mapView;

        private BattleController _battleController;
        private float _unitsPerSecond;

        private void Awake()
        {
            _unitsPerSecond = CameraScroll.UnitsPerSecond(ScrollSpeed, ScrollIntervalMs, TileRenderer.PixelsPerUnit);

            // The scene's serialized orthographic size (5) was never
            // recalibrated for a real display - it doesn't correspond to any
            // particular native pixel scale, so sprites end up much smaller
            // on screen than the original's crisp, blocky pixel art (which
            // always renders at an integer multiple of 1 texture pixel = 1
            // screen pixel). Computing it from Screen.height guarantees a
            // native 1:1 scale (1 texture pixel = 1 screen pixel) on
            // whatever resolution the Editor/build actually runs at, instead
            // of a value tuned for one specific window size.
            var camera = GetComponent<Camera>();
            if (camera.orthographic)
                camera.orthographicSize = Screen.height / (2f * TileRenderer.PixelsPerUnit);
        }

        private void Start()
        {
            // Deferred to Start (not Awake): BattleController lives on a
            // different GameObject than this camera, and Phase 6's
            // BattlescapeBootstrap.Start() is what finishes setting it up.
            _battleController = FindFirstObjectByType<BattleController>();
        }

        private void Update()
        {
            HandleEdgeScroll();
            HandleKeyScroll();
            if (Input.GetKeyDown(KeyCode.Home)) // keyBattleCenterUnit default
                CenterOnSelectedUnit();
            ClampToMapBounds();
        }

        private void HandleEdgeScroll()
        {
            // The icon bar's on-screen height scales with HudBootstrap's
            // CanvasScaler.scaleFactor (HudScale) - a fixed 56px band would be
            // wrong (too small) on any display where the HUD is upscaled.
            float viewportHeightPixels = Screen.height - IconBarHeightPixels * HudScale.ComputeScaleFactor(Screen.height);
            float mouseYFromTop = Screen.height - Input.mousePosition.y;
            if (mouseYFromTop > viewportHeightPixels)
                return; // mouse is over the icon bar, not the map viewport

            var (dx, dy) = CameraScroll.EdgeScrollDirection(
                (int)Input.mousePosition.x, (int)mouseYFromTop,
                Screen.width, (int)viewportHeightPixels, ScrollSpeed);

            if (dx != 0 || dy != 0)
                Pan(dx, dy);
        }

        private void HandleKeyScroll()
        {
            int dx = 0, dy = 0;
            if (Input.GetKey(KeyCode.LeftArrow)) dx += ScrollSpeed;
            if (Input.GetKey(KeyCode.RightArrow)) dx -= ScrollSpeed;
            if (Input.GetKey(KeyCode.UpArrow)) dy += ScrollSpeed;
            if (Input.GetKey(KeyCode.DownArrow)) dy -= ScrollSpeed;

            if (dx != 0 || dy != 0)
                Pan(dx, dy);
        }

        private void Pan(int dxTickPixels, int dyTickPixels)
        {
            // dxTickPixels/dyTickPixels are already scrollSpeed-scaled
            // (matches +-ScrollSpeed or +-ScrollSpeed/2); normalize to a
            // -1..1 direction then scale by the units-per-second rate.
            //
            // X: negated. The original's _mapOffset is added to every drawn
            // point's raw screen position (Camera.cpp:496-497); moving the
            // CAMERA to reproduce the same visible effect as increasing
            // _mapOffset therefore requires the opposite motion - camera
            // position and content offset are inverses of each other. This
            // holds for both axes and needs no coordinate-convention
            // adjustment on X, since screen-X-increases-rightward is the same
            // direction in both SDL and Unity.
            //
            // Y: NOT negated, unlike X. The original's screenY is SDL's
            // Y-DOWN convention (Camera.cpp:475-480, IsoProjection.MapToScreen
            // is a byte-for-byte port of that same formula), but every tile/
            // unit/cursor in this Unity port is placed via
            // IsoProjection.WorldPosition, which negates screenY to match
            // Unity's Y-UP world. That extra negation on the placement side
            // cancels the "camera moves opposite to content" negation on the
            // pan side, leaving Y unnegated here. Verified: with this sign,
            // hovering/pressing the physical top edge of the screen pans the
            // camera so new content enters from the top (matching every
            // standard edge-scroll convention) - the previous version (both
            // axes negated) did the opposite, which is the bug the user
            // reported as "camera up/down inverted".
            float dirX = -dxTickPixels / (float)ScrollSpeed;
            float dirY = dyTickPixels / (float)ScrollSpeed;
            transform.position += new Vector3(dirX, dirY, 0f) * _unitsPerSecond * Time.deltaTime;
        }

        /// <summary>Snaps the camera to the selected unit's tile. Called by Home and the HUD's Center button (Task 6).</summary>
        public void CenterOnSelectedUnit()
        {
            if (_battleController == null)
                _battleController = FindFirstObjectByType<BattleController>();
            if (_battleController?.Selected == null)
                return;

            var pos = _battleController.Selected.Position;
            var (worldX, worldY) = IsoProjection.WorldPosition(pos.X, pos.Y, pos.Z, TileRenderer.PixelsPerUnit);
            transform.position = new Vector3(worldX, worldY, transform.position.z);
        }

        private void ClampToMapBounds()
        {
            if (mapView == null || mapView.Grid == null)
                return;

            var grid = mapView.Grid;
            var (minCornerX, minCornerY) = IsoProjection.WorldPosition(0, grid.Length - 1, 0, TileRenderer.PixelsPerUnit);
            var (maxCornerX, maxCornerY) = IsoProjection.WorldPosition(grid.Width - 1, 0, 0, TileRenderer.PixelsPerUnit);
            var (topCornerX, topCornerY) = IsoProjection.WorldPosition(0, 0, 0, TileRenderer.PixelsPerUnit);
            var (botCornerX, botCornerY) = IsoProjection.WorldPosition(grid.Width - 1, grid.Length - 1, 0, TileRenderer.PixelsPerUnit);

            float minX = Mathf.Min(minCornerX, maxCornerX, topCornerX, botCornerX);
            float maxX = Mathf.Max(minCornerX, maxCornerX, topCornerX, botCornerX);
            float minY = Mathf.Min(minCornerY, maxCornerY, topCornerY, botCornerY);
            float maxY = Mathf.Max(minCornerY, maxCornerY, topCornerY, botCornerY);

            var pos = transform.position;
            pos.x = Mathf.Clamp(pos.x, minX, maxX);
            pos.y = Mathf.Clamp(pos.y, minY, maxY);
            transform.position = pos;
        }
    }
}
