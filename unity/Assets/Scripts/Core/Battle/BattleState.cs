using System.Collections.Generic;
using OpenXcom.Core.Common;
using OpenXcom.Core.Rules;

namespace OpenXcom.Core.Battle
{
    public enum MoveOutcome
    {
        Failed,
        Partial,
        Full,
    }

    public sealed class MoveResult
    {
        public MoveOutcome Outcome;
        public IReadOnlyList<Position> Path;
    }

    public enum FireOutcome
    {
        NoLineOfSight,
        InsufficientTu,
        Fired,
    }

    public sealed class FireResult
    {
        public FireOutcome Outcome;
        public ShotResult Shot;
    }

    /// <summary>
    /// The battle's mutable runtime container: the TileGrid, all units, whose
    /// turn it is, and the outbound BattleEvent queue. Mirrors OXCE's
    /// SavedBattleGame at slice-3 scope (no turn/win-loss flow yet — that's
    /// Phase 5).
    /// </summary>
    public sealed class BattleState
    {
        public TileGrid Grid { get; }
        public List<BattleUnit> Units { get; } = new();
        public Faction CurrentTurn { get; set; } = Faction.Player;
        public Rng Rng { get; }

        /// <summary>
        /// Flat LOFTEMPS.DAT voxel bitmask table (DataLoader.LoadLoftemps),
        /// used by TileEngine.CalculateLine/VoxelCheck for shot resolution.
        /// Empty by default; a scene bootstrap must set this before firing
        /// produces meaningful hits - VoxelCheck treats any loft index
        /// outside an empty/undersized table as passable, so an unset table
        /// degrades to "every shot misses" rather than crashing.
        /// </summary>
        public ushort[] LoftData { get; set; } = System.Array.Empty<ushort>();

        private readonly Queue<BattleEvent> _events = new();

        public BattleState(TileGrid grid, Rng rng = null)
        {
            Grid = grid;
            Rng = rng ?? new Rng(1);
        }

        public void Enqueue(BattleEvent evt) => _events.Enqueue(evt);

        public IReadOnlyList<BattleEvent> DequeueEvents()
        {
            var drained = new List<BattleEvent>(_events.Count);
            while (_events.Count > 0)
                drained.Add(_events.Dequeue());
            return drained;
        }

        /// <summary>
        /// Spawns units at the mapblock's route nodes, in node order, one
        /// unit per node up to whichever list is shorter. [SIMPLIFIED] no
        /// rank/type filtering — real deployment-based spawn rules need
        /// .rul ruleset data not parsed this phase.
        /// </summary>
        public static BattleState SpawnAtRouteNodes(
            TileGrid grid,
            IReadOnlyList<DataLoader.RawRouteNode> routeNodes,
            IReadOnlyList<BattleUnit> squad,
            Rng rng = null)
        {
            var state = new BattleState(grid, rng);
            int count = System.Math.Min(routeNodes.Count, squad.Count);

            for (int i = 0; i < count; i++)
            {
                var node = routeNodes[i];
                var unit = squad[i];
                var pos = new Position(node.X, node.Y, node.Z);

                unit.Position = pos;
                state.Grid.At(pos.X, pos.Y, pos.Z).Occupant = unit;
                state.Units.Add(unit);
            }

            return state;
        }

        /// <summary>
        /// Adds `squad` to this battle, each unit placed at a distinct,
        /// unoccupied node picked at random from `candidateNodes` (via this
        /// BattleState's own Rng). Callable multiple times on the same
        /// state - unlike the static SpawnAtRouteNodes factory above, which
        /// always builds a fresh one - so separate factions can each spawn
        /// at their own filtered node subset. [SIMPLIFIED] no rank/type
        /// filtering, same documented gap as SpawnAtRouteNodes: real
        /// deployment-based spawn rules need .rul data not converted this
        /// phase (design spec §6).
        /// </summary>
        public void SpawnSquad(IReadOnlyList<DataLoader.RawRouteNode> candidateNodes, IReadOnlyList<BattleUnit> squad)
        {
            var remaining = new List<DataLoader.RawRouteNode>(candidateNodes);

            foreach (var unit in squad)
            {
                Position? chosen = null;
                while (remaining.Count > 0)
                {
                    int i = Rng.Generate(0, remaining.Count - 1);
                    var node = remaining[i];
                    remaining.RemoveAt(i);

                    var pos = new Position(node.X, node.Y, node.Z);
                    var tile = Grid[pos];
                    if (tile != null && tile.Occupant == null)
                    {
                        chosen = pos;
                        break;
                    }
                }

                if (chosen == null) continue; // ran out of free candidate nodes

                unit.Position = chosen.Value;
                Grid[chosen.Value].Occupant = unit;
                Units.Add(unit);
            }
        }

        /// <summary>
        /// Finds a path from the unit's current position to target and walks
        /// as far as its TU budget allows, spending TU per step, updating
        /// occupancy, and enqueueing one UnitMovedEvent covering the tiles
        /// actually walked (never enqueued for a no-op or failed move).
        /// </summary>
        public MoveResult TryMove(BattleUnit unit, Position target)
        {
            if (unit.Position == target)
                return new MoveResult { Outcome = MoveOutcome.Full, Path = System.Array.Empty<Position>() };

            var fullPath = Pathfinding.FindPath(Grid, unit.Position, target);
            if (fullPath == null || fullPath.Count == 0)
                return new MoveResult { Outcome = MoveOutcome.Failed, Path = System.Array.Empty<Position>() };

            var walked = new List<Position>();
            var previous = unit.Position;
            var startPosition = previous;

            foreach (var step in fullPath)
            {
                if (!unit.CanSpend(step.StepCost))
                    break;

                unit.Spend(step.StepCost);
                Grid[previous].Occupant = null;
                // Every Pathfinding step is exactly one of Directions.Offsets
                // (Pathfinding.cs:92: neighbor = current + Directions.Offsets[dir]),
                // so IndexOf here is never -1.
                unit.Direction = Directions.IndexOf(step.Position - previous);
                unit.Position = step.Position;
                Grid[step.Position].Occupant = unit;
                walked.Add(step.Position);
                previous = step.Position;
            }

            if (walked.Count == 0)
                return new MoveResult { Outcome = MoveOutcome.Failed, Path = System.Array.Empty<Position>() };

            Enqueue(new UnitMovedEvent(unit, startPosition, walked));
            var outcome = walked.Count == fullPath.Count ? MoveOutcome.Full : MoveOutcome.Partial;
            return new MoveResult { Outcome = outcome, Path = walked };
        }

        /// <summary>
        /// Attempts to fire `weapon` from `attacker` at `defender`. Gated by
        /// line of sight (TileEngine.ComputeVisibleTiles) and TU budget, in
        /// that order. Once both gates pass, TU is spent immediately and the
        /// shot's accuracy is converted into a deviated aim voxel
        /// (Combat.ApplyDeviation), then extended out to max range along
        /// that same direction (Combat.ExtendAimVoxel) before being traced
        /// through real geometry (TileEngine.CalculateLine) - the trace's
        /// actual hit (terrain, `defender`, a different unit caught in the
        /// deviated path, or something behind `defender` on a miss) is what
        /// takes damage, not necessarily `defender` itself.
        /// A kill clears the hit unit's tile occupancy synchronously (no
        /// death-animation state machine this phase). Always enqueues one
        /// ProjectileFiredEvent when a shot is actually fired (hit or miss),
        /// carrying the traced voxel path, plus UnitHitEvent/UnitDiedEvent
        /// for whichever unit was actually hit.
        /// </summary>
        public FireResult TryFire(BattleUnit attacker, BattleItem weapon, BattleActionType action, BattleUnit defender)
        {
            var visibleTiles = TileEngine.ComputeVisibleTiles(Grid, attacker.Position);
            if (!visibleTiles.Contains(defender.Position))
                return new FireResult { Outcome = FireOutcome.NoLineOfSight, Shot = ShotResult.Miss };

            int tuCost = attacker.FireTuCost(action, weapon);
            if (!attacker.CanSpend(tuCost))
                return new FireResult { Outcome = FireOutcome.InsufficientTu, Shot = ShotResult.Miss };

            attacker.Spend(tuCost);

            // Face the defender before resolving the shot (parent design spec
            // §2) - reuses Phase 8's own nearest-of-8 helper rather than a new
            // one, since attacker-to-defender deltas are rarely an exact
            // Directions.Offsets match the way adjacent movement steps are.
            attacker.Direction = TileEngine.GetDirectionTo(attacker.Position, defender.Position);

            int accuracy = Combat.HitChance(attacker, weapon, action, defender.Position);
            var originVoxel = TileEngine.GetOriginVoxel(Grid, attacker, defender.Position);
            var targetVoxel = new Position(
                defender.Position.X * 16 + 8,
                defender.Position.Y * 16 + 8,
                defender.Position.Z * 24 + defender.Height / 2);
            var aimVoxel = Combat.ApplyDeviation(Rng, originVoxel, targetVoxel, accuracy);
            var extendedAimVoxel = Combat.ExtendAimVoxel(originVoxel, aimVoxel);

            var trace = TileEngine.CalculateLine(Grid, LoftData, originVoxel, extendedAimVoxel, attacker);
            var trajectory = new List<Position> { originVoxel, trace.Voxel };

            var hitUnit = trace.Type == VoxelType.Unit ? trace.Unit : null;
            var shot = hitUnit != null ? Combat.ApplyDamage(Rng, attacker, weapon.Rules, hitUnit) : ShotResult.Miss;

            Enqueue(new ProjectileFiredEvent(attacker, defender, shot.Hit, trajectory, weapon.Rules));

            if (shot.Hit)
            {
                Enqueue(new UnitHitEvent(hitUnit, shot.AppliedDamage, shot.Side));

                if (shot.Killed)
                {
                    Grid[hitUnit.Position].Occupant = null;
                    Enqueue(new UnitDiedEvent(hitUnit));
                }
            }

            return new FireResult { Outcome = FireOutcome.Fired, Shot = shot };
        }

        /// <summary>
        /// True when either faction has no living units left. Simplified
        /// port of BattlescapeGame::tallyUnits's core rule
        /// (src/Battlescape/BattlescapeGame.cpp:3361, "liveAliens == 0 ||
        /// liveSoldiers == 0"), ignoring the VIP-escort/must-destroy
        /// objective exceptions (not applicable to a plain skirmish). Checks
        /// each unit's IsAlive - dead units stay in the Units list, so list
        /// membership/count alone would be wrong here. The Units.Count > 0
        /// guard exists because List.Exists on an empty list returns false
        /// for any predicate, so !Exists(...) || !Exists(...) would
        /// otherwise be vacuously true with zero units - a battle that
        /// hasn't started (no units spawned yet) is not "over".
        /// </summary>
        public bool IsBattleOver =>
            Units.Count > 0 && (
                !Units.Exists(u => u.Faction == Faction.Player && u.IsAlive) ||
                !Units.Exists(u => u.Faction == Faction.Hostile && u.IsAlive));

        /// <summary>
        /// Switches CurrentTurn (Player&lt;-&gt;Hostile - [SIMPLIFIED] 2-way
        /// cycle, no Neutral/civilian phase since this slice has no
        /// civilian units), refreshes TU/energy for every living unit of
        /// the NEW current faction only (port of SavedBattleGame::endTurn's
        /// per-faction refresh, src/Battlescape/SavedBattleGame.cpp:1599-1602),
        /// enqueues one TurnChangedEvent, then checks IsBattleOver exactly
        /// once (matching the real engine's end-of-turn-only tally timing,
        /// BattlescapeGame.cpp:652) and enqueues one BattleOverEvent if the
        /// battle just ended.
        /// </summary>
        public void EndTurn()
        {
            CurrentTurn = CurrentTurn == Faction.Player ? Faction.Hostile : Faction.Player;

            foreach (var unit in Units)
                if (unit.Faction == CurrentTurn && unit.IsAlive)
                    unit.RefreshForNewTurn();

            Enqueue(new TurnChangedEvent(CurrentTurn));

            if (IsBattleOver)
            {
                bool playerAlive = Units.Exists(u => u.Faction == Faction.Player && u.IsAlive);
                bool hostileAlive = Units.Exists(u => u.Faction == Faction.Hostile && u.IsAlive);
                var outcome = playerAlive && !hostileAlive ? BattleOutcome.PlayerVictory
                    : !playerAlive && hostileAlive ? BattleOutcome.HostileVictory
                    : BattleOutcome.Draw;
                Enqueue(new BattleOverEvent(outcome));
            }
        }

        /// <summary>
        /// Ends the player's turn: switches to Hostile, runs the full
        /// simplified AI turn for every living hostile unit, then switches
        /// back to Player - unless the battle already ended when switching
        /// to Hostile, in which case the AI turn and the second switch are
        /// both skipped.
        /// </summary>
        public void EndPlayerTurn()
        {
            EndTurn(); // Player -> Hostile
            if (IsBattleOver)
                return;

            AiModule.RunHostileTurn(this);

            EndTurn(); // Hostile -> Player
        }
    }
}
