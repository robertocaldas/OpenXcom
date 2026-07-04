using System.Collections.Generic;

namespace Xcom.Convert.Decoders
{
    public sealed class MapBlockTile
    {
        public int Floor;
        public int WestWall;
        public int NorthWall;
        public int Object;
    }

    public sealed class RouteNode
    {
        public int X;
        public int Y;
        public int Z;
        public int Type;
        public int Rank;
        public int Flags;
        public int Priority;
        public int[] Links = new int[5];
    }

    public sealed class MapBlockData
    {
        public int Width;   // sizeX
        public int Length;  // sizeY
        public int Height;  // sizeZ
        public MapBlockTile[] Tiles = [];
        public List<RouteNode> RouteNodes = new();

        /// <summary>Flat index for a normalized (x,y,z) position, z=0 is the lowest level.</summary>
        public int IndexOf(int x, int y, int z) => (z * Length + y) * Width + x;
    }

    /// <summary>
    /// Decodes MAPS/*.MAP and ROUTES/*.RMP. Ports of
    /// BattlescapeGenerator::loadMAP (src/Battlescape/BattlescapeGenerator.cpp:2079-2177)
    /// and ::loadRMP (src/Battlescape/BattlescapeGenerator.cpp:2353-2410), for a
    /// single, already-selected mapblock (no xoff/yoff/zoff stacking, no
    /// out-of-bounds "dummy node" culling — not needed for one in-bounds node).
    /// </summary>
    public static class MapBlockDecoder
    {
        public static MapBlockData LoadMap(byte[] map)
        {
            int sizeY = map[0];
            int sizeX = map[1];
            int sizeZ = map[2];

            var block = new MapBlockData
            {
                Width = sizeX,
                Length = sizeY,
                Height = sizeZ,
                Tiles = new MapBlockTile[sizeX * sizeY * sizeZ],
            };

            int offset = 3;
            // File order: z from top (sizeZ-1) down to 0, each level row-major
            // (y outer, x inner) — BattlescapeGenerator.cpp:2112, 2165-2176.
            for (int z = sizeZ - 1; z >= 0; z--)
            {
                for (int y = 0; y < sizeY; y++)
                {
                    for (int x = 0; x < sizeX; x++)
                    {
                        var tile = new MapBlockTile
                        {
                            Floor = map[offset],
                            WestWall = map[offset + 1],
                            NorthWall = map[offset + 2],
                            Object = map[offset + 3],
                        };
                        offset += 4;
                        block.Tiles[block.IndexOf(x, y, z)] = tile;
                    }
                }
            }

            return block;
        }

        public static List<RouteNode> LoadRmp(byte[] rmp, int sizeX, int sizeY, int sizeZ)
        {
            var nodes = new List<RouteNode>();
            int count = rmp.Length / 24;

            for (int i = 0; i < count; i++)
            {
                int b = i * 24;
                int posY = rmp[b];
                int posX = rmp[b + 1];
                int posZ = rmp[b + 2];

                var node = new RouteNode
                {
                    X = posX,
                    Y = posY,
                    Z = sizeZ - 1 - posZ, // Z is inverted relative to the raw byte.
                    Type = rmp[b + 19],
                    Rank = rmp[b + 20],
                    Flags = rmp[b + 21],
                    Priority = rmp[b + 23],
                };

                for (int j = 0; j < 5; j++)
                {
                    int raw = rmp[b + 4 + j * 3];
                    // 255=-1 unused, 254=-2 north, 253=-3 east, 252=-4 south, 251=-5 west.
                    node.Links[j] = raw <= 250 ? raw : raw - 256;
                }

                nodes.Add(node);
            }

            return nodes;
        }
    }
}
