using System.Collections.Generic;
using OpenXcom.Core.Common;
using OpenXcom.Core.Rules;

namespace OpenXcom.Core.Battle
{
    /// <summary>One waypoint of a found path, with the incremental TU cost to reach it.</summary>
    public sealed class PathStep
    {
        public Position Position;
        public int StepCost;
    }

    /// <summary>
    /// Grid A* pathfinding on a TU budget. Port of Pathfinding::getTUCost
    /// (src/Battlescape/Pathfinding.cpp:257-618) scoped to this rewrite's model:
    /// single-tile units, one flat level (z is not traversed - no stairs,
    /// falling, flying, gravlift or ladders), walk movement only, and no
    /// missile pathing. Within that scope the movement-blocking gate is faithful:
    /// solid floors/objects (trees, blocking scenery), walls, and directional
    /// "big walls" (fences, diagonal walls in the object slot) all block, via the
    /// same isBlocked/isBlockedDirection edge logic the original uses, and the
    /// step cost sums floor + object + averaged wall TU cost the same way.
    /// </summary>
    public static class Pathfinding
    {
        /// <summary>Fallback TU cost when a step's summed walk cost is 0 (Pathfinding.h:82).</summary>
        public const int DefaultMoveCost = 4;

        /// <summary>A part TU cost of 255 means "impassable" (Pathfinding.h INVALID_MOVE_COST).</summary>
        private const int InvalidMoveCost = 255;

        // Big-wall categories an object part can carry (Pathfinding.h:105-193).
        // Values 1-3 block the whole tile from every direction; 4-9 block only
        // specific edges (and carry no floor-walk cost - see PartTuCost).
        private const int BwBlock = 1;        // solid block (all edges)
        private const int BwNesw = 2;         // NE-SW diagonal
        private const int BwNwse = 3;         // NW-SE diagonal
        private const int BwWest = 4;         // west edge
        private const int BwNorth = 5;        // north edge
        private const int BwEast = 6;         // east edge
        private const int BwSouth = 7;        // south edge
        private const int BwEastSouth = 8;    // east + south edges
        private const int BwWestNorth = 9;    // west + north edges

        /// <summary>Which tile part a block check applies to. BigWall is the pseudo-part
        /// (O_BIGWALL = -1 in the original) covering full-tile object walls.</summary>
        private enum Part { Floor, WestWall, NorthWall, Object, BigWall }

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
            if (toTile.Floor == null) return null; // nothing to stand on (single-level: no falling)

            // Movement-validity gate (Pathfinding.cpp:316,394,437): the destination
            // tile's floor (incl. its occupant) and object must both be passable,
            // and the wall/big-wall edges crossed by this direction must be open.
            if (IsBlocked(grid, to, Part.Floor)) return null;
            if (IsBlocked(grid, to, Part.Object)) return null;
            if (IsBlockedDirection(grid, from, direction)) return null;

            var fromTile = grid[from];

            // Wall TU cost, averaged over the walls this direction crosses
            // (Pathfinding.cpp:493-539). Blocking (255-cost) walls never reach
            // here - they were rejected by IsBlockedDirection above - so this only
            // charges for passable rubble/low walls.
            int wallCost = 0, wallCount = 0;
            void AddWall(MapDataTile wall)
            {
                int w = PartTuCost(wall, isObject: false);
                if (w > 0) { wallCost += w; wallCount++; }
            }
            if (direction == 0 || direction == 7 || direction == 1) AddWall(fromTile.NorthWall);
            if (direction == 2 || direction == 1 || direction == 3) AddWall(toTile.WestWall);
            if (direction == 4 || direction == 3 || direction == 5) AddWall(toTile.NorthWall);
            if (direction == 6 || direction == 5 || direction == 7) AddWall(fromTile.WestWall);
            if (wallCount > 0) wallCost /= wallCount;
            if (wallCost >= InvalidMoveCost) return null;

            // Floor + object walk cost (Pathfinding.cpp:553-565).
            int cost = PartTuCost(toTile.Floor, isObject: false);
            if (toTile.Object != null) cost += PartTuCost(toTile.Object, isObject: true);
            if (cost == 0) cost = DefaultMoveCost; // guard broken 0-cost tiles

            // Diagonal walking costs 50% more (Pathfinding.cpp:574-577), then the
            // wall cost is added on top (not scaled).
            if (Directions.IsDiagonal(direction))
                cost = (int)(cost * 1.5);
            cost += wallCost;

            return cost;
        }

        /// <summary>
        /// Port of Tile::getTUCost (src/Savegame/Tile.cpp:299-311) for walk
        /// movement: a null part is free, a directional big wall (>= 4) in the
        /// object slot carries no floor-walk cost, otherwise the part's own walk
        /// cost (255 = impassable).
        /// </summary>
        private static int PartTuCost(MapDataTile part, bool isObject)
        {
            if (part == null) return 0;
            if (isObject && part.BigWall >= BwWest) return 0;
            return part.TuWalk;
        }

        private static MapDataTile PartData(Tile tile, Part part) => part switch
        {
            Part.Floor => tile.Floor,
            Part.WestWall => tile.WestWall,
            Part.NorthWall => tile.NorthWall,
            Part.Object => tile.Object,
            _ => null,
        };

        /// <summary>
        /// Is movement blocked by a given part of the tile at `pos`? Port of
        /// Pathfinding::isBlocked (src/Battlescape/Pathfinding.cpp:825-929) scoped
        /// to walk movement, single Z-level, no missile target: the falling /
        /// no-floor and door branches don't apply here. A `bigWallExclusion`
        /// lets a diagonal move ignore a big wall whose orientation runs parallel
        /// to that diagonal (so you can slip past its corner).
        /// </summary>
        private static bool IsBlocked(TileGrid grid, Position pos, Part part, int bigWallExclusion = 0)
        {
            var tile = grid[pos];
            if (tile == null) return true; // outside the map

            if (part == Part.BigWall)
            {
                var obj = tile.Object;
                return obj != null && obj.BigWall != 0 && obj.BigWall <= BwNwse && obj.BigWall != bigWallExclusion;
            }
            if (part == Part.WestWall)
            {
                var obj = tile.Object;
                if (obj != null && (obj.BigWall == BwWest || obj.BigWall == BwWestNorth)) return true;
                var west = grid[pos + new Position(-1, 0, 0)];
                if (west == null) return true; // do not look outside the map
                if (west.Object != null && (west.Object.BigWall == BwEast || west.Object.BigWall == BwEastSouth)) return true;
            }
            if (part == Part.NorthWall)
            {
                var obj = tile.Object;
                if (obj != null && (obj.BigWall == BwNorth || obj.BigWall == BwWestNorth)) return true;
                var north = grid[pos + new Position(0, -1, 0)];
                if (north == null) return true; // do not look outside the map
                if (north.Object != null && (north.Object.BigWall == BwSouth || north.Object.BigWall == BwEastSouth)) return true;
            }
            if (part == Part.Floor)
            {
                var u = tile.Occupant;
                // Simplified vs. the original's faction/visibility rules: any
                // living unit standing here blocks the tile for a mover.
                if (u != null && u.IsAlive) return true;
            }

            return PartTuCost(PartData(tile, part), part == Part.Object) == InvalidMoveCost;
        }

        /// <summary>
        /// Does stepping from `pos` in `direction` cross a blocking wall or big
        /// wall? Port of Pathfinding::isBlockedDirection
        /// (src/Battlescape/Pathfinding.cpp:940-1003), walk / no-missile scope.
        /// </summary>
        private static bool IsBlockedDirection(TileGrid grid, Position pos, int direction)
        {
            var north = new Position(0, -1, 0);
            var east = new Position(1, 0, 0);
            var south = new Position(0, 1, 0);
            var west = new Position(-1, 0, 0);

            switch (direction)
            {
                case 0: // north
                    if (IsBlocked(grid, pos, Part.NorthWall)) return true;
                    break;
                case 1: // north-east
                    if (IsBlocked(grid, pos, Part.NorthWall)) return true;
                    if (IsBlocked(grid, pos + north + east, Part.WestWall)) return true;
                    if (IsBlocked(grid, pos + east, Part.WestWall)) return true;
                    if (IsBlocked(grid, pos + east, Part.NorthWall)) return true;
                    if (IsBlocked(grid, pos + east, Part.BigWall, BwNesw)) return true;
                    if (IsBlocked(grid, pos + north, Part.BigWall, BwNesw)) return true;
                    break;
                case 2: // east
                    if (IsBlocked(grid, pos + east, Part.WestWall)) return true;
                    break;
                case 3: // south-east
                    if (IsBlocked(grid, pos + east, Part.WestWall)) return true;
                    if (IsBlocked(grid, pos + south, Part.NorthWall)) return true;
                    if (IsBlocked(grid, pos + south + east, Part.NorthWall)) return true;
                    if (IsBlocked(grid, pos + south + east, Part.WestWall)) return true;
                    if (IsBlocked(grid, pos + east, Part.BigWall, BwNwse)) return true;
                    if (IsBlocked(grid, pos + south, Part.BigWall, BwNwse)) return true;
                    break;
                case 4: // south
                    if (IsBlocked(grid, pos + south, Part.NorthWall)) return true;
                    break;
                case 5: // south-west
                    if (IsBlocked(grid, pos, Part.WestWall)) return true;
                    if (IsBlocked(grid, pos + south, Part.WestWall)) return true;
                    if (IsBlocked(grid, pos + south, Part.NorthWall)) return true;
                    if (IsBlocked(grid, pos + south, Part.BigWall, BwNesw)) return true;
                    if (IsBlocked(grid, pos + west, Part.BigWall, BwNesw)) return true;
                    if (IsBlocked(grid, pos + south + west, Part.NorthWall)) return true;
                    break;
                case 6: // west
                    if (IsBlocked(grid, pos, Part.WestWall)) return true;
                    break;
                case 7: // north-west
                    if (IsBlocked(grid, pos, Part.WestWall)) return true;
                    if (IsBlocked(grid, pos, Part.NorthWall)) return true;
                    if (IsBlocked(grid, pos + west, Part.NorthWall)) return true;
                    if (IsBlocked(grid, pos + north, Part.WestWall)) return true;
                    if (IsBlocked(grid, pos + north, Part.BigWall, BwNwse)) return true;
                    if (IsBlocked(grid, pos + west, Part.BigWall, BwNwse)) return true;
                    break;
            }

            return false;
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
