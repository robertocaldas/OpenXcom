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
    /// OpenXcom.Core.Battle.BattleState.TryMove — this class only translates
    /// mouse clicks into calls on it and drains/animates the resulting
    /// BattleEvents. Makes no gameplay decisions of its own.
    ///
    /// NOTE: this class cannot be compiled or run in the environment this
    /// was written in (no Unity Editor / UnityEngine assemblies available
    /// this session). It follows documented Unity API behavior but has not
    /// been visually verified — check it in the Editor before relying on it.
    /// </summary>
    public sealed class BattleController : MonoBehaviour
    {
        [SerializeField] private Camera raycastCamera;
        [SerializeField] private float tilesPerSecond = 4f;

        private BattleState _state;
        private BattleUnit _selected;
        private readonly Dictionary<BattleUnit, Transform> _unitTransforms = new();
        private Queue<Position> _animationQueue;
        private Transform _animatingTransform;
        private Vector3 _animationTarget;

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
            if (_animatingTransform != null)
            {
                AdvanceAnimation();
                return; // don't accept new input mid-animation
            }

            if (_state == null)
                return;

            if (Input.GetMouseButtonDown(1)) // right-click: fire at a targeted unit
            {
                HandleFireClick();
                return;
            }

            if (!Input.GetMouseButtonDown(0)) // left-click: select / move
                return;

            var ray = raycastCamera.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out var hit))
                return;

            var clickedUnit = FindUnitAt(hit.transform);
            if (clickedUnit != null)
            {
                _selected = clickedUnit;
                return;
            }

            if (_selected == null)
                return;

            var targetTile = TileUnderCursor(hit);
            var result = _state.TryMove(_selected, targetTile);
            if (result.Outcome == MoveOutcome.Failed)
                return;

            DrainAndAnimate();
        }

        private void HandleFireClick()
        {
            if (_selected == null || _selected.RightHand == null)
                return;

            var ray = raycastCamera.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out var hit))
                return;

            var target = FindUnitAt(hit.transform);
            if (target == null || target == _selected)
                return;

            _state.TryFire(_selected, _selected.RightHand, BattleActionType.AimedShot, target);
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
            // lookup against the hit tile's own TileRenderer name ("Tile_x_y_z"),
            // since BattlescapeMapView already names each tile GameObject that
            // way and this avoids re-deriving the iso inverse-projection math
            // for this phase (no input-picking formula was ported yet — parent
            // spec §5 names this as later work, "screen->tile picking").
            var parts = hit.transform.parent.name.Split('_');
            return new Position(int.Parse(parts[1]), int.Parse(parts[2]), int.Parse(parts[3]));
        }

        private void DrainAndAnimate()
        {
            foreach (var evt in _state.DequeueEvents())
            {
                if (evt is UnitMovedEvent moved && _unitTransforms.TryGetValue(moved.Unit, out var t))
                {
                    _animatingTransform = t;
                    _animationQueue = new Queue<Position>(moved.Path);
                    // Seed the target to the unit's current position so
                    // AdvanceAnimation's "close enough, dequeue next waypoint"
                    // gate is trivially satisfied on this first call, instead
                    // of comparing against whatever _animationTarget was left
                    // over from a previous, different unit's animation (or
                    // Vector3.zero on the very first move ever).
                    _animationTarget = t.localPosition;
                    AdvanceAnimation();
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
                }
            }
        }

        private void AdvanceAnimation()
        {
            if (_animationQueue.Count == 0 && Vector3.Distance(_animatingTransform.localPosition, _animationTarget) < 0.01f)
            {
                _animatingTransform = null;
                return;
            }

            if (Vector3.Distance(_animatingTransform.localPosition, _animationTarget) < 0.01f)
            {
                var next = _animationQueue.Dequeue();
                var (sx, sy) = IsoProjection.MapToScreen(next.X, next.Y, next.Z);
                _animationTarget = new Vector3(sx / TileRenderer.PixelsPerUnit, sy / TileRenderer.PixelsPerUnit, 0f);
            }

            _animatingTransform.localPosition = Vector3.MoveTowards(
                _animatingTransform.localPosition, _animationTarget, tilesPerSecond * Time.deltaTime);
        }
    }
}
