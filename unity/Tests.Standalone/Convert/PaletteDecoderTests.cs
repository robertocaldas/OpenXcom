using System.IO;
using SixLabors.ImageSharp.PixelFormats;
using Xcom.Convert.Decoders;
using Xunit;

namespace OpenXcom.Core.Tests.Convert
{
    public class PaletteDecoderTests
    {
        private static readonly string DataDir =
            Path.Combine("..", "..", "..", "..", "RawData", "Resources", "UFO");

        private static byte[] PalettesDat() =>
            File.ReadAllBytes(Path.Combine(DataDir, "GEODATA", "PALETTES.DAT"));

        [Fact]
        public void Load_Returns256Colors()
        {
            var pal = PaletteDecoder.Load(PalettesDat(), paletteIndex: 4);
            Assert.Equal(256, pal.Length);
        }

        [Fact]
        public void Load_ScalesSixBitChannelsByFour_AndMatchesRawBytes()
        {
            var raw = PalettesDat();
            int offset = 4 * 774; // battlescape palette
            var pal = PaletteDecoder.Load(raw, paletteIndex: 4);

            // color 5, channel bytes are 6-bit (0..63) scaled *4
            byte rawR = raw[offset + 5 * 3 + 0];
            Assert.Equal((byte)(rawR * 4), pal[5].R);
        }

        [Fact]
        public void Load_Index0IsTransparent()
        {
            var pal = PaletteDecoder.Load(PalettesDat(), paletteIndex: 4);
            Assert.Equal(0, pal[0].A);
        }
    }
}
