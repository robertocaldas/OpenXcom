using System.Collections.Generic;
using OpenXcom.Core.Common;
using OpenXcom.Core.Rules;

namespace OpenXcom.Core.Battle
{
    /// <summary>
    /// Builds a TileGrid from a decoded mapblock + terrain dataset resolution.
    /// Port of the tile-resolution step of BattlescapeGenerator::loadMAP
    /// (src/Battlescape/BattlescapeGenerator.cpp:2146-2160) for a single,
    /// already-positioned mapblock (xoff=yoff=zoff=0 — no multi-block tiling).
    /// A raw part value of 0 means "leave this tile part as it already is",
    /// not "clear it" (same source, the `terrainObjectID>0` guard) — matters
    /// once a piece is overlaid on top of previously-placed tiles, since a
    /// mostly-hollow piece (e.g. a craft's landing-gear deck) must let the
    /// terrain underneath show through rather than blanking it.
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

                        if (raw.Floor > 0) tile.Floor = Resolve(raw.Floor, terrain, datasetTiles);
                        if (raw.WestWall > 0) tile.WestWall = Resolve(raw.WestWall, terrain, datasetTiles);
                        if (raw.NorthWall > 0) tile.NorthWall = Resolve(raw.NorthWall, terrain, datasetTiles);
                        if (raw.Object > 0) tile.Object = Resolve(raw.Object, terrain, datasetTiles);

                        tile.Walkable = tile.Floor != null && !tile.Floor.NoFloor;
                        tile.BlocksSight = (tile.WestWall?.StopLOS ?? false)
                            || (tile.NorthWall?.StopLOS ?? false)
                            || (tile.Object?.StopLOS ?? false);
                    }
                }
            }

            return grid;
        }

        /// <summary>
        /// Resolves a MapScriptInterpreter.GeneratedLayout (an ordered list
        /// of placed pieces at grid offsets) into one TileGrid plus a
        /// combined, offset-adjusted route-node list. Pieces are applied in
        /// order - a later piece at the same cell (the final craft/UFO
        /// overlay, or a removeBlock clear) overwrites an earlier one, and a
        /// null-BlockName piece leaves that cell's tiles empty. Generalizes
        /// the single-mapblock tile-resolution step of BattlescapeGenerator::loadMAP
        /// (src/Battlescape/BattlescapeGenerator.cpp:2144-2160) to many
        /// pieces at their real (xoff, yoff) offsets, and the route-node
        /// loading BattlescapeGenerator::loadNodes performs across every
        /// placed block (called before the craft/UFO overlay pass,
        /// BattlescapeGenerator.cpp:3276) - including a landing-zone
        /// filler's own node even where a craft/UFO was placed on top of
        /// it afterward, an authentic quirk of the original's ordering.
        /// </summary>
        public static (TileGrid Grid, List<DataLoader.RawRouteNode> RouteNodes) BuildFromLayout(
            GeneratedLayout layout,
            System.Func<string, RuleTerrain> terrainByName,
            System.Func<string, DataLoader.RawMapBlockData> mapBlockByName,
            System.Func<string, IReadOnlyDictionary<string, List<MapDataTile>>> datasetTilesByTerrain)
        {
            var grid = new TileGrid(layout.MapSizeXBlocks * 10, layout.MapSizeYBlocks * 10, MaxHeightAcross(layout, mapBlockByName));
            var routeNodes = new List<DataLoader.RawRouteNode>();

            foreach (var piece in layout.Pieces)
            {
                if (piece.BlockName == null)
                {
                    ClearCell(grid, piece.GridX * 10, piece.GridY * 10); // removeBlock clear
                    continue;
                }

                var block = mapBlockByName(piece.BlockName);
                var terrain = terrainByName(piece.TerrainName);
                var datasetTiles = datasetTilesByTerrain(piece.TerrainName);
                int xoff = piece.GridX * 10, yoff = piece.GridY * 10;

                for (int z = 0; z < block.Height; z++)
                {
                    for (int y = 0; y < block.Length; y++)
                    {
                        for (int x = 0; x < block.Width; x++)
                        {
                            int idx = (z * block.Length + y) * block.Width + x;
                            var raw = block.Tiles[idx];
                            var tile = grid.At(xoff + x, yoff + y, z);
                            if (tile == null) continue; // outside the map's Z levels

                            if (raw.Floor > 0) tile.Floor = Resolve(raw.Floor, terrain, datasetTiles);
                            if (raw.WestWall > 0) tile.WestWall = Resolve(raw.WestWall, terrain, datasetTiles);
                            if (raw.NorthWall > 0) tile.NorthWall = Resolve(raw.NorthWall, terrain, datasetTiles);
                            if (raw.Object > 0) tile.Object = Resolve(raw.Object, terrain, datasetTiles);

                            tile.Walkable = tile.Floor != null && !tile.Floor.NoFloor;
                            tile.BlocksSight = (tile.WestWall?.StopLOS ?? false)
                                || (tile.NorthWall?.StopLOS ?? false)
                                || (tile.Object?.StopLOS ?? false);
                        }
                    }
                }

                foreach (var node in block.RouteNodes)
                {
                    routeNodes.Add(new DataLoader.RawRouteNode
                    {
                        X = node.X + xoff, Y = node.Y + yoff, Z = node.Z,
                        Type = node.Type, Rank = node.Rank, Flags = node.Flags,
                        Priority = node.Priority, Links = node.Links,
                    });
                }
            }

            return (grid, routeNodes);
        }

        /// <summary>
        /// Resets one 10x10-tile grid cell (across every Z level) back to
        /// empty. A null-BlockName piece is a removeBlocks clear
        /// (BattlescapeGenerator.cpp:4618-4710) applied to a cell an
        /// earlier piece already wrote tiles into, so the clear must reset
        /// those tiles rather than merely skip writing new ones.
        /// </summary>
        private static void ClearCell(TileGrid grid, int xoff, int yoff)
        {
            for (int z = 0; z < grid.Height; z++)
            {
                for (int y = 0; y < 10; y++)
                {
                    for (int x = 0; x < 10; x++)
                    {
                        var tile = grid.At(xoff + x, yoff + y, z);
                        if (tile == null) continue;

                        tile.Floor = null;
                        tile.WestWall = null;
                        tile.NorthWall = null;
                        tile.Object = null;
                        tile.Walkable = false;
                        tile.BlocksSight = false;
                    }
                }
            }
        }

        private static int MaxHeightAcross(GeneratedLayout layout, System.Func<string, DataLoader.RawMapBlockData> mapBlockByName)
        {
            int max = 1;
            foreach (var piece in layout.Pieces)
            {
                if (piece.BlockName == null) continue;
                max = System.Math.Max(max, mapBlockByName(piece.BlockName).Height);
            }
            return max;
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
