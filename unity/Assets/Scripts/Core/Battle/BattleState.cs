using System.Collections.Generic;
using OpenXcom.Core.Common;
using OpenXcom.Core.Rules;

namespace OpenXcom.Core.Battle
{
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
    }
}
