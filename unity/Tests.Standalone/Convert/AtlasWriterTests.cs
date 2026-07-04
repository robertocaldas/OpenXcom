using System;
using System.Collections.Generic;
using System.IO;
using SixLabors.ImageSharp;
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

        [Fact]
        public void Build_WithMoreFramesThanColumns_WrapsToNextRow()
        {
            var frames = new List<IndexedFrame> { SolidFrame(1), SolidFrame(1), SolidFrame(1) };
            var atlas = AtlasWriter.Build(frames, TestPalette(), columns: 2);

            // 3 frames, 2 columns -> frame index 2 must wrap to row 1.
            Assert.Equal(0, atlas.Frames[2].X);
            Assert.Equal(2, atlas.Frames[2].Y); // row 1 * frame height (2)
        }

        [Fact]
        public void Build_SecondFramePixelsPaintedAtItsOwnOffset()
        {
            var frames = new List<IndexedFrame> { SolidFrame(1), SolidFrame(2) };
            var atlas = AtlasWriter.Build(frames, TestPalette(), columns: 16);

            // Frame 1 (green, index 2) starts at x=2 per the grid test's own expectation.
            var px = atlas.Image[2, 0];
            Assert.Equal(new Rgba32(0, 255, 0, 255), px);
        }

        [Fact]
        public void Save_RoundTrips_PngDimensionsAndLowercaseJsonKeys()
        {
            var frames = new List<IndexedFrame> { SolidFrame(1), SolidFrame(2) };
            var atlas = AtlasWriter.Build(frames, TestPalette(), columns: 16);

            string dir = Path.Combine(Path.GetTempPath(), "atlaswriter-" + Guid.NewGuid());
            string pngPath = Path.Combine(dir, "atlas.png");
            string jsonPath = Path.Combine(dir, "atlas.frames.json");

            AtlasWriter.Save(atlas, pngPath, jsonPath);

            Assert.True(File.Exists(pngPath));
            using var loaded = Image.Load<Rgba32>(pngPath);
            Assert.Equal(atlas.Image.Width, loaded.Width);
            Assert.Equal(atlas.Image.Height, loaded.Height);

            string json = File.ReadAllText(jsonPath);
            Assert.Contains("\"x\":", json);
            Assert.Contains("\"y\":", json);
            Assert.Contains("\"w\":", json);
            Assert.Contains("\"h\":", json);
            Assert.DoesNotContain("\"X\":", json);

            Directory.Delete(dir, recursive: true);
        }

        [Fact]
        public void Save_WithBareFilename_DoesNotThrow()
        {
            var frames = new List<IndexedFrame> { SolidFrame(1) };
            var atlas = AtlasWriter.Build(frames, TestPalette());

            string prevDir = Directory.GetCurrentDirectory();
            string tempDir = Path.Combine(Path.GetTempPath(), "atlaswriter-barename-" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);
            Directory.SetCurrentDirectory(tempDir);
            try
            {
                var ex = Record.Exception(() => AtlasWriter.Save(atlas, "atlas.png", "atlas.frames.json"));
                Assert.Null(ex);
                Assert.True(File.Exists("atlas.png"));
            }
            finally
            {
                Directory.SetCurrentDirectory(prevDir);
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
