using OpenXcom.Unity.Rendering;
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
            float viewportHeightPixels = Screen.height - IconBarHeightPixels;
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
            // [FIXED - live, this task] Negated relative to the original
            // paper derivation. CameraScroll.EdgeScrollDirection's dx/dy are
            // a faithful port of Camera::mouseOver's delta to _mapOffset
            // (Camera.cpp:334-335: "_mapOffset.x += x"), and _mapOffset is
            // *subtracted* from the raw projected position to get the final
            // screen position's relationship to what's centered
            // (Camera::centerOnPosition, Camera.cpp:428:
            // "_mapOffset.x = -(screenPos.x - halfWidth)" => the raw-space
            // point at screen center is halfWidth - _mapOffset.x, so it
            // moves by -dx per scroll tick). This CameraController instead
            // sets transform.position directly to the raw projected position
            // of whatever should be centered (see CenterOnSelectedUnit
            // below), i.e. Unity's camera position plays the role of that
            // "raw-space center point", not of _mapOffset itself - so it
            // must move by -dx/-dy to match, not +dx/+dy. Confirmed live via
            // execute_script: EdgeScrollDirection returns dx=-8 when
            // hovering the right screen edge; applying that unnegated would
            // move the camera left (revealing content to the left) when the
            // mouse is at the right edge, which is backwards.
            float dirX = -dxTickPixels / (float)ScrollSpeed;
            float dirY = -dyTickPixels / (float)ScrollSpeed;
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
            var (screenX, screenY) = IsoProjection.MapToScreen(pos.X, pos.Y, pos.Z);
            transform.position = new Vector3(
                screenX / TileRenderer.PixelsPerUnit, screenY / TileRenderer.PixelsPerUnit, transform.position.z);
        }

        private void ClampToMapBounds()
        {
            if (mapView == null || mapView.Grid == null)
                return;

            var grid = mapView.Grid;
            var (minSx, minSy) = IsoProjection.MapToScreen(0, grid.Length - 1, 0);
            var (maxSx, maxSy) = IsoProjection.MapToScreen(grid.Width - 1, 0, 0);
            var (topSx, topSy) = IsoProjection.MapToScreen(0, 0, 0);
            var (botSx, botSy) = IsoProjection.MapToScreen(grid.Width - 1, grid.Length - 1, 0);

            float minX = Mathf.Min(minSx, maxSx, topSx, botSx) / TileRenderer.PixelsPerUnit;
            float maxX = Mathf.Max(minSx, maxSx, topSx, botSx) / TileRenderer.PixelsPerUnit;
            float minY = Mathf.Min(minSy, maxSy, topSy, botSy) / TileRenderer.PixelsPerUnit;
            float maxY = Mathf.Max(minSy, maxSy, topSy, botSy) / TileRenderer.PixelsPerUnit;

            var pos = transform.position;
            pos.x = Mathf.Clamp(pos.x, minX, maxX);
            pos.y = Mathf.Clamp(pos.y, minY, maxY);
            transform.position = pos;
        }
    }
}
