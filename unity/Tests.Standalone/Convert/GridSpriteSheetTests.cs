using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xcom.Convert.Output;
using Xunit;

namespace OpenXcom.Core.Tests.Convert
{
    public class GridSpriteSheetTests
    {
        [Fact]
        public void Convert_CopiesThePngAndEmitsRowMajorGridFrameRects()
        {
            string srcPng = Path.Combine(TestPaths.CommonDir, "Resources", "Pathfinding", "Pathfinding.png");
            string outDir = Path.Combine(Path.GetTempPath(), "gridsheet-" + System.Guid.NewGuid());
            Directory.CreateDirectory(outDir);
            string outPng = Path.Combine(outDir, "pathfinding.png");
            string outJson = Path.Combine(outDir, "pathfinding.frames.json");

            GridSpriteSheet.Convert(srcPng, outPng, outJson, frameWidth: 32, frameHeight: 40, columns: 12, rows: 2);

            Assert.True(File.Exists(outPng));

            // The output PNG is now re-encoded (indexed source -> truecolor+alpha
            // output with colorkeying applied), so it's no longer byte-identical
            // to the source file. Compare decoded pixel content/dimensions instead.
            using var srcImage = Image.Load<Rgba32>(srcPng);
            using var outImage = Image.Load<Rgba32>(outPng);
            Assert.Equal(srcImage.Width, outImage.Width);
            Assert.Equal(srcImage.Height, outImage.Height);

            var json = JObject.Parse(File.ReadAllText(outJson));
            var frames = json["frames"];
            Assert.Equal(24, frames.Count());
            Assert.Equal(0, (int)frames[0]["x"]);
            Assert.Equal(0, (int)frames[0]["y"]);
            Assert.Equal(32, (int)frames[0]["w"]);
            Assert.Equal(40, (int)frames[0]["h"]);
            // frame 12 = first frame of the second row (12 cols/row).
            Assert.Equal(0, (int)frames[12]["x"]);
            Assert.Equal(40, (int)frames[12]["y"]);
            // frame 11 = last frame of the first row.
            Assert.Equal(11 * 32, (int)frames[11]["x"]);
            Assert.Equal(0, (int)frames[11]["y"]);

            Directory.Delete(outDir, recursive: true);
        }

        [Fact]
        public void Convert_ColorkeysIndexedSourceSoBackgroundIsTransparentButGlyphContentSurvives()
        {
            // Pathfinding.png is PNG color-type 3 (indexed/palette), with palette
            // index 0 = magenta (255,0,255) and no tRNS chunk - confirmed by
            // direct inspection. About 96% of its pixels are index 0
            // (background) and about 4% are arrow-glyph content (nonzero
            // indices). Before the fix, GridSpriteSheet.Convert did a raw file
            // copy, so every pixel decoded with alpha=255 (fully opaque) once
            // loaded as RGBA - this test guards against that regression.
            string srcPng = Path.Combine(TestPaths.CommonDir, "Resources", "Pathfinding", "Pathfinding.png");
            string outDir = Path.Combine(Path.GetTempPath(), "gridsheet-" + System.Guid.NewGuid());
            Directory.CreateDirectory(outDir);
            string outPng = Path.Combine(outDir, "pathfinding.png");
            string outJson = Path.Combine(outDir, "pathfinding.frames.json");

            GridSpriteSheet.Convert(srcPng, outPng, outJson, frameWidth: 32, frameHeight: 40, columns: 12, rows: 2);

            using var outImage = Image.Load<Rgba32>(outPng);

            // Pixel (0,0) is confirmed (by direct inspection of the source
            // indexed pixel data) to be palette index 0 - i.e. background -
            // so after colorkeying it must be fully transparent.
            Rgba32 corner = outImage[0, 0];
            Assert.Equal(0, corner.A);

            int total = 0, alphaZero = 0, alphaFull = 0;
            outImage.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < row.Length; x++)
                    {
                        total++;
                        if (row[x].A == 0) alphaZero++;
                        else if (row[x].A == 255) alphaFull++;
                    }
                }
            });

            // Background dominates the sheet (~96% measured), so require a
            // clear majority is colorkeyed to transparent...
            Assert.True(alphaZero > total / 2,
                $"expected a majority of pixels to be alpha=0 (background), got {alphaZero}/{total}");
            // ...but real arrow-glyph content (~4% measured) must survive
            // fully opaque - if the colorkey were wrong (e.g. matching every
            // color), this would be 0.
            Assert.True(alphaFull > 0,
                "expected some pixels to remain fully opaque (arrow glyph content), got none");

            Directory.Delete(outDir, recursive: true);
        }
    }
}
