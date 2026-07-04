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
    }
}
