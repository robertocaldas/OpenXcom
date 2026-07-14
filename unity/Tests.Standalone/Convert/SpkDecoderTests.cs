using System.IO;
using Xcom.Convert.Decoders;
using Xunit;

namespace OpenXcom.Core.Tests.Convert
{
    public class SpkDecoderTests
    {
        private static readonly string DataDir = TestPaths.RawDataDir;

        [Fact]
        public void Load_DecodesHandCraftedRleBytes_TransparentRun_ExactPixelMatch()
        {
            // width=4, height=1 (4 pixels total).
            //   0xFFFF, count=1  -> 1*2=2 transparent (index 0) pixels: [0]=0,[1]=0
            //   0xFFFE, count=1  -> 1*2=2 explicit bytes follow: [2]=9, [3]=7
            byte[] data =
            {
                0xFF, 0xFF, 0x01, 0x00, // flag=65535, count=1
                0xFE, 0xFF, 0x01, 0x00, 0x09, 0x07, // flag=65534, count=1, bytes 9,7
            };

            var frame = SpkDecoder.Load(data, width: 4, height: 1);

            Assert.Equal(new byte[] { 0, 0, 9, 7 }, frame.Pixels);
        }

        [Fact]
        public void Load_UnrecognizedFlag_SkippedWithoutWritingPixels()
        {
            // A flag that's neither 0xFFFF nor 0xFFFE consumes 2 bytes and
            // writes nothing (matches Surface::loadSpk's missing else branch).
            byte[] data =
            {
                0x01, 0x02, // flag=0x0201, not 65535/65534 -> skipped
                0xFE, 0xFF, 0x01, 0x00, 0x05, 0x06, // flag=65534, count=1, bytes 5,6
            };

            var frame = SpkDecoder.Load(data, width: 2, height: 1);

            Assert.Equal(new byte[] { 5, 6 }, frame.Pixels);
        }

        [Fact]
        public void Load_RowWraparound_MatchesSetPixelIterative()
        {
            // width=2, height=2. A single run of 4 explicit bytes should wrap
            // from row 0 into row 1 exactly like Surface::setPixelIterative.
            byte[] data = { 0xFE, 0xFF, 0x02, 0x00, 0x01, 0x02, 0x03, 0x04 };

            var frame = SpkDecoder.Load(data, width: 2, height: 2);

            Assert.Equal(new byte[] { 1, 2, 3, 4 }, frame.Pixels);
        }

        [Fact]
        public void Crop_ExtractsSubRectangle()
        {
            var source = new IndexedFrame
            {
                Width = 4,
                Height = 3,
                Pixels = new byte[]
                {
                    1, 2, 3, 4,
                    5, 6, 7, 8,
                    9, 10, 11, 12,
                },
            };

            var cropped = SpkDecoder.Crop(source, x: 1, y: 1, width: 2, height: 2);

            Assert.Equal(2, cropped.Width);
            Assert.Equal(2, cropped.Height);
            Assert.Equal(new byte[] { 6, 7, 10, 11 }, cropped.Pixels);
        }

        [Fact]
        public void Load_RealIconsFile_ProducesNonTransparentIconBarRegion()
        {
            // Regression test for the real bug found live: ICONS.PCK is SPK-
            // encoded into a full 320x200 canvas (Mod.cpp:5830), not a
            // 320x56 PCK+TAB sprite frame - decoding it with PckDecoder
            // silently produced an all-transparent image. The icon bar
            // itself is the bottom 56 rows (200 - 56 = 144).
            byte[] raw = File.ReadAllBytes(Path.Combine(DataDir, "UFOGRAPH", "ICONS.PCK"));
            var full = SpkDecoder.Load(raw, width: 320, height: 200);
            var iconBar = SpkDecoder.Crop(full, x: 0, y: 144, width: 320, height: 56);

            int opaque = 0;
            foreach (var px in iconBar.Pixels)
                if (px != 0) opaque++;

            Assert.True(opaque > iconBar.Pixels.Length / 2,
                $"expected most of the icon bar region to be opaque, got {opaque}/{iconBar.Pixels.Length}");
        }
    }
}
