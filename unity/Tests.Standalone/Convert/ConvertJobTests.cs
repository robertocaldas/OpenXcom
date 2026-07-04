using System.IO;
using System.Linq;
using Xcom.Convert;
using Xunit;

namespace OpenXcom.Core.Tests.Convert
{
    public class ConvertJobTests
    {
        private static readonly string DataDir = TestPaths.RawDataDir;

        [Fact]
        public void Run_ProducesPaletteTerrainUnitAndMapblockOutputs()
        {
            string outDir = Path.Combine(Path.GetTempPath(), "xcomconv-" + System.Guid.NewGuid());
            var written = ConvertJob.Run(DataDir, outDir);

            Assert.Contains(written, p => p.EndsWith("palettes.json"));
            Assert.Contains(written, p => p == "terrain-CULTIVAT.png");
            Assert.Contains(written, p => p == "tiles-CULTIVAT.json");
            Assert.Contains(written, p => p == "terrain-BLANKS.png");
            Assert.Contains(written, p => p == "tiles-BLANKS.json");
            Assert.Contains(written, p => p == "terrain-BARN.png");
            Assert.Contains(written, p => p == "tiles-BARN.json");
            Assert.Contains(written, p => p == "terrain-CULTA.datasets.json");
            Assert.Contains(written, p => p == "mapblock-CULTA00.json");
            Assert.Contains(written, p => p.Contains("units") && p.EndsWith(".png"));
            Assert.True(File.Exists(Path.Combine(outDir, "manifest.json")));

            Assert.Equal(15, written.Count);
            Assert.Contains("manifest.json", written);
            var manifestJson = File.ReadAllText(Path.Combine(outDir, "manifest.json"));
            var manifest = Newtonsoft.Json.Linq.JObject.Parse(manifestJson);
            var files = manifest["files"].Select(t => t.ToString()).ToList();
            Assert.Equal(15, files.Count);
            Assert.Contains("manifest.json", files);

            // terrain-CULTA.datasets.json content: ordered [BLANKS, CULTIVAT, BARN]
            // with real record counts (2, 37, 29 — verified against file sizes / 62).
            var datasetsJson = File.ReadAllText(Path.Combine(outDir, "terrain-CULTA.datasets.json"));
            var datasets = Newtonsoft.Json.Linq.JObject.Parse(datasetsJson);
            var dsArray = datasets["Datasets"].ToList();
            Assert.Equal(3, dsArray.Count);
            Assert.Equal("BLANKS", dsArray[0]["Name"].ToString());
            Assert.Equal(2, (int)dsArray[0]["Size"]);
            Assert.Equal("CULTIVAT", dsArray[1]["Name"].ToString());
            Assert.Equal(37, (int)dsArray[1]["Size"]);
            Assert.Equal("BARN", dsArray[2]["Name"].ToString());
            Assert.Equal(29, (int)dsArray[2]["Size"]);

            // mapblock-CULTA00.json: dims + the one real route node.
            var blockJson = File.ReadAllText(Path.Combine(outDir, "mapblock-CULTA00.json"));
            var block = Newtonsoft.Json.Linq.JObject.Parse(blockJson);
            Assert.Equal(10, (int)block["Width"]);
            Assert.Equal(10, (int)block["Length"]);
            Assert.Equal(1, (int)block["Height"]);
            Assert.Equal(100, block["Tiles"].Count());
            Assert.Single(block["RouteNodes"]);

            Directory.Delete(outDir, recursive: true);
        }
    }
}
