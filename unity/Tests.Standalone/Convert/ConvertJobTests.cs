using System.IO;
using System.Linq;
using Xcom.Convert;
using Xunit;

namespace OpenXcom.Core.Tests.Convert
{
    public class ConvertJobTests
    {
        private static readonly string DataDir =
            Path.Combine("..", "..", "..", "..", "RawData", "Resources", "UFO");

        [Fact]
        public void Run_ProducesPaletteTerrainAndUnitOutputs()
        {
            string outDir = Path.Combine(Path.GetTempPath(), "xcomconv-" + System.Guid.NewGuid());
            var written = ConvertJob.Run(DataDir, outDir);

            Assert.Contains(written, p => p.EndsWith("palettes.json"));
            Assert.Contains(written, p => p.Contains("terrain") && p.EndsWith(".png"));
            Assert.Contains(written, p => p.StartsWith("tiles-"));
            Assert.Contains(written, p => p.Contains("units") && p.EndsWith(".png"));
            Assert.True(File.Exists(Path.Combine(outDir, "manifest.json")));

            Assert.Equal(7, written.Count);
            Assert.Contains("manifest.json", written);
            var manifestJson = File.ReadAllText(Path.Combine(outDir, "manifest.json"));
            var manifest = Newtonsoft.Json.Linq.JObject.Parse(manifestJson);
            var files = manifest["files"].Select(t => t.ToString()).ToList();
            Assert.Equal(7, files.Count);
            Assert.Contains("manifest.json", files);

            Directory.Delete(outDir, recursive: true);
        }
    }
}
