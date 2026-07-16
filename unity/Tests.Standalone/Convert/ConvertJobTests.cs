using System.IO;
using System.Linq;
using Xcom.Convert;
using Xunit;

namespace OpenXcom.Core.Tests.Convert
{
    public class ConvertJobTests
    {
        private static readonly string DataDir = TestPaths.RawDataDir;
        private static readonly string RulesDir = TestPaths.RulesDir;
        private static readonly string CommonDir = TestPaths.CommonDir;

        [Fact]
        public void Run_ProducesPaletteTerrainUnitRulesAndMapblockOutputs()
        {
            string outDir = Path.Combine(Path.GetTempPath(), "xcomconv-" + System.Guid.NewGuid());
            var written = ConvertJob.Run(DataDir, RulesDir, CommonDir, outDir);

            Assert.Contains(written, p => p.EndsWith("palettes.json"));
            Assert.Contains(written, p => p == "terrain-CULTIVAT.png");
            Assert.Contains(written, p => p == "tiles-CULTIVAT.json");
            Assert.Contains(written, p => p == "terrain-BLANKS.png");
            Assert.Contains(written, p => p == "tiles-BLANKS.json");
            Assert.Contains(written, p => p == "terrain-BARN.png");
            Assert.Contains(written, p => p == "tiles-BARN.json");
            Assert.Contains(written, p => p == "terrain-CULTA.datasets.json");
            Assert.Contains(written, p => p == "mapblock-CULTA00.json");
            Assert.Contains(written, p => p == "units-XCOM_0.png");
            Assert.Contains(written, p => p == "units-XCOM_0.frames.json");
            Assert.Contains(written, p => p == "units-SECTOID.png");
            Assert.Contains(written, p => p == "units-SECTOID.frames.json");
            Assert.Contains(written, p => p == "units.json");
            Assert.Contains(written, p => p == "armors.json");
            Assert.Contains(written, p => p == "loftemps.json");
            Assert.Contains(written, p => p == "items.json");
            Assert.Contains(written, p => p == "cursor.png");
            Assert.Contains(written, p => p == "cursor.frames.json");
            Assert.Contains(written, p => p == "icons.png");
            Assert.Contains(written, p => p == "icons.frames.json");
            Assert.Contains(written, p => p == "pathfinding.png");
            Assert.Contains(written, p => p == "pathfinding.frames.json");
            Assert.Contains(written, p => p == "handob.png");
            Assert.Contains(written, p => p == "handob.frames.json");
            Assert.Contains(written, p => p == "bulletsprites.png");
            Assert.Contains(written, p => p == "bulletsprites.frames.json");
            Assert.True(File.Exists(Path.Combine(outDir, "manifest.json")));

            Assert.Equal(31, written.Count);
            var manifestJson = File.ReadAllText(Path.Combine(outDir, "manifest.json"));
            var manifest = Newtonsoft.Json.Linq.JObject.Parse(manifestJson);
            var files = manifest["files"].Select(t => t.ToString()).ToList();
            Assert.Equal(31, files.Count);

            // icons.png: single 320x56 frame (no companion .TAB on disk).
            var iconsFramesJson = File.ReadAllText(Path.Combine(outDir, "icons.frames.json"));
            var iconsFrames = Newtonsoft.Json.Linq.JObject.Parse(iconsFramesJson)["frames"];
            Assert.Single(iconsFrames);
            Assert.Equal(320, (int)iconsFrames[0]["w"]);
            Assert.Equal(56, (int)iconsFrames[0]["h"]);

            // cursor.png: 32x40 frames, at least the 2 the tile selector needs.
            var cursorFramesJson = File.ReadAllText(Path.Combine(outDir, "cursor.frames.json"));
            var cursorFrames = Newtonsoft.Json.Linq.JObject.Parse(cursorFramesJson)["frames"];
            Assert.True(cursorFrames.Count() >= 2);
            Assert.Equal(32, (int)cursorFrames[0]["w"]);
            Assert.Equal(40, (int)cursorFrames[0]["h"]);

            // pathfinding.png: 24 frames (12 cols x 2 rows), 32x40 each.
            var pathFramesJson = File.ReadAllText(Path.Combine(outDir, "pathfinding.frames.json"));
            var pathFrames = Newtonsoft.Json.Linq.JObject.Parse(pathFramesJson)["frames"];
            Assert.Equal(24, pathFrames.Count());

            // units.json: soldier + Sectoid, real stats.
            var unitsJson = File.ReadAllText(Path.Combine(outDir, "units.json"));
            var units = Newtonsoft.Json.Linq.JArray.Parse(unitsJson);
            Assert.Equal(2, units.Count);
            var sectoidUnit = units.Single(u => u["Id"].ToString() == "STR_SECTOID_SOLDIER");
            Assert.Equal(54, (int)sectoidUnit["Stats"]["TimeUnits"]);
            Assert.Equal("SECTOID_ARMOR0", sectoidUnit["ArmorId"].ToString());
            var soldierUnit = units.Single(u => u["Id"].ToString() == "STR_SOLDIER");
            Assert.Equal(50, (int)soldierUnit["Stats"]["TimeUnits"]);

            // armors.json: soldier's real STR_NONE_UC armor + Sectoid armor, both with Loftemps.
            var armorsJson = File.ReadAllText(Path.Combine(outDir, "armors.json"));
            var armors = Newtonsoft.Json.Linq.JArray.Parse(armorsJson);
            Assert.Equal(2, armors.Count);
            var soldierArmorJson = armors.Single(a => a["Id"].ToString() == "STR_NONE_UC");
            Assert.Equal(12, (int)soldierArmorJson["Front"]);
            Assert.Equal(3, (int)soldierArmorJson["Loftemps"]);
            var sectoidArmorJson = armors.Single(a => a["Id"].ToString() == "SECTOID_ARMOR0");
            Assert.Equal(4, (int)sectoidArmorJson["Front"]);
            Assert.Equal(2, (int)sectoidArmorJson["Loftemps"]);

            // loftemps.json: flat ushort array, 112 templates x 16 rows.
            var loftempsJson = File.ReadAllText(Path.Combine(outDir, "loftemps.json"));
            var loftemps = Newtonsoft.Json.Linq.JArray.Parse(loftempsJson);
            Assert.Equal(112 * 16, loftemps.Count);

            // items.json: rifle + plasma pistol, power/damageType from the clip.
            var itemsJson = File.ReadAllText(Path.Combine(outDir, "items.json"));
            var items = Newtonsoft.Json.Linq.JArray.Parse(itemsJson);
            Assert.Equal(2, items.Count);
            var rifle = items.Single(i => i["Id"].ToString() == "STR_RIFLE");
            Assert.Equal(30, (int)rifle["Power"]);
            Assert.True((bool)rifle["TwoHanded"]);
            var pistol = items.Single(i => i["Id"].ToString() == "STR_PLASMA_PISTOL");
            Assert.Equal(52, (int)pistol["Power"]);

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
