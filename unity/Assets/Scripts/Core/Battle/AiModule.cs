using OpenXcom.Core.Common;
using OpenXcom.Core.Rules;

namespace OpenXcom.Core.Battle
{
    /// <summary>
    /// Minimal hostile-unit AI. Simplified port of AIModule::think's
    /// reducible core (src/Battlescape/AIModule.cpp:422): spot the nearest
    /// visible enemy via the same TileEngine visibility the player uses,
    /// fire if possible, otherwise approach. [SIMPLIFIED] no patrol/ambush/
    /// escape behaviors, no melee, no per-unit action-count cap.
    /// </summary>
    public static class AiModule
    {
        /// <summary>
        /// Runs one decision cycle for one hostile unit: fire at the
        /// nearest visible enemy if possible, else move one step closer.
        /// Returns true if the unit made real progress (fired, or actually
        /// moved) - callers repeat this for the same unit until it returns
        /// false, then move on to the next unit (RunHostileTurn).
        /// </summary>
        public static bool TakeTurn(BattleState state, BattleUnit unit)
        {
            if (!unit.IsAlive)
                return false;

            var target = FindNearestVisibleEnemy(state, unit);
            if (target == null)
                return false;

            var weapon = unit.WeaponFor(BattleActionType.AimedShot);
            if (weapon != null)
            {
                var fireResult = state.TryFire(unit, weapon, BattleActionType.AimedShot, target);
                if (fireResult.Outcome == FireOutcome.Fired)
                    return true;
            }

            // Couldn't fire (no weapon, or insufficient TU) - approach
            // instead. Pathfinding always treats the target's own tile as
            // occupied/blocked, so we path to one of its orthogonal
            // neighbors, not the target's exact position.
            var approachTile = FindApproachTile(state.Grid, target.Position);
            if (approachTile == null)
                return false; // target has no free orthogonal neighbor - nothing to do

            var moveResult = state.TryMove(unit, approachTile.Value);
            return moveResult.Path.Count > 0;
        }

        /// <summary>
        /// Runs every living hostile unit's turn: repeats TakeTurn for one
        /// unit until it makes no more progress, then advances to the next
        /// living hostile unit, in Units list order. [SIMPLIFIED] no
        /// per-unit action-count cap (the real engine caps at 2 AI actions
        /// per think() call). Termination is guaranteed: every
        /// progress-reporting call spends TU > 0 (firing always costs a
        /// nonzero percentage of max TU; a real move step costs at least
        /// Pathfinding.DefaultMoveCost), and TU only decreases.
        /// </summary>
        public static void RunHostileTurn(BattleState state)
        {
            foreach (var unit in state.Units)
            {
                if (!unit.IsAlive || unit.Faction != Faction.Hostile)
                    continue;

                while (TakeTurn(state, unit)) { }
            }
        }

        private static BattleUnit FindNearestVisibleEnemy(BattleState state, BattleUnit unit)
        {
            var visibleTiles = TileEngine.ComputeVisibleTiles(state.Grid, unit.Position);
            BattleUnit nearest = null;
            double nearestDistance = double.MaxValue;

            foreach (var other in state.Units)
            {
                if (!other.IsAlive || other.Faction == unit.Faction)
                    continue;
                if (!visibleTiles.Contains(other.Position))
                    continue;

                double distance = unit.Position.Distance(other.Position);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = other;
                }
            }

            return nearest;
        }

        /// <summary>
        /// Finds the first free (unoccupied, walkable) orthogonal neighbor
        /// of targetPos, checking N/E/S/W in that order
        /// (Directions.Offsets indices 0,2,4,6). Excludes ANY occupied
        /// tile, including the calling unit's own current tile (it is
        /// always that tile's occupant while standing there) - this is
        /// what guarantees TryMove is never called with the acting unit's
        /// own position as the destination.
        /// </summary>
        private static Position? FindApproachTile(TileGrid grid, Position targetPos)
        {
            int[] orthogonalDirections = { 0, 2, 4, 6 }; // N, E, S, W
            foreach (int dir in orthogonalDirections)
            {
                var candidate = targetPos + Directions.Offsets[dir];
                var tile = grid[candidate];
                if (tile != null && tile.Occupant == null && tile.Floor != null && !tile.Floor.NoFloor)
                    return candidate;
            }
            return null;
        }
    }
}
