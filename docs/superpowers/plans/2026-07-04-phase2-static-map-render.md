# Phase 2 (Static Map Render) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a real X-COM mapblock (`CULTA00`) into a `TileGrid` via a ported
`MapGenerator`, and draw it as a correctly-overlapping isometric scene in
Unity — the first "pan around a real map" milestone from the parent spec.

**Architecture:** Extend `Xcom.Convert` with a `MapBlockDecoder` (MAP+RMP) and
wire it plus two more MCD/PCK dataset conversions (`BLANKS`, `BARN`) into
`ConvertJob`. Add `RuleTerrain`/`MapDataTile`/`DataLoader`/`MapGenerator` to
`OpenXcom.Core` (pure C#, fully `dotnet test`-verified). Add a pure-math
`IsoProjection` class plus `TileRenderer`/`BattlescapeMapView` MonoBehaviours
to `OpenXcom.Unity`.

**Tech Stack:** .NET 8, Newtonsoft.Json (Convert-side write), System.Text.Json
(Core-side read, .NET 8 shared framework — no new package), xUnit, Unity
`SpriteRenderer`/`Texture2D`/`Sprite`.

## Global Constraints

- Target game is UFO (Enemy Unknown), not TFTD. Terrain for this phase is
  `CULTA` (mapblock `CULTA00`), datasets `BLANKS`, `CULTIVAT`, `BARN` — all
  real files already on disk under `unity/RawData/Resources/UFO/`.
- `OpenXcom.Core` (`unity/Assets/Scripts/Core/`) must have **zero
  `UnityEngine` references** — enforced by its asmdef's
  `"noEngineReferences": true`. `System.Text.Json` is part of the .NET 8
  shared framework, not a Unity package, so using it in Core does not violate
  this.
- `Xcom.Convert` never depends on `OpenXcom.Core`, and `OpenXcom.Core` never
  depends on `Xcom.Convert`. `Tests.Standalone` is the only project allowed
  to reference both (it already does — see its `.csproj`).
- No `.rul`/YAML parsing this phase. The one terrain needed (`CULTA` →
  ordered datasets `[BLANKS, CULTIVAT, BARN]`) is hardcoded in `ConvertJob`,
  the same way Phase 1 hardcoded `CULTIVAT`/`XCOM_0`. Full ruleset conversion
  is a later, separate concern.
- New Convert-emitted JSON "data dump" files (mapblock, terrain-datasets)
  follow the existing `tiles-CULTIVAT.json` precedent: plain
  `JsonConvert.SerializeObject` of the C# model's public field names
  (PascalCase). Do **not** add `[JsonProperty]` lowercase attributes to these
  — that convention is reserved for the atlas frame-rect files
  (`AtlasWriter`'s existing `x`/`y`/`w`/`h`), which is a different, already-
  established exception.
- No multi-mapblock composition (the 10×10-tile-grid stitching of several
  blocks into one battlefield) and no `.RMP` "dummy node" out-of-bounds
  culling — this phase's one mapblock has exactly one, in-bounds route node
  (verified: `CULTA00.RMP` is exactly 24 bytes). Both are explicitly deferred;
  do not add speculative handling for cases with no test data to verify
  against.
- Environment: the .NET SDK is at `~/.dotnet`, not on the default `PATH` in
  non-interactive shells. Every `dotnet` command in this plan must be run as:
  `export PATH="$HOME/.dotnet:$PATH" && dotnet ...`.
- This session's environment cannot open the Unity Editor. Tasks 1-5 are
  fully verified by `dotnet test`. Task 6 (MonoBehaviours) cannot be compiled
  or run here — its implementer and reviewer must say so explicitly rather
  than claim verification that didn't happen.

---

### Task 1: `MapBlockDecoder` — decode `.MAP` + `.RMP`

**Files:**
- Create: `unity/Xcom.Convert/Decoders/MapBlockDecoder.cs`
- Test: `unity/Tests.Standalone/Convert/MapBlockDecoderTests.cs`

**Interfaces:**
- Consumes: nothing new (raw `byte[]` file contents, read by the test/caller).
- Produces (used by Task 2):
  - `Xcom.Convert.Decoders.MapBlockTile { int Floor, WestWall, NorthWall, Object; }`
  - `Xcom.Convert.Decoders.RouteNode { int X, Y, Z, Type, Rank, Flags, Priority; int[] Links (length 5); }`
  - `Xcom.Convert.Decoders.MapBlockData { int Width, Length, Height; MapBlockTile[] Tiles; List<RouteNode> RouteNodes; int IndexOf(int x, int y, int z); }`
  - `Xcom.Convert.Decoders.MapBlockDecoder.LoadMap(byte[] map) -> MapBlockData`
  - `Xcom.Convert.Decoders.MapBlockDecoder.LoadRmp(byte[] rmp, int sizeX, int sizeY, int sizeZ) -> List<RouteNode>`

- [ ] **Step 1: Write the failing tests**

Create `unity/Tests.Standalone/Convert/MapBlockDecoderTests.cs`:

```csharp
using System.IO;
using Xcom.Convert.Decoders;
using Xunit;

namespace OpenXcom.Core.Tests.Convert
{
    public class MapBlockDecoderTests
    {
        private static readonly string DataDir =
            Path.Combine("..", "..", "..", "..", "RawData", "Resources", "UFO");

        private static byte[] MapBytes() =>
            File.ReadAllBytes(Path.Combine(DataDir, "MAPS", "CULTA00.MAP"));

        private static byte[] RmpBytes() =>
            File.ReadAllBytes(Path.Combine(DataDir, "ROUTES", "CULTA00.RMP"));

        [Fact]
        public void LoadMap_ParsesHeaderDimensions()
        {
            var block = MapBlockDecoder.LoadMap(MapBytes());

            // CULTA00.MAP is 403 bytes = 3-byte header (sizeY,sizeX,sizeZ) +
            // 10*10*1*4 tile bytes. Header order is Y,X,Z (BattlescapeGenerator.cpp:2092-2094).
            Assert.Equal(10, block.Width);   // sizeX (header byte 1)
            Assert.Equal(10, block.Length);  // sizeY (header byte 0)
            Assert.Equal(1, block.Height);   // sizeZ (header byte 2)
            Assert.Equal(100, block.Tiles.Length);
        }

        [Fact]
        public void LoadMap_FirstTileByteMatchesHeaderPlusOffsetZero()
        {
            var raw = MapBytes();
            var block = MapBlockDecoder.LoadMap(raw);

            // With sizeZ=1, the single level's first tile record starts right
            // after the 3-byte header, in file order x=0,y=0. Since there's
            // only one level, no z-reordering applies: block tile (0,0,0)'s
            // 4 bytes are exactly raw[3..6].
            var tile = block.Tiles[block.IndexOf(0, 0, 0)];
            Assert.Equal(raw[3], tile.Floor);
            Assert.Equal(raw[4], tile.WestWall);
            Assert.Equal(raw[5], tile.NorthWall);
            Assert.Equal(raw[6], tile.Object);
        }

        [Fact]
        public void LoadMap_LastTileByteMatchesEndOfFile()
        {
            var raw = MapBytes();
            var block = MapBlockDecoder.LoadMap(raw);

            // Last tile in file order is x=9,y=9 (single level) -> last 4 bytes of the file.
            var tile = block.Tiles[block.IndexOf(9, 9, 0)];
            int last = raw.Length - 4;
            Assert.Equal(raw[last], tile.Floor);
            Assert.Equal(raw[last + 1], tile.WestWall);
            Assert.Equal(raw[last + 2], tile.NorthWall);
            Assert.Equal(raw[last + 3], tile.Object);
        }

        [Fact]
        public void LoadRmp_DecodesOneNodeWithExpectedFieldCount()
        {
            var raw = RmpBytes();
            Assert.Equal(24, raw.Length); // exactly one 24-byte record on disk

            var nodes = MapBlockDecoder.LoadRmp(raw, sizeX: 10, sizeY: 10, sizeZ: 1);

            Assert.Single(nodes);
            var n = nodes[0];
            // Position: byte0=posY, byte1=posX, byte2=posZ (raw), Z inverted: sizeZ-1-posZ.
            Assert.Equal(raw[1], n.X);
            Assert.Equal(raw[0], n.Y);
            Assert.Equal(1 - 1 - raw[2], n.Z);
            Assert.Equal(raw[19], n.Type);
            Assert.Equal(raw[20], n.Rank);
            Assert.Equal(raw[21], n.Flags);
            Assert.Equal(raw[23], n.Priority);
            Assert.Equal(5, n.Links.Length);
        }

        [Fact]
        public void LoadRmp_LinkDecoding_SpecialValuesAndAbsoluteIndicesBothWork()
        {
            // Hand-crafted single 24-byte record. Link bytes at offsets 4,7,10,13,16.
            var rec = new byte[24];
            rec[4] = 5;    // <=250 -> absolute index 5
            rec[7] = 255;  // -> -1 (unused)
            rec[10] = 254; // -> -2 (north exit)
            rec[13] = 253; // -> -3 (east exit)
            rec[16] = 251; // -> -5 (west exit)
            rec[19] = 1;   // type
            rec[20] = 2;   // rank

            var nodes = MapBlockDecoder.LoadRmp(rec, sizeX: 10, sizeY: 10, sizeZ: 1);

            Assert.Single(nodes);
            var links = nodes[0].Links;
            Assert.Equal(5, links[0]);
            Assert.Equal(-1, links[1]);
            Assert.Equal(-2, links[2]);
            Assert.Equal(-3, links[3]);
            Assert.Equal(-5, links[4]);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test --filter "FullyQualifiedName~MapBlockDecoderTests"`
Expected: FAIL to build — `MapBlockDecoder`/`MapBlockData`/`RouteNode` do not exist yet.

- [ ] **Step 3: Write the implementation**

Create `unity/Xcom.Convert/Decoders/MapBlockDecoder.cs`:

```csharp
using System.Collections.Generic;

namespace Xcom.Convert.Decoders
{
    public sealed class MapBlockTile
    {
        public int Floor;
        public int WestWall;
        public int NorthWall;
        public int Object;
    }

    public sealed class RouteNode
    {
        public int X;
        public int Y;
        public int Z;
        public int Type;
        public int Rank;
        public int Flags;
        public int Priority;
        public int[] Links = new int[5];
    }

    public sealed class MapBlockData
    {
        public int Width;   // sizeX
        public int Length;  // sizeY
        public int Height;  // sizeZ
        public MapBlockTile[] Tiles;
        public List<RouteNode> RouteNodes = new();

        /// <summary>Flat index for a normalized (x,y,z) position, z=0 is the lowest level.</summary>
        public int IndexOf(int x, int y, int z) => (z * Length + y) * Width + x;
    }

    /// <summary>
    /// Decodes MAPS/*.MAP and ROUTES/*.RMP. Ports of
    /// BattlescapeGenerator::loadMAP (src/Battlescape/BattlescapeGenerator.cpp:2079-2177)
    /// and ::loadRMP (src/Battlescape/BattlescapeGenerator.cpp:2353-2410), for a
    /// single, already-selected mapblock (no xoff/yoff/zoff stacking, no
    /// out-of-bounds "dummy node" culling — not needed for one in-bounds node).
    /// </summary>
    public static class MapBlockDecoder
    {
        public static MapBlockData LoadMap(byte[] map)
        {
            int sizeY = map[0];
            int sizeX = map[1];
            int sizeZ = map[2];

            var block = new MapBlockData
            {
                Width = sizeX,
                Length = sizeY,
                Height = sizeZ,
                Tiles = new MapBlockTile[sizeX * sizeY * sizeZ],
            };

            int offset = 3;
            // File order: z from top (sizeZ-1) down to 0, each level row-major
            // (y outer, x inner) — BattlescapeGenerator.cpp:2112, 2165-2176.
            for (int z = sizeZ - 1; z >= 0; z--)
            {
                for (int y = 0; y < sizeY; y++)
                {
                    for (int x = 0; x < sizeX; x++)
                    {
                        var tile = new MapBlockTile
                        {
                            Floor = map[offset],
                            WestWall = map[offset + 1],
                            NorthWall = map[offset + 2],
                            Object = map[offset + 3],
                        };
                        offset += 4;
                        block.Tiles[block.IndexOf(x, y, z)] = tile;
                    }
                }
            }

            return block;
        }

        public static List<RouteNode> LoadRmp(byte[] rmp, int sizeX, int sizeY, int sizeZ)
        {
            var nodes = new List<RouteNode>();
            int count = rmp.Length / 24;

            for (int i = 0; i < count; i++)
            {
                int b = i * 24;
                int posY = rmp[b];
                int posX = rmp[b + 1];
                int posZ = rmp[b + 2];

                var node = new RouteNode
                {
                    X = posX,
                    Y = posY,
                    Z = sizeZ - 1 - posZ, // Z is inverted relative to the raw byte.
                    Type = rmp[b + 19],
                    Rank = rmp[b + 20],
                    Flags = rmp[b + 21],
                    Priority = rmp[b + 23],
                };

                for (int j = 0; j < 5; j++)
                {
                    int raw = rmp[b + 4 + j * 3];
                    // 255=-1 unused, 254=-2 north, 253=-3 east, 252=-4 south, 251=-5 west.
                    node.Links[j] = raw <= 250 ? raw : raw - 256;
                }

                nodes.Add(node);
            }

            return nodes;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test --filter "FullyQualifiedName~MapBlockDecoderTests"`
Expected: PASS (5/5).

- [ ] **Step 5: Commit**

```bash
git add unity/Xcom.Convert/Decoders/MapBlockDecoder.cs unity/Tests.Standalone/Convert/MapBlockDecoderTests.cs
git commit -m "feat(convert): decode MAPS/*.MAP + ROUTES/*.RMP mapblocks"
```

---

### Task 2: Wire `ConvertJob` — BLANKS/BARN datasets + CULTA00 mapblock

**Files:**
- Modify: `unity/Xcom.Convert/ConvertJob.cs`
- Modify: `unity/Tests.Standalone/Convert/ConvertJobTests.cs`

**Interfaces:**
- Consumes: `MapBlockDecoder.LoadMap`/`LoadRmp` (Task 1); existing
  `PckDecoder.Load`, `McdDecoder.Load`, `AtlasWriter.Build`/`Save` (Phase 1).
- Produces (used by Task 3/4 via real files on disk after `ConvertJob.Run`):
  new output files `terrain-BLANKS.png`/`.frames.json`, `tiles-BLANKS.json`,
  `terrain-BARN.png`/`.frames.json`, `tiles-BARN.json`,
  `terrain-CULTA.datasets.json` (shape: `{ "Name": "CULTA", "Datasets": [ { "Name": "BLANKS", "Size": 2 }, { "Name": "CULTIVAT", "Size": 37 }, { "Name": "BARN", "Size": 29 } ] }`),
  `mapblock-CULTA00.json` (a serialized `MapBlockData`, including `RouteNodes`).

- [ ] **Step 1: Write the failing test (update existing test's expectations)**

Replace the body of `unity/Tests.Standalone/Convert/ConvertJobTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test --filter "FullyQualifiedName~ConvertJobTests"`
Expected: FAIL — `written.Count` is 7, not 15; new files don't exist.

- [ ] **Step 3: Write the implementation**

Replace `unity/Xcom.Convert/ConvertJob.cs`:

```csharp
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
```

- [ ] **Step 4: Run test to verify it passes**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test --filter "FullyQualifiedName~ConvertJobTests"`
Expected: PASS (1/1).

- [ ] **Step 5: Run the full test suite (regression check)**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test`
Expected: all tests pass (Phase 1 tests + Task 1 + Task 2's new/updated tests).

- [ ] **Step 6: Commit**

```bash
git add unity/Xcom.Convert/ConvertJob.cs unity/Tests.Standalone/Convert/ConvertJobTests.cs
git commit -m "feat(convert): convert BLANKS/BARN datasets + CULTA00 mapblock"
```

---

### Task 3: `OpenXcom.Core` — `RuleTerrain`, `MapDataTile`, `DataLoader`

**Files:**
- Create: `unity/Assets/Scripts/Core/Rules/MapDataTile.cs`
- Create: `unity/Assets/Scripts/Core/Rules/RuleTerrain.cs`
- Create: `unity/Assets/Scripts/Core/Rules/DataLoader.cs`
- Test: `unity/Tests.Standalone/RuleTerrainTests.cs`
- Test: `unity/Tests.Standalone/DataLoaderTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks directly (parses Convert's JSON
  *shape*, described in Task 2, but does not reference `Xcom.Convert` types).
- Produces (used by Task 4):
  - `OpenXcom.Core.Rules.MapDataTile { int[] Frames; int ScanG; bool IsUfoDoor, StopLOS, NoFloor; int BigWall; bool Gravlift, IsDoor, BlockFire, BlockSmoke; int TuWalk, TuSlide, TuFly, Armor, TerrainLevel, YOffset; string DatasetName; int LocalIndex; bool IsBackTileObject { get; } }`
  - `OpenXcom.Core.Rules.MapDataSetInfo { string Name; int Size; }`
  - `OpenXcom.Core.Rules.RuleTerrain(string name, IReadOnlyList<MapDataSetInfo> dataSets)`, method `(string DatasetName, int LocalIndex) Resolve(int rawIndex)`
  - `OpenXcom.Core.Rules.DataLoader.LoadTiles(string gameDataDir, string datasetName) -> List<MapDataTile>`
  - `OpenXcom.Core.Rules.DataLoader.LoadTerrain(string gameDataDir, string terrainName) -> RuleTerrain`
  - `OpenXcom.Core.Rules.DataLoader.RawMapBlockTile { int Floor, WestWall, NorthWall, Object; }`
  - `OpenXcom.Core.Rules.DataLoader.RawRouteNode { int X, Y, Z, Type, Rank, Flags, Priority; int[] Links; }`
  - `OpenXcom.Core.Rules.DataLoader.RawMapBlockData { int Width, Length, Height; List<RawMapBlockTile> Tiles; List<RawRouteNode> RouteNodes; }`
  - `OpenXcom.Core.Rules.DataLoader.LoadMapBlock(string gameDataDir, string blockName) -> RawMapBlockData`

- [ ] **Step 1: Write the failing tests**

Create `unity/Tests.Standalone/RuleTerrainTests.cs`:

```csharp
using System.Collections.Generic;
using OpenXcom.Core.Rules;
using Xunit;

namespace OpenXcom.Core.Tests
{
    public class RuleTerrainTests
    {
        [Fact]
        public void Resolve_WalksDatasetsInOrder_SyntheticSizes()
        {
            // Dataset A size 3, dataset B size 5 (mirrors BLANKS/CULTIVAT ordering).
            var terrain = new RuleTerrain("TEST", new List<MapDataSetInfo>
            {
                new() { Name = "A", Size = 3 },
                new() { Name = "B", Size = 5 },
            });

            Assert.Equal(("A", 0), terrain.Resolve(0));
            Assert.Equal(("A", 2), terrain.Resolve(2));
            Assert.Equal(("B", 0), terrain.Resolve(3));
            Assert.Equal(("B", 4), terrain.Resolve(7));
        }

        [Fact]
        public void Resolve_RealCultaDatasetSizes_BoundaryIndicesLandInExpectedDataset()
        {
            // Real record counts verified against file sizes: BLANKS=2, CULTIVAT=37, BARN=29.
            var terrain = new RuleTerrain("CULTA", new List<MapDataSetInfo>
            {
                new() { Name = "BLANKS", Size = 2 },
                new() { Name = "CULTIVAT", Size = 37 },
                new() { Name = "BARN", Size = 29 },
            });

            Assert.Equal(("BLANKS", 1), terrain.Resolve(1));      // last BLANKS index
            Assert.Equal(("CULTIVAT", 0), terrain.Resolve(2));    // first CULTIVAT index
            Assert.Equal(("CULTIVAT", 36), terrain.Resolve(38));  // last CULTIVAT index
            Assert.Equal(("BARN", 0), terrain.Resolve(39));       // first BARN index
            Assert.Equal(("BARN", 28), terrain.Resolve(67));      // last BARN index
        }

        [Fact]
        public void Resolve_OutOfRangeIndex_FallsBackToFirstDatasetRecordZero()
        {
            var terrain = new RuleTerrain("TEST", new List<MapDataSetInfo>
            {
                new() { Name = "A", Size = 3 },
            });

            Assert.Equal(("A", 0), terrain.Resolve(999));
        }
    }
}
```

Create `unity/Tests.Standalone/DataLoaderTests.cs`:

```csharp
using System;
using System.IO;
using OpenXcom.Core.Rules;
using Xunit;

namespace OpenXcom.Core.Tests
{
    public class DataLoaderTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "dataloader-" + Guid.NewGuid());

        public DataLoaderTests() => Directory.CreateDirectory(_dir);
        public void Dispose() => Directory.Delete(_dir, recursive: true);

        [Fact]
        public void LoadTiles_ParsesFieldsAndBase64EncodedByteArray()
        {
            // Newtonsoft serializes a C# byte[] as a base64 string; confirm
            // System.Text.Json round-trips that same convention correctly.
            string json = @"[
                {
                    ""Frames"": ""AQIDBAUGBwg="",
                    ""ScanG"": 42,
                    ""IsUfoDoor"": false,
                    ""StopLOS"": true,
                    ""NoFloor"": false,
                    ""BigWall"": 2,
                    ""Gravlift"": false,
                    ""IsDoor"": false,
                    ""BlockFire"": false,
                    ""BlockSmoke"": false,
                    ""TuWalk"": 4,
                    ""TuSlide"": 8,
                    ""TuFly"": 1,
                    ""Armor"": 20,
                    ""TLevel"": -1,
                    ""PLevel"": 3
                }
            ]";
            File.WriteAllText(Path.Combine(_dir, "tiles-TEST.json"), json);

            var tiles = DataLoader.LoadTiles(_dir, "TEST");

            Assert.Single(tiles);
            var t = tiles[0];
            // "AQIDBAUGBwg=" base64-decodes to bytes 1,2,3,4,5,6,7,8.
            Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 8 }, t.Frames);
            Assert.Equal(42, t.ScanG);
            Assert.True(t.StopLOS);
            Assert.Equal(2, t.BigWall);
            Assert.Equal(4, t.TuWalk);
            Assert.Equal(-1, t.TerrainLevel);
            Assert.Equal(3, t.YOffset);
            Assert.Equal("TEST", t.DatasetName);
            Assert.Equal(0, t.LocalIndex);
            Assert.True(t.IsBackTileObject); // BigWall=2 < 6
        }

        [Fact]
        public void LoadTerrain_ParsesOrderedDatasetList()
        {
            string json = @"{
                ""Name"": ""CULTA"",
                ""Datasets"": [
                    { ""Name"": ""BLANKS"", ""Size"": 2 },
                    { ""Name"": ""CULTIVAT"", ""Size"": 37 },
                    { ""Name"": ""BARN"", ""Size"": 29 }
                ]
            }";
            File.WriteAllText(Path.Combine(_dir, "terrain-CULTA.datasets.json"), json);

            var terrain = DataLoader.LoadTerrain(_dir, "CULTA");

            Assert.Equal("CULTA", terrain.Name);
            Assert.Equal(3, terrain.DataSets.Count);
            Assert.Equal(("CULTIVAT", 5), terrain.Resolve(7)); // 2 (BLANKS) + 5
        }

        [Fact]
        public void LoadMapBlock_ParsesDimsTilesAndRouteNodes()
        {
            string json = @"{
                ""Width"": 2, ""Length"": 1, ""Height"": 1,
                ""Tiles"": [
                    { ""Floor"": 5, ""WestWall"": 0, ""NorthWall"": 0, ""Object"": 0 },
                    { ""Floor"": 6, ""WestWall"": 1, ""NorthWall"": 0, ""Object"": 0 }
                ],
                ""RouteNodes"": [
                    { ""X"": 1, ""Y"": 0, ""Z"": 0, ""Type"": 1, ""Rank"": 0, ""Flags"": 0, ""Priority"": 5, ""Links"": [-1,-1,-1,-1,-1] }
                ]
            }";
            File.WriteAllText(Path.Combine(_dir, "mapblock-TESTBLOCK.json"), json);

            var block = DataLoader.LoadMapBlock(_dir, "TESTBLOCK");

            Assert.Equal(2, block.Width);
            Assert.Equal(1, block.Length);
            Assert.Equal(1, block.Height);
            Assert.Equal(2, block.Tiles.Count);
            Assert.Equal(5, block.Tiles[0].Floor);
            Assert.Equal(6, block.Tiles[1].Floor);
            Assert.Equal(1, block.Tiles[1].WestWall);
            Assert.Single(block.RouteNodes);
            Assert.Equal(5, block.RouteNodes[0].Priority);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test --filter "FullyQualifiedName~RuleTerrainTests|FullyQualifiedName~DataLoaderTests"`
Expected: FAIL to build — `RuleTerrain`, `MapDataTile`, `DataLoader` do not exist yet.

- [ ] **Step 3: Write the implementation**

Create `unity/Assets/Scripts/Core/Rules/MapDataTile.cs`:

```csharp
namespace OpenXcom.Core.Rules
{
    /// <summary>
    /// One resolved terrain object record (one tile-part's worth of data),
    /// converted from an MCD record. Port of the fields MapDataSet::loadData
    /// (src/Mod/MapDataSet.cpp:105+) actually needs for static rendering.
    /// </summary>
    public sealed class MapDataTile
    {
        public int[] Frames = new int[8];
        public int ScanG;
        public bool IsUfoDoor;
        public bool StopLOS;
        public bool NoFloor;
        public int BigWall;
        public bool Gravlift;
        public bool IsDoor;
        public bool BlockFire;
        public bool BlockSmoke;
        public int TuWalk;
        public int TuSlide;
        public int TuFly;
        public int Armor;
        public int TerrainLevel; // MCD T_Level
        public int YOffset;      // MCD P_Level

        /// <summary>Which dataset's own atlas this record's Frames index into.</summary>
        public string DatasetName;

        /// <summary>0-based index within DatasetName's own record list.</summary>
        public int LocalIndex;

        /// <summary>
        /// Port of MapData::isBackTileObject (src/Mod/MapData.cpp:135-137):
        /// objects with this flag draw before units; others draw after.
        /// </summary>
        public bool IsBackTileObject => BigWall < 6 || BigWall == 9;
    }
}
```

Create `unity/Assets/Scripts/Core/Rules/RuleTerrain.cs`:

```csharp
using System.Collections.Generic;

namespace OpenXcom.Core.Rules
{
    public sealed class MapDataSetInfo
    {
        public string Name;
        public int Size;
    }

    /// <summary>
    /// A terrain's ordered list of MCD datasets. Port of
    /// RuleTerrain::getMapData (src/Mod/RuleTerrain.cpp:238-260): a raw,
    /// nonzero .MAP tile-part index resolves by walking the dataset list,
    /// subtracting each dataset's size, until it fits within one dataset.
    /// </summary>
    public sealed class RuleTerrain
    {
        public string Name { get; }
        public IReadOnlyList<MapDataSetInfo> DataSets { get; }

        public RuleTerrain(string name, IReadOnlyList<MapDataSetInfo> dataSets)
        {
            Name = name;
            DataSets = dataSets;
        }

        public (string DatasetName, int LocalIndex) Resolve(int rawIndex)
        {
            int id = rawIndex;
            foreach (var ds in DataSets)
            {
                if (id < ds.Size)
                    return (ds.Name, id);
                id -= ds.Size;
            }
            // Corrupt/out-of-range reference: fall back to the first
            // dataset's record 0 (matches the C++'s "BLANKS 0" fallback).
            return (DataSets[0].Name, 0);
        }
    }
}
```

Create `unity/Assets/Scripts/Core/Rules/DataLoader.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace OpenXcom.Core.Rules
{
    /// <summary>
    /// Reads Xcom.Convert's emitted JSON from a GameData directory into
    /// Core's rule/data model. Core never parses YAML or references
    /// Xcom.Convert's types directly — only this converted JSON shape.
    /// </summary>
    public static class DataLoader
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        public static List<MapDataTile> LoadTiles(string gameDataDir, string datasetName)
        {
            string path = Path.Combine(gameDataDir, $"tiles-{datasetName}.json");
            string json = File.ReadAllText(path);
            var raw = JsonSerializer.Deserialize<List<RawMcdRecord>>(json, Options);

            var result = new List<MapDataTile>(raw.Count);
            for (int i = 0; i < raw.Count; i++)
            {
                var r = raw[i];
                var frames = new int[r.Frames.Length];
                for (int f = 0; f < r.Frames.Length; f++) frames[f] = r.Frames[f];

                result.Add(new MapDataTile
                {
                    Frames = frames,
                    ScanG = r.ScanG,
                    IsUfoDoor = r.IsUfoDoor,
                    StopLOS = r.StopLOS,
                    NoFloor = r.NoFloor,
                    BigWall = r.BigWall,
                    Gravlift = r.Gravlift,
                    IsDoor = r.IsDoor,
                    BlockFire = r.BlockFire,
                    BlockSmoke = r.BlockSmoke,
                    TuWalk = r.TuWalk,
                    TuSlide = r.TuSlide,
                    TuFly = r.TuFly,
                    Armor = r.Armor,
                    TerrainLevel = r.TLevel,
                    YOffset = r.PLevel,
                    DatasetName = datasetName,
                    LocalIndex = i,
                });
            }
            return result;
        }

        public static RuleTerrain LoadTerrain(string gameDataDir, string terrainName)
        {
            string path = Path.Combine(gameDataDir, $"terrain-{terrainName}.datasets.json");
            string json = File.ReadAllText(path);
            var raw = JsonSerializer.Deserialize<RawTerrainDatasets>(json, Options);

            var dataSets = new List<MapDataSetInfo>(raw.Datasets.Count);
            foreach (var d in raw.Datasets)
                dataSets.Add(new MapDataSetInfo { Name = d.Name, Size = d.Size });

            return new RuleTerrain(raw.Name, dataSets);
        }

        public static RawMapBlockData LoadMapBlock(string gameDataDir, string blockName)
        {
            string path = Path.Combine(gameDataDir, $"mapblock-{blockName}.json");
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<RawMapBlockData>(json, Options);
        }

        // --- JSON-shaped DTOs matching Xcom.Convert's emitted field names ---

        private sealed class RawMcdRecord
        {
            public byte[] Frames { get; set; }
            public int ScanG { get; set; }
            public bool IsUfoDoor { get; set; }
            public bool StopLOS { get; set; }
            public bool NoFloor { get; set; }
            public int BigWall { get; set; }
            public bool Gravlift { get; set; }
            public bool IsDoor { get; set; }
            public bool BlockFire { get; set; }
            public bool BlockSmoke { get; set; }
            public int TuWalk { get; set; }
            public int TuSlide { get; set; }
            public int TuFly { get; set; }
            public int Armor { get; set; }
            public int TLevel { get; set; }
            public int PLevel { get; set; }
        }

        private sealed class RawDatasetInfo
        {
            public string Name { get; set; }
            public int Size { get; set; }
        }

        private sealed class RawTerrainDatasets
        {
            public string Name { get; set; }
            public List<RawDatasetInfo> Datasets { get; set; }
        }

        public sealed class RawMapBlockTile
        {
            public int Floor { get; set; }
            public int WestWall { get; set; }
            public int NorthWall { get; set; }
            public int Object { get; set; }
        }

        public sealed class RawRouteNode
        {
            public int X { get; set; }
            public int Y { get; set; }
            public int Z { get; set; }
            public int Type { get; set; }
            public int Rank { get; set; }
            public int Flags { get; set; }
            public int Priority { get; set; }
            public int[] Links { get; set; }
        }

        public sealed class RawMapBlockData
        {
            public int Width { get; set; }
            public int Length { get; set; }
            public int Height { get; set; }
            public List<RawMapBlockTile> Tiles { get; set; }
            public List<RawRouteNode> RouteNodes { get; set; }
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test --filter "FullyQualifiedName~RuleTerrainTests|FullyQualifiedName~DataLoaderTests"`
Expected: PASS (3 + 3 = 6 tests).

- [ ] **Step 5: Commit**

```bash
git add unity/Assets/Scripts/Core/Rules/MapDataTile.cs unity/Assets/Scripts/Core/Rules/RuleTerrain.cs unity/Assets/Scripts/Core/Rules/DataLoader.cs unity/Tests.Standalone/RuleTerrainTests.cs unity/Tests.Standalone/DataLoaderTests.cs
git commit -m "feat(core): RuleTerrain dataset resolution + DataLoader JSON parsing"
```

---

### Task 4: `OpenXcom.Core` — `MapGenerator` builds a `TileGrid`

**Files:**
- Modify: `unity/Assets/Scripts/Core/Battle/Tile.cs`
- Create: `unity/Assets/Scripts/Core/Battle/MapGenerator.cs`
- Test: `unity/Tests.Standalone/MapGeneratorTests.cs`

**Interfaces:**
- Consumes: `OpenXcom.Core.Rules.RuleTerrain`, `MapDataTile`,
  `DataLoader.RawMapBlockData` (Task 3); existing `OpenXcom.Core.Battle.TileGrid`,
  `Tile` (Phase 1).
- Produces (used by Task 6, conceptually — not compiled against this session):
  `Tile.Floor`/`WestWall`/`NorthWall`/`Object` (each `MapDataTile`, nullable);
  `OpenXcom.Core.Battle.MapGenerator.Build(DataLoader.RawMapBlockData block, RuleTerrain terrain, IReadOnlyDictionary<string, List<MapDataTile>> datasetTiles) -> TileGrid`.

- [ ] **Step 1: Write the failing test**

Create `unity/Tests.Standalone/MapGeneratorTests.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using OpenXcom.Core.Battle;
using OpenXcom.Core.Rules;
using Xunit;

namespace OpenXcom.Core.Tests
{
    /// <summary>
    /// Exercises the real pipeline end-to-end: decode real MAP/RMP/MCD bytes
    /// via Xcom.Convert's decoders (same as ConvertJob would), serialize to
    /// JSON the same way ConvertJob does, then parse via Core's DataLoader
    /// and build a TileGrid via MapGenerator. This proves the JSON schema
    /// Convert emits and the schema Core parses actually agree, not just that
    /// each side's hand-written fixtures happen to match.
    /// </summary>
    public class MapGeneratorTests : System.IDisposable
    {
        private static readonly string DataDir =
            Path.Combine("..", "..", "..", "..", "RawData", "Resources", "UFO");

        private readonly string _outDir = Path.Combine(Path.GetTempPath(), "mapgen-" + System.Guid.NewGuid());

        public MapGeneratorTests() => Directory.CreateDirectory(_outDir);
        public void Dispose() => Directory.Delete(_outDir, recursive: true);

        private (RuleTerrain terrain, Dictionary<string, List<MapDataTile>> datasetTiles, DataLoader.RawMapBlockData block) LoadReal()
        {
            // Reuse the real ConvertJob to produce genuine GameData files, then
            // read them back exactly as the shipped pipeline would.
            Xcom.Convert.ConvertJob.Run(DataDir, _outDir);

            var terrain = DataLoader.LoadTerrain(_outDir, "CULTA");
            var datasetTiles = new Dictionary<string, List<MapDataTile>>
            {
                ["BLANKS"] = DataLoader.LoadTiles(_outDir, "BLANKS"),
                ["CULTIVAT"] = DataLoader.LoadTiles(_outDir, "CULTIVAT"),
                ["BARN"] = DataLoader.LoadTiles(_outDir, "BARN"),
            };
            var block = DataLoader.LoadMapBlock(_outDir, "CULTA00");

            return (terrain, datasetTiles, block);
        }

        [Fact]
        public void Build_GridDimensionsMatchMapblock()
        {
            var (terrain, datasetTiles, block) = LoadReal();

            var grid = MapGenerator.Build(block, terrain, datasetTiles);

            Assert.Equal(10, grid.Width);
            Assert.Equal(10, grid.Length);
            Assert.Equal(1, grid.Height);
        }

        [Fact]
        public void Build_ResolvesEveryNonZeroTilePartToAMapDataTileInTheCorrectDataset()
        {
            var (terrain, datasetTiles, block) = LoadReal();
            var grid = MapGenerator.Build(block, terrain, datasetTiles);

            bool foundAtLeastOneResolvedPart = false;
            for (int y = 0; y < grid.Length; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    var tile = grid.At(x, y, 0);
                    int rawIdx = block.Tiles[block.Width * y + x /* z=0 only level */].Floor;
                    if (rawIdx > 0)
                    {
                        Assert.NotNull(tile.Floor);
                        var (expectedDataset, expectedLocal) = terrain.Resolve(rawIdx);
                        Assert.Equal(expectedDataset, tile.Floor.DatasetName);
                        Assert.Equal(expectedLocal, tile.Floor.LocalIndex);
                        foundAtLeastOneResolvedPart = true;
                    }
                    else
                    {
                        Assert.Null(tile.Floor);
                    }
                }
            }
            Assert.True(foundAtLeastOneResolvedPart, "Expected at least one nonzero floor index in CULTA00.");
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test --filter "FullyQualifiedName~MapGeneratorTests"`
Expected: FAIL to build — `MapGenerator` and `Tile.Floor` do not exist yet.

- [ ] **Step 3: Write the implementation**

Modify `unity/Assets/Scripts/Core/Battle/Tile.cs` (add 4 fields, keep the
existing `Walkable`/`BlocksSight`/`ExtraMoveCost`/`Occupant` — those stay
untouched, movement semantics are Phase 3's concern):

```csharp
using OpenXcom.Core.Rules;

namespace OpenXcom.Core.Battle
{
    /// <summary>
    /// One battlefield tile's mutable state. Slice 1 models only what movement/LOS
    /// need: whether the floor is walkable and whether a wall blocks sight/movement.
    /// OXCE's real Tile carries per-part MapData (floor/walls/object), fire, smoke,
    /// inventory, etc. — added in later slices.
    /// </summary>
    public sealed class Tile
    {
        /// <summary>Can a unit stand here (a floor exists and it is not solid).</summary>
        public bool Walkable = true;

        /// <summary>Blocks both movement into it and line of sight through it.</summary>
        public bool BlocksSight;

        /// <summary>Extra TU cost to enter this tile beyond the base move cost.</summary>
        public int ExtraMoveCost;

        /// <summary>The unit currently occupying this tile, if any.</summary>
        public BattleUnit Occupant;

        /// <summary>Resolved per-part terrain records, set by MapGenerator. Null = nothing in that slot.</summary>
        public MapDataTile Floor;
        public MapDataTile WestWall;
        public MapDataTile NorthWall;
        public MapDataTile Object;
    }
}
```

Create `unity/Assets/Scripts/Core/Battle/MapGenerator.cs`:

```csharp
using System.Collections.Generic;
using OpenXcom.Core.Common;
using OpenXcom.Core.Rules;

namespace OpenXcom.Core.Battle
{
    /// <summary>
    /// Builds a TileGrid from a decoded mapblock + terrain dataset resolution.
    /// Port of the tile-resolution step of BattlescapeGenerator::loadMAP
    /// (src/Battlescape/BattlescapeGenerator.cpp:2144-2160) for a single,
    /// already-positioned mapblock (xoff=yoff=zoff=0 — no multi-block tiling).
    /// </summary>
    public static class MapGenerator
    {
        public static TileGrid Build(
            DataLoader.RawMapBlockData block,
            RuleTerrain terrain,
            IReadOnlyDictionary<string, List<MapDataTile>> datasetTiles)
        {
            var grid = new TileGrid(block.Width, block.Length, block.Height);

            for (int z = 0; z < block.Height; z++)
            {
                for (int y = 0; y < block.Length; y++)
                {
                    for (int x = 0; x < block.Width; x++)
                    {
                        int idx = (z * block.Length + y) * block.Width + x;
                        var raw = block.Tiles[idx];
                        var tile = grid.At(x, y, z);

                        tile.Floor = Resolve(raw.Floor, terrain, datasetTiles);
                        tile.WestWall = Resolve(raw.WestWall, terrain, datasetTiles);
                        tile.NorthWall = Resolve(raw.NorthWall, terrain, datasetTiles);
                        tile.Object = Resolve(raw.Object, terrain, datasetTiles);
                    }
                }
            }

            return grid;
        }

        private static MapDataTile Resolve(
            int rawIndex,
            RuleTerrain terrain,
            IReadOnlyDictionary<string, List<MapDataTile>> datasetTiles)
        {
            if (rawIndex <= 0)
                return null;

            var (datasetName, localIndex) = terrain.Resolve(rawIndex);
            return datasetTiles[datasetName][localIndex];
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test --filter "FullyQualifiedName~MapGeneratorTests"`
Expected: PASS (2/2).

- [ ] **Step 5: Run the full test suite (regression check)**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test`
Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add unity/Assets/Scripts/Core/Battle/Tile.cs unity/Assets/Scripts/Core/Battle/MapGenerator.cs unity/Tests.Standalone/MapGeneratorTests.cs
git commit -m "feat(core): MapGenerator builds a TileGrid from a real mapblock"
```

---

### Task 5: `OpenXcom.Unity` — pure-math `IsoProjection`

**Files:**
- Create: `unity/Assets/Scripts/Unity/Rendering/IsoProjection.cs`
- Modify: `unity/Tests.Standalone/OpenXcom.Core.Tests.csproj`
- Test: `unity/Tests.Standalone/Unity/IsoProjectionTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces (used by Task 6): `OpenXcom.Unity.Rendering.IsoProjection.SpriteWidth`
  (`32`), `.SpriteHeight` (`40`), `.PartRank { Floor=0, WestWall=1, NorthWall=2, Object=3 }`,
  `.MapToScreen(int x, int y, int z) -> (int ScreenX, int ScreenY)`,
  `.SortingOrder(int x, int y, int z, int mapWidth, int mapLength, PartRank part) -> int`.

**Note:** this file lives under `Assets/Scripts/Unity/` (it is conceptually
part of the Unity renderer, per the parent spec's "Core deals only in grid
Positions... never pixels" boundary) but must contain **no `using
UnityEngine`** so `Tests.Standalone` can compile and test it directly, the
same way it already does for `Assets/Scripts/Core/**`.

- [ ] **Step 1: Write the failing test**

Create `unity/Tests.Standalone/Unity/IsoProjectionTests.cs`:

```csharp
using OpenXcom.Unity.Rendering;
using Xunit;

namespace OpenXcom.Core.Tests.Unity
{
    public class IsoProjectionTests
    {
        [Fact]
        public void MapToScreen_Origin_IsScreenOrigin()
        {
            var (sx, sy) = IsoProjection.MapToScreen(0, 0, 0);
            Assert.Equal(0, sx);
            Assert.Equal(0, sy);
        }

        [Fact]
        public void MapToScreen_MatchesCameraCppFormula()
        {
            // screenX = (x-y)*16, screenY = (x+y)*8 - z*24 (Camera.cpp:475-480, 32x40 sprites).
            var (sx, sy) = IsoProjection.MapToScreen(3, 1, 0);
            Assert.Equal((3 - 1) * 16, sx);
            Assert.Equal((3 + 1) * 8, sy);

            var (sx2, sy2) = IsoProjection.MapToScreen(2, 2, 1);
            Assert.Equal(0, sx2);
            Assert.Equal((2 + 2) * 8 - 1 * 24, sy2);
        }

        [Fact]
        public void SortingOrder_HigherZAlwaysOutranksAnyLowerZTile()
        {
            int lowZLastTile = IsoProjection.SortingOrder(9, 9, 0, 10, 10, IsoProjection.PartRank.Object);
            int highZFirstTile = IsoProjection.SortingOrder(0, 0, 1, 10, 10, IsoProjection.PartRank.Floor);
            Assert.True(highZFirstTile > lowZLastTile);
        }

        [Fact]
        public void SortingOrder_WithinATile_PartsOrderFloorThenWallsThenObject()
        {
            int floor = IsoProjection.SortingOrder(5, 5, 0, 10, 10, IsoProjection.PartRank.Floor);
            int west = IsoProjection.SortingOrder(5, 5, 0, 10, 10, IsoProjection.PartRank.WestWall);
            int north = IsoProjection.SortingOrder(5, 5, 0, 10, 10, IsoProjection.PartRank.NorthWall);
            int obj = IsoProjection.SortingOrder(5, 5, 0, 10, 10, IsoProjection.PartRank.Object);
            Assert.True(floor < west);
            Assert.True(west < north);
            Assert.True(north < obj);
        }

        [Fact]
        public void SortingOrder_LaterYAtSameZOutranksEarlierY()
        {
            int earlier = IsoProjection.SortingOrder(9, 0, 0, 10, 10, IsoProjection.PartRank.Object);
            int later = IsoProjection.SortingOrder(0, 1, 0, 10, 10, IsoProjection.PartRank.Floor);
            Assert.True(later > earlier);
        }
    }
}
```

- [ ] **Step 2: Add the new source file to the test project**

Modify `unity/Tests.Standalone/OpenXcom.Core.Tests.csproj` — add the single
Unity-side pure-math file alongside the existing Core wildcard include:

```xml
  <ItemGroup>
    <!-- Single source of truth: the pure-C# game core Unity also compiles. -->
    <Compile Include="../Assets/Scripts/Core/**/*.cs" />
    <!-- Pure-math iso projection helper: no UnityEngine dependency, testable here. -->
    <Compile Include="../Assets/Scripts/Unity/Rendering/IsoProjection.cs" />
  </ItemGroup>
```

- [ ] **Step 3: Run test to verify it fails**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test --filter "FullyQualifiedName~IsoProjectionTests"`
Expected: FAIL to build — the source file referenced by the new `<Compile Include>` doesn't exist yet.

- [ ] **Step 4: Write the implementation**

Create `unity/Assets/Scripts/Unity/Rendering/IsoProjection.cs`:

```csharp
namespace OpenXcom.Unity.Rendering
{
    /// <summary>
    /// Pure-math port of Camera::convertMapToScreen (src/Battlescape/Camera.cpp:475-480)
    /// and the per-tile-part draw order of Map::drawTerrain
    /// (src/Battlescape/Map.cpp:900-907, 939-1318). Intentionally has no
    /// UnityEngine dependency so it is unit-testable without the Editor;
    /// MonoBehaviours call into this for the actual pixel math.
    /// </summary>
    public static class IsoProjection
    {
        public const int SpriteWidth = 32;
        public const int SpriteHeight = 40;

        /// <summary>Tile-part draw rank within one tile: floor, west wall, north wall, object.</summary>
        public enum PartRank
        {
            Floor = 0,
            WestWall = 1,
            NorthWall = 2,
            Object = 3,
        }

        /// <summary>
        /// Tile-origin screen position (camera pan is added separately by the caller,
        /// matching Camera.cpp — convertMapToScreen itself never adds camera offset).
        /// </summary>
        public static (int ScreenX, int ScreenY) MapToScreen(int x, int y, int z)
        {
            int screenX = (x - y) * (SpriteWidth / 2);
            int screenY = (x + y) * (SpriteWidth / 4) - z * ((SpriteHeight + SpriteWidth / 4) / 2);
            return (screenX, screenY);
        }

        /// <summary>
        /// Monotonic back-to-front sorting order for one tile-part, reproducing
        /// the C++ draw loop's Z-outer, Y-middle, X-inner nesting plus the
        /// floor/west/north/object per-tile order (Map.cpp:900-907, 939-1318).
        /// </summary>
        public static int SortingOrder(int x, int y, int z, int mapWidth, int mapLength, PartRank part)
        {
            long tileIndex = ((long)z * mapLength + y) * mapWidth + x;
            return (int)(tileIndex * 4 + (int)part);
        }
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test --filter "FullyQualifiedName~IsoProjectionTests"`
Expected: PASS (5/5).

- [ ] **Step 6: Run the full test suite (regression check)**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test`
Expected: all tests pass.

- [ ] **Step 7: Commit**

```bash
git add unity/Assets/Scripts/Unity/Rendering/IsoProjection.cs unity/Tests.Standalone/OpenXcom.Core.Tests.csproj unity/Tests.Standalone/Unity/IsoProjectionTests.cs
git commit -m "feat(unity): pure-math IsoProjection (screen coords + sorting order)"
```

---

### Task 6: `OpenXcom.Unity` — `TileRenderer` + `BattlescapeMapView` MonoBehaviours

**⚠️ This task cannot be compiled, run, or visually verified in this
development environment — there is no Unity Editor installed and no
`UnityEngine` assemblies available to compile against.** Write this code to
the same standard of care as the rest of the plan, using only documented
Unity API behavior, but the implementer and reviewer must both report this
limitation explicitly rather than claim any test passed. There is no test
step in this task for that reason — review is spec-compliance-by-reading
only.

**Files:**
- Create: `unity/Assets/Scripts/Unity/OpenXcom.Unity.asmdef`
- Create: `unity/Assets/Scripts/Unity/Rendering/TileRenderer.cs`
- Create: `unity/Assets/Scripts/Unity/BattlescapeMapView.cs`

**Interfaces:**
- Consumes: `OpenXcom.Core.Battle.{TileGrid, MapGenerator}`,
  `OpenXcom.Core.Rules.{DataLoader, RuleTerrain, MapDataTile}` (Tasks 3-4),
  `OpenXcom.Unity.Rendering.IsoProjection` (Task 5).
- Produces: nothing consumed by a later task in this plan (this is the
  terminal task of Phase 2).

- [ ] **Step 1: Create the Unity assembly definition**

Create `unity/Assets/Scripts/Unity/OpenXcom.Unity.asmdef`:

```json
{
    "name": "OpenXcom.Unity",
    "rootNamespace": "OpenXcom.Unity",
    "references": [
        "OpenXcom.Core"
    ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": []
}
```

- [ ] **Step 2: Write `TileRenderer`**

Create `unity/Assets/Scripts/Unity/Rendering/TileRenderer.cs`:

```csharp
using UnityEngine;

namespace OpenXcom.Unity.Rendering
{
    /// <summary>
    /// Draws one battlefield tile's up-to-4 parts (floor/west wall/north
    /// wall/object) as child SpriteRenderers, positioned and sorted per
    /// IsoProjection. Pure display: takes already-resolved Sprites and a
    /// tile position, makes no gameplay decisions.
    /// </summary>
    public sealed class TileRenderer : MonoBehaviour
    {
        private SpriteRenderer _floor;
        private SpriteRenderer _westWall;
        private SpriteRenderer _northWall;
        private SpriteRenderer _object;

        private void Awake()
        {
            _floor = CreateChild("Floor");
            _westWall = CreateChild("WestWall");
            _northWall = CreateChild("NorthWall");
            _object = CreateChild("Object");
        }

        private SpriteRenderer CreateChild(string childName)
        {
            var go = new GameObject(childName);
            go.transform.SetParent(transform, worldPositionStays: false);
            return go.AddComponent<SpriteRenderer>();
        }

        /// <summary>
        /// Positions this tile at (x,y,z) and assigns each part's sprite (null
        /// = nothing in that slot, matching Core's nullable MapDataTile refs).
        /// yOffsetPixels shifts the floor sprite per MapDataTile.YOffset
        /// (Map.cpp:939-946's "-tile->getYOffset(O_FLOOR)").
        /// </summary>
        public void Setup(int x, int y, int z, int mapWidth, int mapLength,
            Sprite floorSprite, int floorYOffsetPixels,
            Sprite westWallSprite, Sprite northWallSprite, Sprite objectSprite)
        {
            var (screenX, screenY) = IsoProjection.MapToScreen(x, y, z);
            transform.localPosition = new Vector3(screenX, screenY, 0f);

            _floor.sprite = floorSprite;
            _floor.transform.localPosition = new Vector3(0f, -floorYOffsetPixels, 0f);
            _floor.sortingOrder = IsoProjection.SortingOrder(x, y, z, mapWidth, mapLength, IsoProjection.PartRank.Floor);

            _westWall.sprite = westWallSprite;
            _westWall.sortingOrder = IsoProjection.SortingOrder(x, y, z, mapWidth, mapLength, IsoProjection.PartRank.WestWall);

            _northWall.sprite = northWallSprite;
            _northWall.sortingOrder = IsoProjection.SortingOrder(x, y, z, mapWidth, mapLength, IsoProjection.PartRank.NorthWall);

            _object.sprite = objectSprite;
            _object.sortingOrder = IsoProjection.SortingOrder(x, y, z, mapWidth, mapLength, IsoProjection.PartRank.Object);
        }
    }
}
```

- [ ] **Step 3: Write `BattlescapeMapView`**

Create `unity/Assets/Scripts/Unity/BattlescapeMapView.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using OpenXcom.Core.Battle;
using OpenXcom.Core.Rules;
using OpenXcom.Unity.Rendering;
using UnityEngine;

namespace OpenXcom.Unity
{
    /// <summary>
    /// Loads the CULTA00 mapblock via Core's MapGenerator and instantiates one
    /// TileRenderer per tile. Reads converted PNG/JSON directly from
    /// Assets/GameData/ (gitignored, produced locally by `dotnet run` in
    /// Xcom.Convert — never committed) rather than importing it as a Unity
    /// asset, since the output is a regenerable derivative of copyrighted art.
    ///
    /// NOTE: this class cannot be compiled or run in the environment this was
    /// written in (no Unity Editor / UnityEngine assemblies available this
    /// session). It follows documented Unity API behavior but has not been
    /// visually verified — check it in the Editor before relying on it.
    /// </summary>
    public sealed class BattlescapeMapView : MonoBehaviour
    {
        [SerializeField] private string terrainName = "CULTA";
        [SerializeField] private string mapBlockName = "CULTA00";
        [SerializeField] private string[] datasetNames = { "BLANKS", "CULTIVAT", "BARN" };

        private void Start()
        {
            string gameDataDir = Path.Combine(Application.dataPath, "GameData");

            var terrain = DataLoader.LoadTerrain(gameDataDir, terrainName);
            var datasetTiles = new Dictionary<string, List<MapDataTile>>();
            var datasetAtlases = new Dictionary<string, (Texture2D texture, List<Rect> frameRects)>();

            foreach (var name in datasetNames)
            {
                datasetTiles[name] = DataLoader.LoadTiles(gameDataDir, name);
                datasetAtlases[name] = LoadAtlas(gameDataDir, $"terrain-{name}");
            }

            var block = DataLoader.LoadMapBlock(gameDataDir, mapBlockName);
            var grid = MapGenerator.Build(block, terrain, datasetTiles);

            for (int z = 0; z < grid.Height; z++)
            {
                for (int y = 0; y < grid.Length; y++)
                {
                    for (int x = 0; x < grid.Width; x++)
                    {
                        var tile = grid.At(x, y, z);
                        if (tile.Floor == null && tile.WestWall == null &&
                            tile.NorthWall == null && tile.Object == null)
                            continue;

                        var go = new GameObject($"Tile_{x}_{y}_{z}");
                        go.transform.SetParent(transform, worldPositionStays: false);
                        var renderer = go.AddComponent<TileRenderer>();

                        renderer.Setup(x, y, z, grid.Width, grid.Length,
                            floorSprite: SpriteFor(tile.Floor, datasetAtlases),
                            floorYOffsetPixels: tile.Floor?.YOffset ?? 0,
                            westWallSprite: SpriteFor(tile.WestWall, datasetAtlases),
                            northWallSprite: SpriteFor(tile.NorthWall, datasetAtlases),
                            objectSprite: SpriteFor(tile.Object, datasetAtlases));
                    }
                }
            }
        }

        private static (Texture2D texture, List<Rect> frameRects) LoadAtlas(string gameDataDir, string baseName)
        {
            byte[] pngBytes = File.ReadAllBytes(Path.Combine(gameDataDir, $"{baseName}.png"));
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.LoadImage(pngBytes);

            string framesJson = File.ReadAllText(Path.Combine(gameDataDir, $"{baseName}.frames.json"));
            var parsed = JsonUtility.FromJson<AtlasFramesJson>(framesJson);

            var rects = new List<Rect>(parsed.frames.Length);
            foreach (var f in parsed.frames)
            {
                // Atlas frame rects are in top-down image pixel space (AtlasWriter);
                // Unity's Sprite.Create rect is bottom-up texture pixel space.
                float flippedY = texture.height - f.y - f.h;
                rects.Add(new Rect(f.x, flippedY, f.w, f.h));
            }
            return (texture, rects);
        }

        private static Sprite SpriteFor(MapDataTile part, Dictionary<string, (Texture2D texture, List<Rect> frameRects)> atlases)
        {
            if (part == null || part.Frames.Length == 0)
                return null;

            var (texture, frameRects) = atlases[part.DatasetName];
            int frameIndex = part.Frames[0]; // static (unanimated) first frame for this phase
            if (frameIndex < 0 || frameIndex >= frameRects.Count)
                return null;

            var rect = frameRects[frameIndex];
            return Sprite.Create(texture, rect, new Vector2(0.5f, 0f), pixelsPerUnit: 32f);
        }

        [System.Serializable]
        private struct AtlasFrameJson { public int x, y, w, h; }

        [System.Serializable]
        private struct AtlasFramesJson { public AtlasFrameJson[] frames; }
    }
}
```

- [ ] **Step 4: Note the verification gap (no code step — this is the record of it)**

There is no `dotnet test` step for this task. Record in the task report,
verbatim: "Task 6 could not be compiled or executed — no Unity Editor /
UnityEngine assemblies available in this environment. Reviewed for
spec-compliance by reading only."

- [ ] **Step 5: Commit**

```bash
git add unity/Assets/Scripts/Unity/OpenXcom.Unity.asmdef unity/Assets/Scripts/Unity/Rendering/TileRenderer.cs unity/Assets/Scripts/Unity/BattlescapeMapView.cs
git commit -m "feat(unity): TileRenderer + BattlescapeMapView (unverified, no Editor access)"
```
