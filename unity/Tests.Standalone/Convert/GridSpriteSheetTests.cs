using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
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
            Assert.Equal(File.ReadAllBytes(srcPng), File.ReadAllBytes(outPng));

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
    }
}
