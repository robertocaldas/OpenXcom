using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Xcom.Convert.Decoders;
using Xcom.Convert.Output;

namespace Xcom.Convert
{
    public sealed class DatasetInfo
    {
        public string Name;
        public int Size;
    }

    public sealed class TerrainDatasetsInfo
    {
        public string Name;
        public List<DatasetInfo> Datasets = new();
    }

    /// <summary>
    /// Converter MVP + Phase 2: palette, terrain CULTA's 3 datasets
    /// (BLANKS/CULTIVAT/BARN), one unit set (XCOM_0), and the CULTA00
    /// mapblock → PNG atlases + JSON under outDir. Returns the list of
    /// relative paths written.
    /// </summary>
    public static class ConvertJob
    {
        private static readonly string[] CultaDatasets = { "BLANKS", "CULTIVAT", "BARN" };

        public static IReadOnlyList<string> Run(string dataDir, string outDir)
        {
            Directory.CreateDirectory(outDir);
            var written = new List<string>();

            // 1. Palette
            var pal = PaletteDecoder.Load(
                File.ReadAllBytes(Path.Combine(dataDir, "GEODATA", "PALETTES.DAT")));
            File.WriteAllText(Path.Combine(outDir, "palettes.json"),
                JsonConvert.SerializeObject(new { battlescape = ToHex(pal) }, Formatting.Indented));
            written.Add("palettes.json");

            // 2. Terrain CULTA's 3 datasets: sprites -> atlas, MCD -> tiles json.
            var datasetSizes = new List<DatasetInfo>();
            foreach (var name in CultaDatasets)
            {
                var frames = PckDecoder.Load(
                    File.ReadAllBytes(Path.Combine(dataDir, "TERRAIN", $"{name}.PCK")),
                    File.ReadAllBytes(Path.Combine(dataDir, "TERRAIN", $"{name}.TAB")), 32, 40);
                var atlas = AtlasWriter.Build(frames, pal);
                AtlasWriter.Save(atlas,
                    Path.Combine(outDir, $"terrain-{name}.png"),
                    Path.Combine(outDir, $"terrain-{name}.frames.json"));
                written.Add($"terrain-{name}.png");
                written.Add($"terrain-{name}.frames.json");

                var tiles = McdDecoder.Load(File.ReadAllBytes(Path.Combine(dataDir, "TERRAIN", $"{name}.MCD")));
                File.WriteAllText(Path.Combine(outDir, $"tiles-{name}.json"),
                    JsonConvert.SerializeObject(tiles, Formatting.Indented));
                written.Add($"tiles-{name}.json");

                datasetSizes.Add(new DatasetInfo { Name = name, Size = tiles.Count });
            }

            var terrainInfo = new TerrainDatasetsInfo { Name = "CULTA", Datasets = datasetSizes };
            File.WriteAllText(Path.Combine(outDir, "terrain-CULTA.datasets.json"),
                JsonConvert.SerializeObject(terrainInfo, Formatting.Indented));
            written.Add("terrain-CULTA.datasets.json");

            // 3. Units: sprites -> atlas
            var unitFrames = PckDecoder.Load(
                File.ReadAllBytes(Path.Combine(dataDir, "UNITS", "XCOM_0.PCK")),
                File.ReadAllBytes(Path.Combine(dataDir, "UNITS", "XCOM_0.TAB")), 32, 40);
            var unitAtlas = AtlasWriter.Build(unitFrames, pal);
            AtlasWriter.Save(unitAtlas,
                Path.Combine(outDir, "units-XCOM_0.png"),
                Path.Combine(outDir, "units-XCOM_0.frames.json"));
            written.Add("units-XCOM_0.png");
            written.Add("units-XCOM_0.frames.json");

            // 4. Mapblock CULTA00: .MAP + .RMP -> one JSON.
            var block = MapBlockDecoder.LoadMap(
                File.ReadAllBytes(Path.Combine(dataDir, "MAPS", "CULTA00.MAP")));
            block.RouteNodes = MapBlockDecoder.LoadRmp(
                File.ReadAllBytes(Path.Combine(dataDir, "ROUTES", "CULTA00.RMP")),
                block.Width, block.Length, block.Height);
            File.WriteAllText(Path.Combine(outDir, "mapblock-CULTA00.json"),
                JsonConvert.SerializeObject(block, Formatting.Indented));
            written.Add("mapblock-CULTA00.json");

            written.Add("manifest.json");
            File.WriteAllText(Path.Combine(outDir, "manifest.json"),
                JsonConvert.SerializeObject(new { files = written }, Formatting.Indented));

            return written;
        }

        private static string[] ToHex(SixLabors.ImageSharp.PixelFormats.Rgba32[] pal)
        {
            var hex = new string[pal.Length];
            for (int i = 0; i < pal.Length; i++)
                hex[i] = $"#{pal[i].R:X2}{pal[i].G:X2}{pal[i].B:X2}{pal[i].A:X2}";
            return hex;
        }
    }
}
