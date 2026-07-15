using System;
using System.Collections.Generic;
using OpenXcom.Core.Common;
using OpenXcom.Core.Rules;

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

        /// <summary>
        /// Checks what (if anything) occupies this exact voxel: a terrain
        /// part's Loft bitmask, then (if nothing terrain-solid) a living
        /// unit's Loftemps cylinder. loftData is the flat LOFTEMPS.DAT table
        /// (DataLoader.LoadLoftemps) - any loft index outside its bounds is
        /// treated as passable, not an error, so an empty/undersized table
        /// just means "no voxel data configured" rather than crashing.
        /// Port of TileEngine::voxelCheck (src/Battlescape/TileEngine.cpp:4529-4628),
        /// scoped down to this rewrite's data model: no UFO doors, no
        /// gravlift-floor special case, no big (2x2) units - none of these
        /// exist in Core's Tile/MapDataTile/RuleArmor model today, so
        /// porting their branches would be dead code, not a real
        /// simplification of working behavior.
        /// </summary>
        public static VoxelHit VoxelCheck(TileGrid grid, ushort[] loftData, Position voxel, BattleUnit excludeUnit)
        {
            if (voxel.X < 0 || voxel.Y < 0 || voxel.Z < 0)
                return new VoxelHit(VoxelType.OutOfBounds, voxel);

            var tilePos = new Position(voxel.X / 16, voxel.Y / 16, voxel.Z / 24);
            var tile = grid[tilePos];
            if (tile == null)
                return new VoxelHit(VoxelType.OutOfBounds, voxel);

            int zLayer = (voxel.Z % 24) / 2;
            int lx = 15 - (voxel.X % 16);
            int ly = voxel.Y % 16;

            var parts = new (VoxelType type, MapDataTile part)[]
            {
                (VoxelType.Floor, tile.Floor),
                (VoxelType.WestWall, tile.WestWall),
                (VoxelType.NorthWall, tile.NorthWall),
                (VoxelType.Object, tile.Object),
            };

            foreach (var (type, part) in parts)
            {
                if (part == null) continue;
                int loftId = part.Loft[zLayer];
                int idx = loftId * 16 + ly;
                if (idx < loftData.Length && (loftData[idx] & (1 << lx)) != 0)
                    return new VoxelHit(type, voxel);
            }

            var unit = tile.Occupant;
            if (unit != null && unit.IsAlive && unit != excludeUnit)
            {
                int terrainLevel = System.Math.Min(0, tile.Floor?.TerrainLevel ?? 0);
                int tz = tilePos.Z * 24 + unit.Rules.FloatHeight - terrainLevel;
                if (voxel.Z > tz && voxel.Z <= tz + unit.Height)
                {
                    int loftId = unit.Armor.Loftemps;
                    int idx = loftId * 16 + ly;
                    if (idx < loftData.Length && (loftData[idx] & (1 << lx)) != 0)
                        return new VoxelHit(VoxelType.Unit, voxel, unit);
                }
            }

            return VoxelHit.Empty(voxel);
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
