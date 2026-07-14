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

        /// <summary>Ends the player's turn. Same effect as pressing Backspace
        /// (see Update) - exposed so the HUD's End Turn button (Task 6) can
        /// call it from a UnityEvent, which requires a public no-arg method.</summary>
        public void EndTurnFromHud()
        {
            if (_state == null || _state.IsBattleOver)
                return;
            _state.EndPlayerTurn();
            DrainAndAnimate();
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
                // Only the player's own units are selectable - nothing in
                // TryMove/TryFire itself checks faction or whose turn it is, so
                // without this gate a left-click on a visible Hostile unit hands
                // the player full move/fire control over it.
                if (HoveredUnit.Faction == Faction.Player)
                    _selected = HoveredUnit;
                return;
            }

            if (_selected == null || HoveredTile == null || _state.CurrentTurn != Faction.Player)
                return;

            var result = _state.TryMove(_selected, HoveredTile.Value);
            if (result.Outcome == MoveOutcome.Failed)
            {
                Debug.Log($"{_selected.Name} can't move there (out of TU or no path)");
                return;
            }

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

            if (UnityEngine.EventSystems.EventSystem.current != null
                && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
                return; // mouse is over a uGUI element (the icon bar) - not the 3D map

            var ray = raycastCamera.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out var hit))
                return;

            HoveredUnit = FindUnitAt(hit.transform);
            // HoveredTile always resolves - even when the hit was a unit's
            // collider, not a "Tile_x_y_z" one - since the tile cursor must show
            // on an occupied tile too (a unit's own Position IS its tile).
            // Previously HoveredTile stayed null whenever HoveredUnit was set,
            // which made the tile cursor vanish entirely while hovering a unit.
            HoveredTile = HoveredUnit != null ? HoveredUnit.Position : TileUnderCursor(hit);
        }

        private void HandleFireClick()
        {
            if (_selected == null || _selected.RightHand == null || _state.CurrentTurn != Faction.Player)
                return;

            if (HoveredUnit == null || HoveredUnit == _selected)
                return;

            var result = _state.TryFire(_selected, _selected.RightHand, BattleActionType.AimedShot, HoveredUnit);
            if (result.Outcome != FireOutcome.Fired)
            {
                // Previously silent: an aimed shot costs a large chunk of TU
                // (TuAimed% of the unit's TimeUnits stat) and there's no
                // ammo/reload modeled yet, so InsufficientTu is common and,
                // without this, looked identical to "the game stopped
                // responding to right-click" rather than "out of TU."
                Debug.Log($"{_selected.Name} can't fire: {result.Outcome}");
                return;
            }

            DrainAndAnimate();
        }

        private BattleUnit FindUnitAt(Transform hitTransform)
        {
            foreach (var kv in _unitTransforms)
                if (kv.Value == hitTransform)
                    return kv.Key;
            return null;
        }

        private Position? TileUnderCursor(RaycastHit hit)
        {
            // Inverse of IsoProjection.MapToScreen; left as a direct pixel/world
            // lookup against the hit tile's own TileRenderer name ("Tile_x_y_z").
            // Reads hit.transform directly, NOT .parent: TileRenderer's
            // BoxCollider sits on the Tile_x_y_z GameObject itself. Returns null
            // (not a throw) for any other collider name - observed live when a
            // raycast hit something that wasn't a registered unit or a
            // "Tile_x_y_z"-named collider, which crashed Update() every frame.
            var parts = hit.transform.name.Split('_');
            if (parts.Length != 4 || parts[0] != "Tile"
                || !int.TryParse(parts[1], out int x)
                || !int.TryParse(parts[2], out int y)
                || !int.TryParse(parts[3], out int z))
                return null;
            return new Position(x, y, z);
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
                    var (worldX, worldY) = IsoProjection.WorldPosition(next.X, next.Y, next.Z, TileRenderer.PixelsPerUnit);
                    // Z must track UnitRaycastDepth, not a flat 0 - every tile's
                    // MeshCollider/BoxCollider sits at a small negative
                    // (closer-to-camera) Z via IsoProjection.RaycastDepth, and a
                    // moving unit needs to stay closer than the tile beneath it
                    // (its own UnitRaycastDepth) or the raycast that resolves
                    // clicks/hover starts hitting the tile instead of the unit
                    // standing on it once it settles at Z=0. This made any unit
                    // that had ever moved - including every Hostile unit after
                    // its first AI turn, regardless of what the player did -
                    // permanently unable to be right-click-targeted.
                    float depth = IsoProjection.UnitRaycastDepth(next.X, next.Y, next.Z, _state.Grid.Width, _state.Grid.Length);
                    anim.Target = new Vector3(worldX, worldY, depth);
                }

                anim.Transform.localPosition = Vector3.MoveTowards(
                    anim.Transform.localPosition, anim.Target, tilesPerSecond * Time.deltaTime);
            }
        }
    }
}
