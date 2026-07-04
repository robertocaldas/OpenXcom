using System.Collections.Generic;
using SixLabors.ImageSharp.PixelFormats;
using Xcom.Convert.Decoders;
using Xcom.Convert.Output;
using Xunit;

namespace OpenXcom.Core.Tests.Convert
{
    public class AtlasWriterTests
    {
        private static IndexedFrame SolidFrame(byte index, int w = 2, int h = 2)
        {
            var px = new byte[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = index;
            return new IndexedFrame { Width = w, Height = h, Pixels = px };
        }

        private static Rgba32[] TestPalette()
        {
            var pal = new Rgba32[256];
            pal[0] = new Rgba32(0, 0, 0, 0);       // transparent
            pal[1] = new Rgba32(255, 0, 0, 255);   // red
            pal[2] = new Rgba32(0, 255, 0, 255);   // green
            return pal;
        }

        [Fact]
        public void Build_PlacesFramesInGrid()
        {
            var frames = new List<IndexedFrame> { SolidFrame(1), SolidFrame(2) };
            var atlas = AtlasWriter.Build(frames, TestPalette(), columns: 16);

            Assert.Equal(2, atlas.Frames.Count);
            Assert.Equal(0, atlas.Frames[0].X);
            Assert.Equal(2, atlas.Frames[1].X); // second frame one column over
            Assert.Equal(0, atlas.Frames[1].Y);
        }

        [Fact]
        public void Build_AppliesPaletteToPixels()
        {
            var frames = new List<IndexedFrame> { SolidFrame(1) };
            var atlas = AtlasWriter.Build(frames, TestPalette());
            var px = atlas.Image[0, 0];
            Assert.Equal(new Rgba32(255, 0, 0, 255), px);
        }
    }
}
