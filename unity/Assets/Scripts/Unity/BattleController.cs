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
    ///
    /// Walk-phase stepping and the firing-pose/death animations deliberately
    /// use two different mechanisms. Walk-phase must stay locked to actual
    /// tile arrival (a gameplay-visual sync requirement - the frame must
    /// change exactly when the unit visually reaches each tile, not on a
    /// fixed timer that could drift out of sync with tilesPerSecond), so it's
    /// driven by the existing per-tile Queue.Dequeue() point in
    /// AdvanceAnimations. Firing-pose-hold and the death sequence are both
    /// genuinely time-boxed ("show this pose/sequence for N seconds then
    /// revert/finish") with no positional trigger, so they share one small
    /// timed-sequence mechanism (see TimedSequence/AdvanceTimedSequences)
    /// instead of a third bespoke timer.
    /// </summary>
    public sealed class BattleController : MonoBehaviour
    {
        [SerializeField] private Camera raycastCamera;
        [SerializeField] private float tilesPerSecond = 4f;
        [SerializeField] private ProjectileView projectileView;

        private const float FiringPoseSeconds = 0.3f;
        private const float DeathSequenceSeconds = 0.6f;

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
            public UnitRenderer Renderer;
            public Queue<Position> Queue;
            public Vector3 SegmentStart;
            public Vector3 Target;
            public Position Previous;
            public int Direction;
        }

        private readonly List<UnitAnimation> _activeAnimations = new();

        /// <summary>Shared timer for the two genuinely duration-based
        /// animations (firing-pose hold, death sequence) - unlike walk-phase
        /// stepping (position-driven, see the class doc comment), both of
        /// these are "hold/step for N seconds then settle," so they share one
        /// mechanism instead of two near-identical ad hoc timers.</summary>
        private sealed class TimedSequence
        {
            public UnitRenderer Renderer;
            public BattleUnit Unit; // set for death sequences (unused after StartDeathSequence already removed it from _unitTransforms - kept for clarity/future use)
            public float Elapsed;
            public float Duration;
            public int FrameCount;
            public bool IsDeath;
            public int Direction; // firing-pose revert direction
        }

        private readonly List<TimedSequence> _timedSequences = new();

        private void StartFiringPose(Transform shooterTransform, int direction)
        {
            if (!shooterTransform.TryGetComponent<UnitRenderer>(out var renderer))
                return;

            renderer.SetFrame(direction, walkPhase: -1, isAiming: true);
            _timedSequences.Add(new TimedSequence
            {
                Renderer = renderer, Elapsed = 0f, Duration = FiringPoseSeconds, FrameCount = 1, IsDeath = false, Direction = direction,
            });
        }

        private void StartDeathSequence(BattleUnit unit, Transform deadTransform)
        {
            // Corpses stay on the tile as a visible, inert body (matching the
            // original game - a dead unit becomes a corpse item, not a puff of
            // smoke) rather than vanishing. Only the collider is disabled, so
            // clicks/hover pass through to the tile beneath instead of hitting
            // an invisible-but-still-clickable dead unit.
            if (deadTransform.TryGetComponent<BoxCollider>(out var collider))
                collider.enabled = false;
            _unitTransforms.Remove(unit);
            if (_selected == unit)
                _selected = null;

            if (!deadTransform.TryGetComponent<UnitRenderer>(out var renderer))
                return;

            renderer.SetDeathFrame(0);
            _timedSequences.Add(new TimedSequence
            {
                Renderer = renderer, Unit = unit,
                Elapsed = 0f, Duration = DeathSequenceSeconds, FrameCount = OpenXcom.Unity.Rendering.UnitSpriteFrames.DeathFrameCount, IsDeath = true,
            });
        }

        private void AdvanceTimedSequences()
        {
            for (int i = _timedSequences.Count - 1; i >= 0; i--)
            {
                var seq = _timedSequences[i];
                seq.Elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(seq.Elapsed / seq.Duration);

                if (seq.IsDeath)
                {
                    int phase = Mathf.Min(seq.FrameCount - 1, (int)(t * seq.FrameCount));
                    seq.Renderer.SetDeathFrame(phase);
                }

                if (t < 1f)
                    continue;

                // Death sequences need no completion action beyond the final
                // SetDeathFrame call above - StartDeathSequence already
                // removed the unit from _unitTransforms/_selected up front,
                // and the corpse's GameObject stays active (see
                // StartDeathSequence's doc comment).
                if (!seq.IsDeath)
                    seq.Renderer.SetFrame(seq.Direction, walkPhase: -1, isAiming: false);
                _timedSequences.RemoveAt(i);
            }
        }

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
            AdvanceTimedSequences();

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

        /// <summary>True if transform currently has a pending/in-progress walk
        /// animation - used to skip starting a firing pose for a unit that's
        /// still mid-walk in the same drain batch (see the ProjectileFiredEvent
        /// branch in DrainAndAnimate).</summary>
        private bool IsAnimating(Transform t) => _activeAnimations.Exists(a => a.Transform == t);

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
                    t.TryGetComponent<UnitRenderer>(out var renderer);
                    _activeAnimations.Add(new UnitAnimation
                    {
                        Transform = t,
                        Renderer = renderer,
                        Queue = new Queue<Position>(moved.Path),
                        SegmentStart = t.localPosition,
                        Target = t.localPosition,
                        Previous = moved.From,
                        Direction = moved.Unit.Direction,
                    });
                }
                else if (evt is ProjectileFiredEvent fired)
                {
                    Debug.Log(fired.Hit
                        ? $"{fired.Attacker.Name} hits {fired.Defender.Name}"
                        : $"{fired.Attacker.Name} misses {fired.Defender.Name}");

                    if (projectileView != null && fired.Trajectory.Count >= 2)
                        projectileView.Play(fired.Trajectory[0], fired.Trajectory[^1], fired.Weapon.BulletSprite);

                    // Skip the firing pose entirely for a unit that still has
                    // a pending walk animation in this same drain batch (the
                    // common AI case: approach then shoot in one turn, all
                    // events drained together) - AdvanceAnimations would just
                    // overwrite the aim pose with a walk frame on the very
                    // next tick, and the pose's timed revert could otherwise
                    // snap the unit to a standing frame mid-slide.
                    if (_unitTransforms.TryGetValue(fired.Attacker, out var shooterTransform)
                        && !IsAnimating(shooterTransform))
                        StartFiringPose(shooterTransform, fired.Attacker.Direction);
                }
                else if (evt is UnitHitEvent hitEvent)
                {
                    Debug.Log($"{hitEvent.Unit.Name} takes {hitEvent.Damage} damage ({hitEvent.Side})");
                }
                else if (evt is UnitDiedEvent died && _unitTransforms.TryGetValue(died.Unit, out var deadTransform))
                {
                    StartDeathSequence(died.Unit, deadTransform);
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
                    anim.Renderer?.SetFrame(anim.Direction, walkPhase: -1, isAiming: false);
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
                    anim.SegmentStart = anim.Transform.localPosition;
                    anim.Target = new Vector3(worldX, worldY, depth);
                    anim.Direction = Directions.IndexOf(next - anim.Previous);
                    anim.Previous = next;
                }

                anim.Transform.localPosition = Vector3.MoveTowards(
                    anim.Transform.localPosition, anim.Target, tilesPerSecond * Time.deltaTime);

                // Walk phase is driven by how far across the CURRENT tile
                // segment the unit has actually slid, not by wall-clock time
                // or a once-per-tile jump - this is what keeps the walk cycle
                // visually synced with movement regardless of tilesPerSecond's
                // value (a once-per-dequeue phase bump left the sprite frozen
                // on one frame for the whole tile crossing at low speeds).
                float segmentLength = Vector3.Distance(anim.SegmentStart, anim.Target);
                float remaining = Vector3.Distance(anim.Transform.localPosition, anim.Target);
                float progress = segmentLength > 0.0001f ? 1f - remaining / segmentLength : 1f;
                int walkPhase = Mathf.Min(7, (int)(progress * 8f));
                anim.Renderer?.SetFrame(anim.Direction, walkPhase, isAiming: false);
            }
        }
    }
}
