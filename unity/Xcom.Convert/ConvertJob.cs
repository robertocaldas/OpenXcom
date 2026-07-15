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

        public static IReadOnlyList<string> Run(string dataDir, string rulesDir, string commonDir, string outDir)
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

            // 3b. Alien unit sprite: SECTOID.
            var sectoidFrames = PckDecoder.Load(
                File.ReadAllBytes(Path.Combine(dataDir, "UNITS", "SECTOID.PCK")),
                File.ReadAllBytes(Path.Combine(dataDir, "UNITS", "SECTOID.TAB")), 32, 40);
            var sectoidSpriteAtlas = AtlasWriter.Build(sectoidFrames, pal);
            AtlasWriter.Save(sectoidSpriteAtlas,
                Path.Combine(outDir, "units-SECTOID.png"),
                Path.Combine(outDir, "units-SECTOID.frames.json"));
            written.Add("units-SECTOID.png");
            written.Add("units-SECTOID.frames.json");

            // 3c. CURSOR.PCK: tile-selector cursor (32x40, 17 frames).
            var cursorFrames = PckDecoder.Load(
                File.ReadAllBytes(Path.Combine(dataDir, "UFOGRAPH", "CURSOR.PCK")),
                File.ReadAllBytes(Path.Combine(dataDir, "UFOGRAPH", "CURSOR.TAB")), 32, 40);
            var cursorAtlas = AtlasWriter.Build(cursorFrames, pal);
            AtlasWriter.Save(cursorAtlas,
                Path.Combine(outDir, "cursor.png"),
                Path.Combine(outDir, "cursor.frames.json"));
            written.Add("cursor.png");
            written.Add("cursor.frames.json");

            // 3d. ICONS.PCK: icon bar background. Despite the ".PCK" extension
            // this is NOT a sprite-sheet PCK+TAB file - the original loads it
            // via Surface::loadSpk (a different 16-bit RLE scheme, SpkDecoder)
            // into a full 320x200 screen-sized canvas (Mod.cpp:5830), of which
            // only the bottom 56 rows are the icon bar itself (screenHeight -
            // iconsHeight = 200 - 56 = 144, the same split BattlescapeState
            // uses for visibleMapHeight). Decoding this as a 320x56 PckDecoder
            // frame (this project's original approach) fed the wrong RLE
            // scheme entirely and silently produced an all-transparent image.
            var iconsFull = SpkDecoder.Load(
                File.ReadAllBytes(Path.Combine(dataDir, "UFOGRAPH", "ICONS.PCK")), 320, 200);
            var iconsBar = SpkDecoder.Crop(iconsFull, x: 0, y: 144, width: 320, height: 56);
            var iconsAtlas = AtlasWriter.Build(new List<IndexedFrame> { iconsBar }, pal);
            AtlasWriter.Save(iconsAtlas,
                Path.Combine(outDir, "icons.png"),
                Path.Combine(outDir, "icons.frames.json"));
            written.Add("icons.png");
            written.Add("icons.frames.json");

            // 3e. Pathfinding.png: OXCE-bundled path-preview arrow sheet, already
            // a true-color PNG (12 cols x 2 rows of 32x40) - no palette decode.
            GridSpriteSheet.Convert(
                Path.Combine(commonDir, "Resources", "Pathfinding", "Pathfinding.png"),
                Path.Combine(outDir, "pathfinding.png"),
                Path.Combine(outDir, "pathfinding.frames.json"),
                frameWidth: 32, frameHeight: 40, columns: 12, rows: 2);
            written.Add("pathfinding.png");
            written.Add("pathfinding.frames.json");

            // 4. Mapblock CULTA00: .MAP + .RMP -> one JSON.
            var block = MapBlockDecoder.LoadMap(
                File.ReadAllBytes(Path.Combine(dataDir, "MAPS", "CULTA00.MAP")));
            block.RouteNodes = MapBlockDecoder.LoadRmp(
                File.ReadAllBytes(Path.Combine(dataDir, "ROUTES", "CULTA00.RMP")),
                block.Width, block.Length, block.Height);
            File.WriteAllText(Path.Combine(outDir, "mapblock-CULTA00.json"),
                JsonConvert.SerializeObject(block, Formatting.Indented));
            written.Add("mapblock-CULTA00.json");

            // 5. Rules: XCom soldier + Sectoid stats, soldier + Sectoid armor, rifle + plasma pistol.
            var soldier = RuleYamlDecoder.LoadSoldierUnit(Path.Combine(rulesDir, "soldiers.rul"), "STR_SOLDIER");
            var sectoidUnit = RuleYamlDecoder.LoadAlienUnit(Path.Combine(rulesDir, "units.rul"), "STR_SECTOID_SOLDIER");
            var sectoidArmor = RuleYamlDecoder.LoadArmor(Path.Combine(rulesDir, "armors.rul"), "SECTOID_ARMOR0");
            var soldierArmor = RuleYamlDecoder.LoadArmor(Path.Combine(rulesDir, "armors.rul"), "STR_NONE_UC");
            var rifle = RuleYamlDecoder.LoadWeapon(Path.Combine(rulesDir, "items.rul"), "STR_RIFLE", "STR_RIFLE_CLIP");
            var plasmaPistol = RuleYamlDecoder.LoadWeapon(Path.Combine(rulesDir, "items.rul"), "STR_PLASMA_PISTOL", "STR_PLASMA_PISTOL_CLIP");

            File.WriteAllText(Path.Combine(outDir, "units.json"),
                JsonConvert.SerializeObject(new[] { soldier, sectoidUnit }, Formatting.Indented));
            written.Add("units.json");

            File.WriteAllText(Path.Combine(outDir, "armors.json"),
                JsonConvert.SerializeObject(new[] { soldierArmor, sectoidArmor }, Formatting.Indented));
            written.Add("armors.json");

            File.WriteAllText(Path.Combine(outDir, "items.json"),
                JsonConvert.SerializeObject(new[] { rifle, plasmaPistol }, Formatting.Indented));
            written.Add("items.json");

            // 6. LOFTEMPS.DAT -> loftemps.json (voxel hit-detection templates, shared across all terrain/units).
            var loftemps = LoftempsDecoder.Load(File.ReadAllBytes(Path.Combine(dataDir, "GEODATA", "LOFTEMPS.DAT")));
            File.WriteAllText(Path.Combine(outDir, "loftemps.json"),
                JsonConvert.SerializeObject(loftemps, Formatting.Indented));
            written.Add("loftemps.json");

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
