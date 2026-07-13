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

        private void Start()
        {
            string gameDataDir = Path.Combine(Application.dataPath, "GameData");

            var canvasGo = new GameObject("HudCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, worldPositionStays: false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;

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
    }
}
