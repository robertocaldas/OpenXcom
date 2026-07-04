using System;
using System.Collections.Generic;
using OpenXcom.Core.Common;

namespace OpenXcom.Core.Battle
{
    /// <summary>
    /// Simplified line-of-sight / fog-of-war. Ports the tile-space walk idea
    /// from TileEngine::calculateLineTile (src/Battlescape/TileEngine.cpp:4310-4347)
    /// using a generic integer Bresenham line (not X-COM-specific), checking
    /// each intervening tile's own BlocksSight flag rather than the real
    /// engine's direction-dependent per-edge wall cache (a deliberate
    /// simplification - LOS and movement are different concerns and don't
    /// need matching granularity). [SIMPLIFIED] single level (z held
    /// constant), no voxel/darkness/view-cone modeling.
    /// </summary>
    public static class TileEngine
    {
        /// <summary>Vision range cap in tiles. Ports Mod's default maxViewDistance (src/Mod/Mod.cpp:424).</summary>
        public const int MaxViewDistance = 20;

        /// <summary>
        /// True if a straight line from `from` to `to` (same z-level) is
        /// unobstructed. You can always see your own tile and the target
        /// tile itself, even if the target tile has a blocking wall/object -
        /// only tiles strictly between the two can block the line.
        /// </summary>
        public static bool HasLineOfSight(TileGrid grid, Position from, Position to)
        {
            foreach (var pos in WalkLine(from, to))
            {
                if (pos == from || pos == to)
                    continue;

                var tile = grid[pos];
                if (tile == null || tile.BlocksSight)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// All in-bounds tiles within MaxViewDistance of `from` that have a
        /// clear line of sight. Each returned tile's Discovered flag is set
        /// permanently (fog-of-war reveal); the returned set itself is the
        /// transient "visible this instant" result.
        /// </summary>
        public static HashSet<Position> ComputeVisibleTiles(TileGrid grid, Position from)
        {
            var visible = new HashSet<Position>();

            for (int y = 0; y < grid.Length; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    var to = new Position(x, y, from.Z);
                    if (from.Distance(to) > MaxViewDistance)
                        continue;
                    if (!HasLineOfSight(grid, from, to))
                        continue;

                    visible.Add(to);
                    grid[to].Discovered = true;
                }
            }

            return visible;
        }

        /// <summary>Standard integer Bresenham line walk between two tile positions (z held constant at `from.Z`).</summary>
        private static IEnumerable<Position> WalkLine(Position from, Position to)
        {
            int x0 = from.X, y0 = from.Y, x1 = to.X, y1 = to.Y;
            int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            int x = x0, y = y0;

            while (true)
            {
                yield return new Position(x, y, from.Z);
                if (x == x1 && y == y1)
                    break;

                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x += sx; }
                if (e2 <= dx) { err += dx; y += sy; }
            }
        }
    }
}
