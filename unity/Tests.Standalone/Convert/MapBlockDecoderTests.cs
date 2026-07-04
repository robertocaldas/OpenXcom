using System.IO;
using Xcom.Convert.Decoders;
using Xunit;

namespace OpenXcom.Core.Tests.Convert
{
    public class MapBlockDecoderTests
    {
        private static readonly string DataDir =
            Path.Combine("..", "..", "..", "..", "RawData", "Resources", "UFO");

        private static byte[] MapBytes() =>
            File.ReadAllBytes(Path.Combine(DataDir, "MAPS", "CULTA00.MAP"));

        private static byte[] RmpBytes() =>
            File.ReadAllBytes(Path.Combine(DataDir, "ROUTES", "CULTA00.RMP"));

        [Fact]
        public void LoadMap_ParsesHeaderDimensions()
        {
            var block = MapBlockDecoder.LoadMap(MapBytes());

            // CULTA00.MAP is 403 bytes = 3-byte header (sizeY,sizeX,sizeZ) +
            // 10*10*1*4 tile bytes. Header order is Y,X,Z (BattlescapeGenerator.cpp:2092-2094).
            Assert.Equal(10, block.Width);   // sizeX (header byte 1)
            Assert.Equal(10, block.Length);  // sizeY (header byte 0)
            Assert.Equal(1, block.Height);   // sizeZ (header byte 2)
            Assert.Equal(100, block.Tiles.Length);
        }

        [Fact]
        public void LoadMap_FirstTileByteMatchesHeaderPlusOffsetZero()
        {
            var raw = MapBytes();
            var block = MapBlockDecoder.LoadMap(raw);

            // With sizeZ=1, the single level's first tile record starts right
            // after the 3-byte header, in file order x=0,y=0. Since there's
            // only one level, no z-reordering applies: block tile (0,0,0)'s
            // 4 bytes are exactly raw[3..6].
            var tile = block.Tiles[block.IndexOf(0, 0, 0)];
            Assert.Equal(raw[3], tile.Floor);
            Assert.Equal(raw[4], tile.WestWall);
            Assert.Equal(raw[5], tile.NorthWall);
            Assert.Equal(raw[6], tile.Object);
        }

        [Fact]
        public void LoadMap_LastTileByteMatchesEndOfFile()
        {
            var raw = MapBytes();
            var block = MapBlockDecoder.LoadMap(raw);

            // Last tile in file order is x=9,y=9 (single level) -> last 4 bytes of the file.
            var tile = block.Tiles[block.IndexOf(9, 9, 0)];
            int last = raw.Length - 4;
            Assert.Equal(raw[last], tile.Floor);
            Assert.Equal(raw[last + 1], tile.WestWall);
            Assert.Equal(raw[last + 2], tile.NorthWall);
            Assert.Equal(raw[last + 3], tile.Object);
        }

        [Fact]
        public void LoadRmp_DecodesOneNodeWithExpectedFieldCount()
        {
            var raw = RmpBytes();
            Assert.Equal(24, raw.Length); // exactly one 24-byte record on disk

            var nodes = MapBlockDecoder.LoadRmp(raw, sizeX: 10, sizeY: 10, sizeZ: 1);

            Assert.Single(nodes);
            var n = nodes[0];
            // Position: byte0=posY, byte1=posX, byte2=posZ (raw), Z inverted: sizeZ-1-posZ.
            Assert.Equal(raw[1], n.X);
            Assert.Equal(raw[0], n.Y);
            Assert.Equal(1 - 1 - raw[2], n.Z);
            Assert.Equal(raw[19], n.Type);
            Assert.Equal(raw[20], n.Rank);
            Assert.Equal(raw[21], n.Flags);
            Assert.Equal(raw[23], n.Priority);
            Assert.Equal(5, n.Links.Length);
        }

        [Fact]
        public void LoadRmp_LinkDecoding_SpecialValuesAndAbsoluteIndicesBothWork()
        {
            // Hand-crafted single 24-byte record. Link bytes at offsets 4,7,10,13,16.
            var rec = new byte[24];
            rec[4] = 5;    // <=250 -> absolute index 5
            rec[7] = 255;  // -> -1 (unused)
            rec[10] = 254; // -> -2 (north exit)
            rec[13] = 253; // -> -3 (east exit)
            rec[16] = 251; // -> -5 (west exit)
            rec[19] = 1;   // type
            rec[20] = 2;   // rank

            var nodes = MapBlockDecoder.LoadRmp(rec, sizeX: 10, sizeY: 10, sizeZ: 1);

            Assert.Single(nodes);
            var links = nodes[0].Links;
            Assert.Equal(5, links[0]);
            Assert.Equal(-1, links[1]);
            Assert.Equal(-2, links[2]);
            Assert.Equal(-3, links[3]);
            Assert.Equal(-5, links[4]);
        }
    }
}
