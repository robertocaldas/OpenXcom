using System.Collections.Generic;
using OpenXcom.Core.Common;
using OpenXcom.Core.Rules;

namespace OpenXcom.Core.Battle
{
    /// <summary>
    /// Builds a TileGrid from a decoded mapblock + terrain dataset resolution.
    /// Port of the tile-resolution step of BattlescapeGenerator::loadMAP
    /// (src/Battlescape/BattlescapeGenerator.cpp:2144-2160) for a single,
    /// already-positioned mapblock (xoff=yoff=zoff=0 — no multi-block tiling).
    /// </summary>
    public static class MapGenerator
    {
        public static TileGrid Build(
            DataLoader.RawMapBlockData block,
            RuleTerrain terrain,
            IReadOnlyDictionary<string, List<MapDataTile>> datasetTiles)
        {
            var grid = new TileGrid(block.Width, block.Length, block.Height);

            for (int z = 0; z < block.Height; z++)
            {
                for (int y = 0; y < block.Length; y++)
                {
                    for (int x = 0; x < block.Width; x++)
                    {
                        int idx = (z * block.Length + y) * block.Width + x;
                        var raw = block.Tiles[idx];
                        var tile = grid.At(x, y, z);

                        tile.Floor = Resolve(raw.Floor, terrain, datasetTiles);
                        tile.WestWall = Resolve(raw.WestWall, terrain, datasetTiles);
                        tile.NorthWall = Resolve(raw.NorthWall, terrain, datasetTiles);
                        tile.Object = Resolve(raw.Object, terrain, datasetTiles);

                        tile.Walkable = tile.Floor != null && !tile.Floor.NoFloor;
                        tile.BlocksSight = (tile.WestWall?.StopLOS ?? false)
                            || (tile.NorthWall?.StopLOS ?? false)
                            || (tile.Object?.StopLOS ?? false);
                    }
                }
            }

            return grid;
        }

        private static MapDataTile Resolve(
            int rawIndex,
            RuleTerrain terrain,
            IReadOnlyDictionary<string, List<MapDataTile>> datasetTiles)
        {
            if (rawIndex <= 0)
                return null;

            var (datasetName, localIndex) = terrain.Resolve(rawIndex);
            return datasetTiles[datasetName][localIndex];
        }
    }
}
