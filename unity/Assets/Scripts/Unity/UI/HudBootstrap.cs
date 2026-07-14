using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OpenXcom.Unity.UI
{
    /// <summary>
    /// Builds the Canvas (Constant Pixel Size, so the icon bar renders at
    /// its native resolution regardless of window size - OpenXcom itself
    /// supports arbitrary resolutions while keeping UI elements pixel-
    /// native, rather than uGUI's stretch-to-fit "Scale With Screen Size"),
    /// then the icon bar and selected-unit panel underneath it. Everything
    /// constructed in code, no Inspector drag-and-drop references required -
    /// matches BattlescapeBootstrap's existing convention.
    /// </summary>
    [RequireComponent(typeof(IconBarView))]
    [RequireComponent(typeof(SelectedUnitPanel))]
    public sealed class HudBootstrap : MonoBehaviour
    {
        [SerializeField] private BattleController battleController;
        [SerializeField] private CameraController cameraController;

        private CanvasScaler _scaler;

        private void Start()
        {
            string gameDataDir = Path.Combine(Application.dataPath, "GameData");

            var canvasGo = new GameObject("HudCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, worldPositionStays: false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            _scaler = canvasGo.AddComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            // A fixed scaleFactor of 1 draws the icon bar at its native 320x56
            // pixel size regardless of the actual display resolution - on
            // anything bigger than a tiny window that's a near-invisible speck
            // in the corner. Integer-upscale it instead, same idea as OXCE's
            // own resolution handling (HudScale's doc comment). Set every
            // Update(), not just here: in the Editor the Game view's actual
            // rendered pixel size can change after Start() runs (window
            // resize, docking layout, Game view resolution dropdown) without
            // any reload - a one-time value here goes stale and the scaled
            // HUD ends up sized for a viewport that no longer exists.
            _scaler.scaleFactor = HudScale.ComputeScaleFactor(Screen.height);

            canvasGo.AddComponent<GraphicRaycaster>();

            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var esGo = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            }

            var iconBar = GetComponent<IconBarView>();
            iconBar.Build(canvasGo.GetComponent<RectTransform>(), gameDataDir, battleController, cameraController);

            var panel = GetComponent<SelectedUnitPanel>();
            panel.Build(iconBar.PanelParent, battleController);
        }

        private void Update()
        {
            _scaler.scaleFactor = HudScale.ComputeScaleFactor(Screen.height);
        }
    }
}
