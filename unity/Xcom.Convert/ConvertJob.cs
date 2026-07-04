using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Xcom.Convert.Decoders;
using Xcom.Convert.Output;

namespace Xcom.Convert
{
    /// <summary>
    /// Converter MVP: palette + one terrain (CULTIVAT) + one unit set (XCOM_0) →
    /// PNG atlases + JSON under outDir. Returns the list of relative paths written.
    /// </summary>
    public static class ConvertJob
    {
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

            // 2. Terrain: sprites → atlas, MCD → tiles json
            var terrainFrames = PckDecoder.Load(
                File.ReadAllBytes(Path.Combine(dataDir, "TERRAIN", "CULTIVAT.PCK")),
                File.ReadAllBytes(Path.Combine(dataDir, "TERRAIN", "CULTIVAT.TAB")), 32, 40);
            var terrainAtlas = AtlasWriter.Build(terrainFrames, pal);
            AtlasWriter.Save(terrainAtlas,
                Path.Combine(outDir, "terrain-CULTIVAT.png"),
                Path.Combine(outDir, "terrain-CULTIVAT.frames.json"));
            written.Add("terrain-CULTIVAT.png");
            written.Add("terrain-CULTIVAT.frames.json");

            var tiles = McdDecoder.Load(File.ReadAllBytes(Path.Combine(dataDir, "TERRAIN", "CULTIVAT.MCD")));
            File.WriteAllText(Path.Combine(outDir, "tiles-CULTIVAT.json"),
                JsonConvert.SerializeObject(tiles, Formatting.Indented));
            written.Add("tiles-CULTIVAT.json");

            // 3. Units: sprites → atlas
            var unitFrames = PckDecoder.Load(
                File.ReadAllBytes(Path.Combine(dataDir, "UNITS", "XCOM_0.PCK")),
                File.ReadAllBytes(Path.Combine(dataDir, "UNITS", "XCOM_0.TAB")), 32, 40);
            var unitAtlas = AtlasWriter.Build(unitFrames, pal);
            AtlasWriter.Save(unitAtlas,
                Path.Combine(outDir, "units-XCOM_0.png"),
                Path.Combine(outDir, "units-XCOM_0.frames.json"));
            written.Add("units-XCOM_0.png");
            written.Add("units-XCOM_0.frames.json");

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
