using System.Collections.Generic;
using OpenXcom.Core.Battle;
using OpenXcom.Core.Common;
using OpenXcom.Core.Rules;
using OpenXcom.Unity.Rendering;
using UnityEngine;

namespace OpenXcom.Unity
{
    /// <summary>
    /// Click a unit's GameObject to select it; click a tile to path there.
    /// Pure input + animation glue: all move legality/TU accounting lives in
    /// OpenXcom.Core.Battle.BattleState.TryMove - this class only translates
    /// mouse clicks into calls on it and drains/animates the resulting
    /// BattleEvents. Makes no gameplay decisions of its own.
    /// </summary>
    public sealed class BattleController : MonoBehaviour
    {
        [SerializeField] private Camera raycastCamera;
        [SerializeField] private float tilesPerSecond = 4f;

        private BattleState _state;
        private BattleUnit _selected;
        private readonly Dictionary<BattleUnit, Transform> _unitTransforms = new();

        /// <summary>The currently-selected unit, or null. Read by Phase 7's HUD/cursor views.</summary>
        public BattleUnit Selected => _selected;

        /// <summary>The tile under the mouse this frame, or null (cursor over
        /// the icon bar, over a unit instead, or off the map). Recomputed
        /// every Update() by UpdateHover - read by Phase 7's
        /// TileCursorView/PathPreviewView.</summary>
        public Position? HoveredTile { get; private set; }

        /// <summary>The unit under the mouse this frame, or null. Recomputed
        /// every Update() by UpdateHover.</summary>
        public BattleUnit HoveredUnit { get; private set; }

        /// <summary>One unit's in-progress walk animation. A turn can move
        /// several units (e.g. the AI's hostile turn), so this is a list, not
        /// a single slot - a single-slot design silently clobbers all but the
        /// last unit's animation whenever more than one UnitMovedEvent is
        /// drained in the same call.</summary>
        private sealed class UnitAnimation
        {
            public Transform Transform;
            public Queue<Position> Queue;
            public Vector3 Target;
        }

        private readonly List<UnitAnimation> _activeAnimations = new();

        /// <summary>Wires this controller to an already-populated battle. Called by whichever scene bootstrap owns squad setup.</summary>
        public void Bind(BattleState state, IReadOnlyDictionary<BattleUnit, Transform> unitTransforms)
        {
            _state = state;
            _unitTransforms.Clear();
            foreach (var kv in unitTransforms)
                _unitTransforms[kv.Key] = kv.Value;
        }

        private void Update()
        {
            UpdateHover();

            if (_activeAnimations.Count > 0)
            {
                AdvanceAnimations();
                return; // don't accept new input while any unit is mid-animation
            }

            if (_state == null)
                return;

            if (_state.IsBattleOver)
                return; // no further input once the battle is decided

            // keyBattleEndTurn's OXCE default (Options.cpp:333) - NOT Space.
            if (Input.GetKeyDown(KeyCode.Backspace))
            {
                _state.EndPlayerTurn();
                DrainAndAnimate();
                return;
            }

            if (Input.GetMouseButtonDown(1)) // right-click: fire at a targeted unit
            {
                HandleFireClick();
                return;
            }

            if (!Input.GetMouseButtonDown(0)) // left-click: select / move
                return;

            if (HoveredUnit != null)
            {
                _selected = HoveredUnit;
                return;
            }

            if (_selected == null || HoveredTile == null)
                return;

            var result = _state.TryMove(_selected, HoveredTile.Value);
            if (result.Outcome == MoveOutcome.Failed)
                return;

            DrainAndAnimate();
        }

        /// <summary>
        /// One raycast per frame from the current mouse position, resolving
        /// HoveredUnit/HoveredTile - replaces the old per-click raycasts in
        /// Update/HandleFireClick, so both click handling and Phase 7's
        /// hover-driven views (tile cursor, path preview) share one hit test
        /// instead of raycasting twice per frame.
        /// </summary>
        private void UpdateHover()
        {
            HoveredUnit = null;
            HoveredTile = null;

            if (raycastCamera == null)
                return;

            var ray = raycastCamera.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out var hit))
                return;

            HoveredUnit = FindUnitAt(hit.transform);
            if (HoveredUnit == null)
                HoveredTile = TileUnderCursor(hit);
        }

        private void HandleFireClick()
        {
            if (_selected == null || _selected.RightHand == null)
                return;

            if (HoveredUnit == null || HoveredUnit == _selected)
                return;

            _state.TryFire(_selected, _selected.RightHand, BattleActionType.AimedShot, HoveredUnit);
            DrainAndAnimate();
        }

        private BattleUnit FindUnitAt(Transform hitTransform)
        {
            foreach (var kv in _unitTransforms)
                if (kv.Value == hitTransform)
                    return kv.Key;
            return null;
        }

        private Position TileUnderCursor(RaycastHit hit)
        {
            // Inverse of IsoProjection.MapToScreen; left as a direct pixel/world
            // lookup against the hit tile's own TileRenderer name ("Tile_x_y_z").
            // Reads hit.transform directly, NOT .parent: TileRenderer's
            // BoxCollider sits on the Tile_x_y_z GameObject itself.
            var parts = hit.transform.name.Split('_');
            return new Position(int.Parse(parts[1]), int.Parse(parts[2]), int.Parse(parts[3]));
        }

        private void DrainAndAnimate()
        {
            foreach (var evt in _state.DequeueEvents())
            {
                if (evt is UnitMovedEvent moved && _unitTransforms.TryGetValue(moved.Unit, out var t))
                {
                    _activeAnimations.Add(new UnitAnimation
                    {
                        Transform = t,
                        Queue = new Queue<Position>(moved.Path),
                        Target = t.localPosition,
                    });
                }
                else if (evt is ProjectileFiredEvent fired)
                {
                    Debug.Log(fired.Hit
                        ? $"{fired.Attacker.Name} hits {fired.Defender.Name}"
                        : $"{fired.Attacker.Name} misses {fired.Defender.Name}");
                }
                else if (evt is UnitHitEvent hitEvent)
                {
                    Debug.Log($"{hitEvent.Unit.Name} takes {hitEvent.Damage} damage ({hitEvent.Side})");
                }
                else if (evt is UnitDiedEvent died && _unitTransforms.TryGetValue(died.Unit, out var deadTransform))
                {
                    deadTransform.gameObject.SetActive(false);
                    _unitTransforms.Remove(died.Unit);
                    if (_selected == died.Unit)
                        _selected = null;
                }
                else if (evt is TurnChangedEvent turnChanged)
                {
                    Debug.Log($"Turn changed: {turnChanged.Faction}");
                }
                else if (evt is BattleOverEvent battleOver)
                {
                    Debug.Log($"Battle over: {battleOver.Outcome}");
                }
            }
        }

        private void AdvanceAnimations()
        {
            for (int i = _activeAnimations.Count - 1; i >= 0; i--)
            {
                var anim = _activeAnimations[i];

                if (anim.Queue.Count == 0 && Vector3.Distance(anim.Transform.localPosition, anim.Target) < 0.01f)
                {
                    _activeAnimations.RemoveAt(i);
                    continue;
                }

                if (Vector3.Distance(anim.Transform.localPosition, anim.Target) < 0.01f)
                {
                    var next = anim.Queue.Dequeue();
                    var (sx, sy) = IsoProjection.MapToScreen(next.X, next.Y, next.Z);
                    anim.Target = new Vector3(sx / TileRenderer.PixelsPerUnit, sy / TileRenderer.PixelsPerUnit, 0f);
                }

                anim.Transform.localPosition = Vector3.MoveTowards(
                    anim.Transform.localPosition, anim.Target, tilesPerSecond * Time.deltaTime);
            }
        }
    }
}
