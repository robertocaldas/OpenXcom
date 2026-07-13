using OpenXcom.Core.Battle;
using UnityEngine;
using UnityEngine.UI;

namespace OpenXcom.Unity.UI
{
    /// <summary>
    /// Name/TU/Health/Energy readouts for BattleController.Selected, placed
    /// inside IconBarView's reserved "Stats panel" rect (107,33,164,23 -
    /// BattlescapeState.cpp's hardcoded layout). No Morale bar: BattleUnit
    /// has no morale field (morale/panic is out of scope this project-wide,
    /// not just this phase) - the row is omitted rather than faked with a
    /// placeholder value. Numeric positions below are each field's rect from
    /// BattlescapeState.cpp, translated to be relative to the Stats panel's
    /// own origin (subtract 107,33 from each original x+107,y+33 pair).
    /// </summary>
    public sealed class SelectedUnitPanel : MonoBehaviour
    {
        private BattleController _battleController;
        private Text _nameText;
        private Text _tuText;
        private Text _energyText;
        private Text _healthText;
        private Slider _tuBar;
        private Slider _energyBar;
        private Slider _healthBar;

        public void Build(RectTransform panelParent, BattleController battleController)
        {
            _battleController = battleController;

            _nameText = BuildText(panelParent, "Name", new Vector2(28, -0), new Vector2(136, 10));
            _tuText = BuildText(panelParent, "TuText", new Vector2(29, -9), new Vector2(15, 5));
            _energyText = BuildText(panelParent, "EnergyText", new Vector2(47, -9), new Vector2(15, 5));
            _healthText = BuildText(panelParent, "HealthText", new Vector2(29, -17), new Vector2(15, 5));

            _tuBar = BuildBar(panelParent, "TuBar", new Vector2(63, -8), new Vector2(102, 3), new Color(0.25f, 0.35f, 0.85f));
            _energyBar = BuildBar(panelParent, "EnergyBar", new Vector2(63, -12), new Vector2(102, 3), new Color(0.85f, 0.65f, 0.15f));
            _healthBar = BuildBar(panelParent, "HealthBar", new Vector2(63, -16), new Vector2(102, 3), new Color(0.15f, 0.75f, 0.25f));
        }

        private static Text BuildText(RectTransform parent, string name, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;

            var text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 8;
            text.color = Color.white;
            text.alignment = TextAnchor.UpperLeft;
            return text;
        }

        private static Slider BuildBar(RectTransform parent, string name, Vector2 pos, Vector2 size, Color fillColor)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;

            var slider = go.AddComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.interactable = false;
            slider.transition = Selectable.Transition.None;

            var fillAreaGo = new GameObject("FillArea", typeof(RectTransform));
            var fillAreaRt = fillAreaGo.GetComponent<RectTransform>();
            fillAreaRt.SetParent(rt, worldPositionStays: false);
            fillAreaRt.anchorMin = Vector2.zero;
            fillAreaRt.anchorMax = Vector2.one;
            fillAreaRt.sizeDelta = Vector2.zero;

            var fillGo = new GameObject("Fill", typeof(RectTransform));
            var fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.SetParent(fillAreaRt, worldPositionStays: false);
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.sizeDelta = Vector2.zero;
            var fillImage = fillGo.AddComponent<Image>();
            fillImage.color = fillColor;

            slider.fillRect = fillRt;
            slider.targetGraphic = fillImage;
            return slider;
        }

        private void Update()
        {
            var unit = _battleController.Selected;
            bool hasSelection = unit != null;

            _nameText.text = hasSelection ? unit.Name : "";
            _tuText.text = hasSelection ? unit.TimeUnits.ToString() : "";
            _energyText.text = hasSelection ? unit.Energy.ToString() : "";
            _healthText.text = hasSelection ? unit.Health.ToString() : "";

            _tuBar.value = hasSelection && unit.Stats.TimeUnits > 0 ? (float)unit.TimeUnits / unit.Stats.TimeUnits : 0f;
            _energyBar.value = hasSelection && unit.Stats.Stamina > 0 ? (float)unit.Energy / unit.Stats.Stamina : 0f;
            _healthBar.value = hasSelection && unit.Stats.Health > 0 ? (float)unit.Health / unit.Stats.Health : 0f;
        }
    }
}
