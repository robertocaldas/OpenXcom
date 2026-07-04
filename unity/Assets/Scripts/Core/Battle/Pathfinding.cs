using System.Collections.Generic;
using OpenXcom.Core.Common;

namespace OpenXcom.Core.Battle
{
    /// <summary>One waypoint of a found path, with the incremental TU cost to reach it.</summary>
    public sealed class PathStep
    {
        public Position Position;
        public int StepCost;
    }

    /// <summary>
    /// Grid A* pathfinding on a TU budget. Simplified port of
    /// Pathfinding::getTUCost (src/Battlescape/Pathfinding.cpp:257-600):
    /// single-tile units, one flat level (z is not traversed), no stairs/
    /// falling/flying, no diagonal wall-corner blocking (wall checks are
    /// orthogonal-direction only this phase).
    /// </summary>
    public static class Pathfinding
    {
        /// <summary>Fallback TU cost when a tile's own floor walk cost is 0 (Pathfinding.h:82).</summary>
        public const int DefaultMoveCost = 4;

        /// <summary>
        /// TU cost to move one step from `from` in compass direction `direction`
        /// (0..7, see Common.Directions), or null if that step is blocked.
        /// </summary>
        public static int? StepCost(TileGrid grid, Position from, int direction)
        {
            var offset = Directions.Offsets[direction];
            var to = from + offset;
            var toTile = grid[to];
            if (toTile == null) return null;
            if (toTile.Occupant != null) return null;
            if (toTile.Floor == null) return null;

            if (!Directions.IsDiagonal(direction))
            {
                var fromTile = grid[from];
                if (offset.X < 0 && fromTile.WestWall != null) return null;   // moving W: source's own west wall
                if (offset.Y < 0 && fromTile.NorthWall != null) return null;  // moving N: source's own north wall
                if (offset.X > 0 && toTile.WestWall != null) return null;     // moving E: dest's west wall
                if (offset.Y > 0 && toTile.NorthWall != null) return null;    // moving S: dest's north wall
            }

            int cost = toTile.Floor.TuWalk > 0 ? toTile.Floor.TuWalk : DefaultMoveCost;
            if (Directions.IsDiagonal(direction))
                cost = cost * 3 / 2;
            return cost;
        }

        /// <summary>
        /// A* search from start to goal. Returns the ordered waypoints from
        /// start (exclusive) to goal (inclusive) with each step's own TU
        /// cost, an empty list if start == goal, or null if unreachable.
        /// </summary>
        public static List<PathStep> FindPath(TileGrid grid, Position start, Position goal)
        {
            if (start == goal)
                return new List<PathStep>();

            var openSet = new List<Position> { start };
            var cameFrom = new Dictionary<Position, Position>();
            var gScore = new Dictionary<Position, int> { [start] = 0 };

            while (openSet.Count > 0)
            {
                Position current = openSet[0];
                int bestF = int.MaxValue;
                foreach (var candidate in openSet)
                {
                    int f = gScore[candidate] + candidate.ChebyshevDistance(goal);
                    if (f < bestF)
                    {
                        bestF = f;
                        current = candidate;
                    }
                }

                if (current == goal)
                    return ReconstructPath(cameFrom, gScore, current);

                openSet.Remove(current);

                for (int dir = 0; dir < 8; dir++)
                {
                    int? stepCost = StepCost(grid, current, dir);
                    if (stepCost == null)
                        continue;

                    var neighbor = current + Directions.Offsets[dir];
                    int tentativeG = gScore[current] + stepCost.Value;

                    if (!gScore.TryGetValue(neighbor, out int existingG) || tentativeG < existingG)
                    {
                        cameFrom[neighbor] = current;
                        gScore[neighbor] = tentativeG;
                        if (!openSet.Contains(neighbor))
                            openSet.Add(neighbor);
                    }
                }
            }

            return null;
        }

        private static List<PathStep> ReconstructPath(
            Dictionary<Position, Position> cameFrom,
            Dictionary<Position, int> gScore,
            Position current)
        {
            var reversed = new List<PathStep>();
            while (cameFrom.TryGetValue(current, out var prev))
            {
                reversed.Add(new PathStep { Position = current, StepCost = gScore[current] - gScore[prev] });
                current = prev;
            }
            reversed.Reverse();
            return reversed;
        }
    }
}
