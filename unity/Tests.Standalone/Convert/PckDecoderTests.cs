using System.IO;
using Xcom.Convert.Decoders;
using Xunit;

namespace OpenXcom.Core.Tests.Convert
{
    public class PckDecoderTests
    {
        private static readonly string DataDir =
            Path.Combine("..", "..", "..", "..", "RawData", "Resources", "UFO");

        // XCOM.PCK is the standard X-COM soldier sprite set; 32x40 frames.
        private static byte[] Pck() => File.ReadAllBytes(Path.Combine(DataDir, "UNITS", "XCOM_0.PCK"));
        private static byte[] Tab() => File.ReadAllBytes(Path.Combine(DataDir, "UNITS", "XCOM_0.TAB"));

        [Fact]
        public void Load_FrameCountMatches16BitTab()
        {
            // XCOM_0.TAB uses 16-bit offsets → nframes = tab.Length / 2.
            var frames = PckDecoder.Load(Pck(), Tab(), 32, 40);
            Assert.Equal(Tab().Length / 2, frames.Count);
        }

        [Fact]
        public void Load_EveryFrameIsFullSizeAndIndexed()
        {
            var frames = PckDecoder.Load(Pck(), Tab(), 32, 40);
            foreach (var f in frames)
            {
                Assert.Equal(32, f.Width);
                Assert.Equal(40, f.Height);
                Assert.Equal(32 * 40, f.Pixels.Length);
            }
        }

        [Fact]
        public void Load_FirstFrameHasSomeOpaquePixels()
        {
            var frames = PckDecoder.Load(Pck(), Tab(), 32, 40);
            bool anyOpaque = false;
            foreach (var px in frames[0].Pixels)
                if (px != 0) { anyOpaque = true; break; }
            Assert.True(anyOpaque, "decoded frame 0 was entirely transparent");
        }

        [Fact]
        public void Load_DecodesHandCraftedRleBytes_ExactPixelMatch()
        {
            // width=2, height=2 (4 pixels total). PCK frame bytes:
            //   lead=0            -> no leading transparent rows skipped
            //   5                 -> pixel[0] = 5 (literal)
            //   0xFE, 1           -> pixel[1] skipped (stays 0, transparent)
            //   7                 -> pixel[2] = 7 (literal)
            //   3                 -> pixel[3] = 3 (literal)
            //   0xFF              -> end of frame
            byte[] pck = { 0x00, 0x05, 0xFE, 0x01, 0x07, 0x03, 0xFF };
            // TAB: 1 frame via short array (tab.Length=2 < 4 triggers nframes=1).
            byte[] tab = { 0x00, 0x00 };

            var frames = PckDecoder.Load(pck, tab, width: 2, height: 2);

            Assert.Single(frames);
            Assert.Equal(new byte[] { 5, 0, 7, 3 }, frames[0].Pixels);
        }

        [Fact]
        public void Load_DecodesHandCraftedRleBytes_LeadingRowSkip_ExactPixelMatch()
        {
            // width=2, height=2 (4 pixels total). PCK frame bytes:
            //   lead=1            -> skip 1*width=2 leading transparent pixels
            //   5                 -> pixel[2] = 5 (literal; dst started at 2)
            //   0xFF              -> end of frame (pixel[3] stays 0, default)
            byte[] pck = { 0x01, 0x05, 0xFF };
            byte[] tab = { 0x00, 0x00 }; // tab.Length < 4 -> nframes = 1 (per PckDecoder.cs)

            var frames = PckDecoder.Load(pck, tab, width: 2, height: 2);

            Assert.Single(frames);
            Assert.Equal(new byte[] { 0, 0, 5, 0 }, frames[0].Pixels);
        }

        [Fact]
        public void Load_DecodesHandCraftedRleBytes_MultiCountTransparentRun_ExactPixelMatch()
        {
            // width=4, height=1 (4 pixels total). PCK frame bytes:
            //   lead=0            -> no leading transparent rows skipped
            //   0xFE, 3           -> 3 transparent pixels (pixel[0..2] stay 0)
            //   9                 -> pixel[3] = 9 (literal; dst was advanced to 3 by the run)
            //   0xFF              -> end of frame
            byte[] pck = { 0x00, 0xFE, 0x03, 0x09, 0xFF };
            byte[] tab = { 0x00, 0x00 }; // tab.Length < 4 -> nframes = 1

            var frames = PckDecoder.Load(pck, tab, width: 4, height: 1);

            Assert.Single(frames);
            Assert.Equal(new byte[] { 0, 0, 0, 9 }, frames[0].Pixels);
        }
    }
}
