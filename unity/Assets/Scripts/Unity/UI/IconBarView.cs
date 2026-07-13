using System.IO;
using OpenXcom.Unity.Rendering;
using UnityEngine;
using UnityEngine.UI;

namespace OpenXcom.Unity.UI
{
    /// <summary>
    /// The bottom icon bar: the converted ICONS.PCK background at its real
    /// 320x56 size, plus every button at its exact original pixel rect
    /// (BattlescapeState.cpp's hardcoded constructors - see the Phase 7
    /// plan's Global Constraints table). Only End Turn and Center are wired
    /// to real BattleState/CameraController actions; every other button
    /// renders the real icon art but does nothing - Core has no kneel/
    /// inventory/reserve-TU/next-soldier/show-layers/abort/stats-popup
    /// action yet (user's explicit choice: full icon bar layout, functional
    /// buttons only for what Core supports).
    /// </summary>
    public sealed class IconBarView : MonoBehaviour
    {
        private const float IconBarWidth = 320f;
        private const float IconBarHeight = 56f;

        private static readonly (string name, Rect rect, bool functional)[] Buttons =
        {
            ("UnitUp", new Rect(48, 0, 32, 16), false),
            ("UnitDown", new Rect(48, 16, 32, 16), false),
            ("MapUp", new Rect(80, 0, 32, 16), false),
            ("MapDown", new Rect(80, 16, 32, 16), false),
            ("ShowMap", new Rect(112, 0, 32, 16), false),
            ("Kneel", new Rect(112, 16, 32, 16), false),
            ("Inventory", new Rect(144, 0, 32, 16), false),
            ("Center", new Rect(144, 16, 32, 16), true),
            ("NextSoldier", new Rect(176, 0, 32, 16), false),
            ("PrevSoldier", new Rect(176, 16, 32, 16), false),
            ("ShowLayers", new Rect(208, 0, 32, 16), false),
            ("Help", new Rect(208, 16, 32, 16), false),
            ("EndTurn", new Rect(240, 0, 32, 16), true),
            ("Abort", new Rect(240, 16, 32, 16), false),
        };

        /// <summary>Reserved area for Task 7's SelectedUnitPanel (the "Stats panel" rect: 107,33,164,23).</summary>
        public RectTransform PanelParent { get; private set; }

        /// <summary>
        /// battleController/cameraController are passed in (not
        /// [SerializeField]) because HudBootstrap - the single owner of both
        /// references - is the only caller; IconBarView stays a pure builder
        /// with no Inspector wiring of its own, matching
        /// BattlescapeBootstrap's existing "everything constructed in code"
        /// convention.
        /// </summary>
        public void Build(RectTransform canvasRoot, string gameDataDir,
            BattleController battleController, CameraController cameraController)
        {
            var barGo = new GameObject("IconBar", typeof(RectTransform));
            var barRect = barGo.GetComponent<RectTransform>();
            barRect.SetParent(canvasRoot, worldPositionStays: false);
            barRect.anchorMin = new Vector2(0.5f, 0f);
            barRect.anchorMax = new Vector2(0.5f, 0f);
            barRect.pivot = new Vector2(0.5f, 0f);
            barRect.sizeDelta = new Vector2(IconBarWidth, IconBarHeight);
            barRect.anchoredPosition = Vector2.zero;

            var (texture, frameRects) = AtlasLoader.Load(gameDataDir, "icons");
            var bgImage = barGo.AddComponent<Image>();
            bgImage.sprite = Sprite.Create(texture, frameRects[0], new Vector2(0.5f, 0.5f));
            bgImage.type = Image.Type.Simple;

            foreach (var (name, rect, functional) in Buttons)
                BuildButton(barRect, name, rect, functional, battleController, cameraController);

            PanelParent = BuildPanelParent(barRect);
        }

        private void BuildButton(RectTransform barRect, string name, Rect rect, bool functional,
            BattleController battleController, CameraController cameraController)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(barRect, worldPositionStays: false);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(rect.width, rect.height);
            rt.anchoredPosition = new Vector2(rect.x, -rect.y);

            var image = go.AddComponent<Image>();
            image.color = functional
                ? new Color(0.25f, 0.55f, 0.25f, 0.55f)  // functional buttons: faint green tint
                : new Color(0.15f, 0.15f, 0.15f, 0.35f); // inert buttons: faint dark tint, still visible as a button-shaped region

            if (!functional)
                return;

            var button = go.AddComponent<Button>();
            if (name == "EndTurn")
                button.onClick.AddListener(() => battleController.EndTurnFromHud());
            else if (name == "Center")
                button.onClick.AddListener(() => cameraController.CenterOnSelectedUnit());
        }

        private RectTransform BuildPanelParent(RectTransform barRect)
        {
            var go = new GameObject("StatsPanel", typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(barRect, worldPositionStays: false);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(164, 23);
            rt.anchoredPosition = new Vector2(107, -33);
            return rt;
        }
    }
}
