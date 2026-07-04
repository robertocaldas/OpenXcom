# Phase 1: Converter MVP — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A standalone .NET CLI (`Xcom.Convert`) that decodes the original UFO
palette, one terrain tileset (PCK + MCD), and one unit sprite set (PCK) into
Unity-ready PNG atlases + JSON, verified by an xUnit suite that runs without Unity.

**Architecture:** A pure-C# console app with four decoders (palette, PCK/TAB
sprites, MCD tiles, atlas/JSON writers) plus a CLI. It reads original data from
`unity/RawData/Resources/UFO/` and writes to `unity/Assets/GameData/` (both
gitignored). No `UnityEngine` anywhere. Tested by structural/self-consistent
assertions against the real data files (not pre-baked golden bytes).

**Tech Stack:** .NET 8 SDK, C# 12, xUnit, SixLabors.ImageSharp (PNG encode,
converter-only), Newtonsoft.Json (JSON output).

## Global Constraints

- **No `UnityEngine` references** in `Xcom.Convert` or `OpenXcom.Core`. Convert is
  a plain .NET console app; Core's asmdef has `noEngineReferences: true`.
- **Target framework:** `net8.0`. `LangVersion` `latest`.
- **Original data path (input):** `unity/RawData/Resources/UFO/` — gitignored,
  never committed.
- **Converted output path:** `unity/Assets/GameData/` — gitignored, regenerated
  locally.
- **Serialization deps:** Newtonsoft.Json (JSON, read later by Core+Unity);
  YamlDotNet (rulesets — introduced in Phase 2, not here); SixLabors.ImageSharp
  (PNG, converter-only).
- **Byte-stable output:** identical input → identical output (no timestamps/GUIDs
  in emitted files).
- **Sprite dimensions:** UNITS and TERRAIN sprites are **32×40** px.
- **Battlescape palette:** `GEODATA/PALETTES.DAT`, palette index **4**, byte
  offset `4 × 774 = 3096`; 256 colors, 3 bytes each (6-bit), scaled `×4`; color 0
  is transparent (alpha 0).
- **Commit style:** small commits per step as shown. Work on branch
  `unity-rewrite` (already checked out).

---

### Task 0: Solution, projects, and verify the existing Core scaffold

Establishes the build. Installs the SDK, creates the solution wiring `Xcom.Convert`
and `Tests.Standalone`, and **verifies the currently-unverified Core scaffold** by
running its existing combat tests.

**Files:**
- Create: `unity/Xcom.Convert/Xcom.Convert.csproj`
- Create: `unity/Xcom.Convert/Program.cs`
- Create: `unity/OpenXcom.sln`
- Modify: `unity/Tests.Standalone/OpenXcom.Core.Tests.csproj` (add ImageSharp +
  Newtonsoft + ProjectReference to Xcom.Convert)

**Interfaces:**
- Produces: solution `unity/OpenXcom.sln`; project `Xcom.Convert` with root
  namespace `Xcom.Convert`; a runnable `dotnet test` over `Tests.Standalone`.

- [ ] **Step 1: Install the .NET 8 SDK**

Run:
```bash
brew install --cask dotnet-sdk
dotnet --version
```
Expected: prints a version `8.x` or newer. If `dotnet` is not found after install,
open a new shell or `export PATH="/usr/local/share/dotnet:$PATH"`.

- [ ] **Step 2: Run the existing scaffold tests to verify they pass**

Run:
```bash
cd unity/Tests.Standalone && dotnet test
```
Expected: build succeeds and the 8 `CombatMathTests` pass. If compile errors
appear, fix them before proceeding — this confirms the scaffold committed earlier
is sound. Do not continue until green.

- [ ] **Step 3: Create the converter console project**

Create `unity/Xcom.Convert/Xcom.Convert.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <RootNamespace>Xcom.Convert</RootNamespace>
    <AssemblyName>Xcom.Convert</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="SixLabors.ImageSharp" Version="3.1.5" />
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
  </ItemGroup>

</Project>
```

Create `unity/Xcom.Convert/Program.cs`:
```csharp
namespace Xcom.Convert
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            System.Console.WriteLine("Xcom.Convert — no command yet.");
            return 0;
        }
    }
}
```

- [ ] **Step 4: Let the test project reference the converter**

Edit `unity/Tests.Standalone/OpenXcom.Core.Tests.csproj` — add, inside a new
`<ItemGroup>`:
```xml
  <ItemGroup>
    <ProjectReference Include="../Xcom.Convert/Xcom.Convert.csproj" />
  </ItemGroup>
```

- [ ] **Step 5: Create the solution and add both projects**

Run:
```bash
cd unity
dotnet new sln -n OpenXcom
dotnet sln add Xcom.Convert/Xcom.Convert.csproj
dotnet sln add Tests.Standalone/OpenXcom.Core.Tests.csproj
dotnet build
```
Expected: solution builds; both projects compile.

- [ ] **Step 6: Commit**

```bash
git add unity/Xcom.Convert unity/OpenXcom.sln unity/Tests.Standalone/OpenXcom.Core.Tests.csproj
git commit -m "build: add Xcom.Convert project + solution, verify Core scaffold tests"
```

---

### Task 1: Palette decoder

Decode the battlescape palette from `PALETTES.DAT` into 256 RGBA colors.

**Files:**
- Create: `unity/Xcom.Convert/Decoders/PaletteDecoder.cs`
- Test: `unity/Tests.Standalone/Convert/PaletteDecoderTests.cs`

**Interfaces:**
- Produces: `Xcom.Convert.Decoders.PaletteDecoder.Load(byte[] datBytes, int paletteIndex = 4) -> Rgba32[]`
  returning exactly 256 entries; `Rgba32` is `SixLabors.ImageSharp.PixelFormats.Rgba32`.

- [ ] **Step 1: Write the failing test**

Create `unity/Tests.Standalone/Convert/PaletteDecoderTests.cs`:
```csharp
using System.IO;
using SixLabors.ImageSharp.PixelFormats;
using Xcom.Convert.Decoders;
using Xunit;

namespace OpenXcom.Core.Tests.Convert
{
    public class PaletteDecoderTests
    {
        private static readonly string DataDir =
            Path.Combine("..", "..", "..", "..", "RawData", "Resources", "UFO");

        private static byte[] PalettesDat() =>
            File.ReadAllBytes(Path.Combine(DataDir, "GEODATA", "PALETTES.DAT"));

        [Fact]
        public void Load_Returns256Colors()
        {
            var pal = PaletteDecoder.Load(PalettesDat(), paletteIndex: 4);
            Assert.Equal(256, pal.Length);
        }

        [Fact]
        public void Load_ScalesSixBitChannelsByFour_AndMatchesRawBytes()
        {
            var raw = PalettesDat();
            int offset = 4 * 774; // battlescape palette
            var pal = PaletteDecoder.Load(raw, paletteIndex: 4);

            // color 5, channel bytes are 6-bit (0..63) scaled *4
            byte rawR = raw[offset + 5 * 3 + 0];
            Assert.Equal((byte)(rawR * 4), pal[5].R);
        }

        [Fact]
        public void Load_Index0IsTransparent()
        {
            var pal = PaletteDecoder.Load(PalettesDat(), paletteIndex: 4);
            Assert.Equal(0, pal[0].A);
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run:
```bash
cd unity/Tests.Standalone && dotnet test --filter PaletteDecoderTests
```
Expected: FAIL — `PaletteDecoder` does not exist (compile error).

- [ ] **Step 3: Write the decoder**

Create `unity/Xcom.Convert/Decoders/PaletteDecoder.cs`:
```csharp
using SixLabors.ImageSharp.PixelFormats;

namespace Xcom.Convert.Decoders
{
    /// <summary>
    /// Decodes an 8-bit palette from GEODATA/PALETTES.DAT.
    /// Port of Engine/Palette.cpp loadDat: palettes are 774 bytes apart
    /// (palOffset), each color is 3 bytes of 6-bit channels scaled *4;
    /// index 0 is the transparent color.
    /// </summary>
    public static class PaletteDecoder
    {
        public const int PaletteStride = 768 + 6; // 774
        public const int BattlescapePalette = 4;

        public static Rgba32[] Load(byte[] datBytes, int paletteIndex = BattlescapePalette)
        {
            int offset = paletteIndex * PaletteStride;
            var colors = new Rgba32[256];
            for (int i = 0; i < 256; i++)
            {
                int p = offset + i * 3;
                byte r = (byte)(datBytes[p + 0] * 4);
                byte g = (byte)(datBytes[p + 1] * 4);
                byte b = (byte)(datBytes[p + 2] * 4);
                byte a = (byte)(i == 0 ? 0 : 255);
                colors[i] = new Rgba32(r, g, b, a);
            }
            return colors;
        }
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run:
```bash
cd unity/Tests.Standalone && dotnet test --filter PaletteDecoderTests
```
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add unity/Xcom.Convert/Decoders/PaletteDecoder.cs unity/Tests.Standalone/Convert/PaletteDecoderTests.cs
git commit -m "feat(convert): decode battlescape palette from PALETTES.DAT"
```

---

### Task 2: PCK/TAB sprite decoder

Decode a PCK+TAB sprite set into a list of 8-bit indexed frames.

**Files:**
- Create: `unity/Xcom.Convert/Decoders/PckDecoder.cs`
- Test: `unity/Tests.Standalone/Convert/PckDecoderTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `Xcom.Convert.Decoders.IndexedFrame` — `{ int Width; int Height; byte[] Pixels; }`
    (Pixels length == Width*Height, each byte a palette index; 0 = transparent).
  - `Xcom.Convert.Decoders.PckDecoder.Load(byte[] pck, byte[] tab, int width, int height) -> List<IndexedFrame>`.

- [ ] **Step 1: Write the failing test**

Create `unity/Tests.Standalone/Convert/PckDecoderTests.cs`:
```csharp
using System.IO;
using Xcom.Convert.Decoders;
using Xunit;

namespace OpenXcom.Core.Tests.Convert
{
    public class PckDecoderTests
    {
        private static readonly string DataDir =
            Path.Combine("..", "..", "..", "..", "RawData", "Resources", "UFO");

        // XCOM.PCK is the standard X-COM soldier sprite set; 32x40 frames.
        private static byte[] Pck() => File.ReadAllBytes(Path.Combine(DataDir, "UNITS", "XCOM_0.PCK"));
        private static byte[] Tab() => File.ReadAllBytes(Path.Combine(DataDir, "UNITS", "XCOM_0.TAB"));

        [Fact]
        public void Load_FrameCountMatches16BitTab()
        {
            // XCOM_0.TAB uses 16-bit offsets → nframes = tab.Length / 2.
            var frames = PckDecoder.Load(Pck(), Tab(), 32, 40);
            Assert.Equal(Tab().Length / 2, frames.Count);
        }

        [Fact]
        public void Load_EveryFrameIsFullSizeAndIndexed()
        {
            var frames = PckDecoder.Load(Pck(), Tab(), 32, 40);
            foreach (var f in frames)
            {
                Assert.Equal(32, f.Width);
                Assert.Equal(40, f.Height);
                Assert.Equal(32 * 40, f.Pixels.Length);
            }
        }

        [Fact]
        public void Load_FirstFrameHasSomeOpaquePixels()
        {
            var frames = PckDecoder.Load(Pck(), Tab(), 32, 40);
            bool anyOpaque = false;
            foreach (var px in frames[0].Pixels)
                if (px != 0) { anyOpaque = true; break; }
            Assert.True(anyOpaque, "decoded frame 0 was entirely transparent");
        }
    }
}
```

> If `XCOM_0.PCK` is absent, list `RawData/Resources/UFO/UNITS/` and substitute an
> existing 32×40 unit set (e.g. `SECTOID.PCK`/`.TAB`); update the filenames and the
> nframes assertion accordingly.

- [ ] **Step 2: Run the test to verify it fails**

Run:
```bash
cd unity/Tests.Standalone && dotnet test --filter PckDecoderTests
```
Expected: FAIL — `PckDecoder`/`IndexedFrame` undefined.

- [ ] **Step 3: Write the decoder**

Create `unity/Xcom.Convert/Decoders/PckDecoder.cs`:
```csharp
using System.Collections.Generic;

namespace Xcom.Convert.Decoders
{
    /// <summary>One decoded 8-bit indexed sprite frame. Index 0 = transparent.</summary>
    public sealed class IndexedFrame
    {
        public int Width;
        public int Height;
        public byte[] Pixels = System.Array.Empty<byte>();
    }

    /// <summary>
    /// Decodes PCK+TAB sprite sets. Port of Engine/SurfaceSet.cpp loadPck.
    /// TAB holds per-frame offsets: if the first 4 bytes are non-zero the offsets
    /// are 16-bit (nframes = size/2), else 32-bit (nframes = size/4).
    /// PCK per frame: first byte = number of fully-transparent rows to skip; then
    /// a stream where 0xFF ends the frame, 0xFE n writes n transparent pixels, and
    /// any other byte is a palette index written to the next pixel.
    /// </summary>
    public static class PckDecoder
    {
        public static List<IndexedFrame> Load(byte[] pck, byte[] tab, int width, int height)
        {
            int nframes;
            if (tab.Length >= 4)
            {
                int first = tab[0] | (tab[1] << 8) | (tab[2] << 16) | (tab[3] << 24);
                nframes = first != 0 ? tab.Length / 2 : tab.Length / 4;
            }
            else
            {
                nframes = 1;
            }

            var frames = new List<IndexedFrame>(nframes);
            int pckPos = 0;

            for (int frame = 0; frame < nframes; frame++)
            {
                var pixels = new byte[width * height];
                int dst = 0;

                // First byte: count of leading transparent rows.
                byte lead = pck[pckPos++];
                dst += lead * width; // pixels default to 0 (transparent)

                byte value;
                while ((value = pck[pckPos++]) != 0xFF)
                {
                    if (value == 0xFE)
                    {
                        byte count = pck[pckPos++];
                        dst += count; // transparent run
                    }
                    else
                    {
                        if (dst < pixels.Length) pixels[dst] = value;
                        dst++;
                    }
                }

                frames.Add(new IndexedFrame { Width = width, Height = height, Pixels = pixels });
            }

            // TAB offsets aren't needed to walk frames: each frame ends with 0xFF,
            // and frames are stored contiguously. TAB is used only for the count.
            return frames;
        }
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run:
```bash
cd unity/Tests.Standalone && dotnet test --filter PckDecoderTests
```
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add unity/Xcom.Convert/Decoders/PckDecoder.cs unity/Tests.Standalone/Convert/PckDecoderTests.cs
git commit -m "feat(convert): decode PCK/TAB indexed sprite frames"
```

---

### Task 3: Atlas + palette PNG writer and frame-rect JSON

Composite decoded frames into a single PNG atlas (RGBA, palette applied) and emit a
JSON manifest of frame rectangles.

**Files:**
- Create: `unity/Xcom.Convert/Output/AtlasWriter.cs`
- Test: `unity/Tests.Standalone/Convert/AtlasWriterTests.cs`

**Interfaces:**
- Consumes: `IndexedFrame` (Task 2), `Rgba32[]` palette (Task 1).
- Produces:
  - `Xcom.Convert.Output.AtlasFrame` — `{ int X; int Y; int W; int H; }`.
  - `Xcom.Convert.Output.AtlasResult` — `{ Image<Rgba32> Image; List<AtlasFrame> Frames; }`.
  - `Xcom.Convert.Output.AtlasWriter.Build(IReadOnlyList<IndexedFrame> frames, Rgba32[] palette, int columns = 16) -> AtlasResult`.
  - `AtlasWriter.Save(AtlasResult atlas, string pngPath, string jsonPath)` — writes
    PNG + `{ "frames": [ {x,y,w,h}, ... ] }` via Newtonsoft.

- [ ] **Step 1: Write the failing test**

Create `unity/Tests.Standalone/Convert/AtlasWriterTests.cs`:
```csharp
using System.Collections.Generic;
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
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run:
```bash
cd unity/Tests.Standalone && dotnet test --filter AtlasWriterTests
```
Expected: FAIL — `AtlasWriter`/`AtlasFrame`/`AtlasResult` undefined.

- [ ] **Step 3: Write the atlas writer**

Create `unity/Xcom.Convert/Output/AtlasWriter.cs`:
```csharp
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Xcom.Convert.Output
{
    public sealed class AtlasFrame
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int W { get; set; }
        public int H { get; set; }
    }

    public sealed class AtlasResult
    {
        public Image<Rgba32> Image { get; set; } = null!;
        public List<AtlasFrame> Frames { get; set; } = new();
    }

    /// <summary>
    /// Packs indexed frames into a fixed-column grid atlas, applying the palette.
    /// All frames are assumed the same size (true for a single PCK set).
    /// </summary>
    public static class AtlasWriter
    {
        public static AtlasResult Build(IReadOnlyList<Decoders.IndexedFrame> frames,
            Rgba32[] palette, int columns = 16)
        {
            if (frames.Count == 0)
                return new AtlasResult { Image = new Image<Rgba32>(1, 1) };

            int fw = frames[0].Width, fh = frames[0].Height;
            int cols = System.Math.Min(columns, frames.Count);
            int rows = (frames.Count + cols - 1) / cols;
            var image = new Image<Rgba32>(cols * fw, rows * fh);
            var rects = new List<AtlasFrame>(frames.Count);

            for (int i = 0; i < frames.Count; i++)
            {
                int cx = (i % cols) * fw;
                int cy = (i / cols) * fh;
                var f = frames[i];
                for (int y = 0; y < fh; y++)
                    for (int x = 0; x < fw; x++)
                        image[cx + x, cy + y] = palette[f.Pixels[y * fw + x]];
                rects.Add(new AtlasFrame { X = cx, Y = cy, W = fw, H = fh });
            }

            return new AtlasResult { Image = image, Frames = rects };
        }

        public static void Save(AtlasResult atlas, string pngPath, string jsonPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(pngPath)!);
            atlas.Image.SaveAsPng(pngPath);
            var json = JsonConvert.SerializeObject(new { frames = atlas.Frames }, Formatting.Indented);
            File.WriteAllText(jsonPath, json);
        }
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run:
```bash
cd unity/Tests.Standalone && dotnet test --filter AtlasWriterTests
```
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add unity/Xcom.Convert/Output/AtlasWriter.cs unity/Tests.Standalone/Convert/AtlasWriterTests.cs
git commit -m "feat(convert): pack frames into palette-applied PNG atlas + rect JSON"
```

---

### Task 4: MCD tile decoder

Decode a terrain's `.MCD` file into per-tile-part records (JSON).

**Files:**
- Create: `unity/Xcom.Convert/Decoders/McdDecoder.cs`
- Test: `unity/Tests.Standalone/Convert/McdDecoderTests.cs`

**Interfaces:**
- Produces:
  - `Xcom.Convert.Decoders.McdRecord` — fields: `byte[] Frames` (8), `int ScanG`,
    `bool IsUfoDoor`, `bool StopLOS`, `bool NoFloor`, `int BigWall`, `bool Gravlift`,
    `bool IsDoor`, `bool BlockFire`, `bool BlockSmoke`, `int TuWalk`, `int TuSlide`,
    `int TuFly`, `int Armor`, `int HeBlock`, `int DieMcd`, `int Flammable`,
    `int AltMcd`, `int TLevel`, `int PLevel`, `int LightBlock`, `int Footstep`,
    `int TileType`, `int HeType`, `int HeStrength`, `int SmokeBlockage`, `int Fuel`,
    `int LightSource`, `int TargetType`, `int XcomBase`.
  - `Xcom.Convert.Decoders.McdDecoder.Load(byte[] mcd) -> List<McdRecord>`.
  - Record size constant `McdDecoder.RecordSize == 62`.

- [ ] **Step 1: Write the failing test**

Create `unity/Tests.Standalone/Convert/McdDecoderTests.cs`:
```csharp
using System.IO;
using Xcom.Convert.Decoders;
using Xunit;

namespace OpenXcom.Core.Tests.Convert
{
    public class McdDecoderTests
    {
        private static readonly string DataDir =
            Path.Combine("..", "..", "..", "..", "RawData", "Resources", "UFO");

        private static byte[] Mcd() => File.ReadAllBytes(Path.Combine(DataDir, "TERRAIN", "CULTIVAT.MCD"));

        [Fact]
        public void RecordSizeIs62()
        {
            Assert.Equal(62, McdDecoder.RecordSize);
        }

        [Fact]
        public void Load_RecordCountMatchesFileSizeOver62()
        {
            var records = McdDecoder.Load(Mcd());
            Assert.Equal(Mcd().Length / 62, records.Count);
        }

        [Fact]
        public void Load_ParsesFrameAndFlagFieldsAtCorrectOffsets()
        {
            var raw = Mcd();
            var records = McdDecoder.Load(raw);
            var r0 = records[0];

            // Frame[0] is byte 0; Stop_LOS is byte 31; TU_Walk is byte 39; T_Level is byte 48.
            Assert.Equal(raw[0], r0.Frames[0]);
            Assert.Equal(raw[31] != 0, r0.StopLOS);
            Assert.Equal(raw[39], (byte)r0.TuWalk);
            Assert.Equal((sbyte)raw[48], (sbyte)r0.TLevel);
        }
    }
}
```

> If `CULTIVAT.MCD` is absent, list `RawData/Resources/UFO/TERRAIN/` and pick any
> `.MCD` present; update the filename.

- [ ] **Step 2: Run the test to verify it fails**

Run:
```bash
cd unity/Tests.Standalone && dotnet test --filter McdDecoderTests
```
Expected: FAIL — `McdDecoder`/`McdRecord` undefined.

- [ ] **Step 3: Write the decoder**

Create `unity/Xcom.Convert/Decoders/McdDecoder.cs`. Field order/offsets are the
`struct MCD` from `Mod/MapDataSet.cpp` (62 bytes, `#pragma pack(1)`):
```csharp
using System.Collections.Generic;

namespace Xcom.Convert.Decoders
{
    public sealed class McdRecord
    {
        public byte[] Frames = new byte[8]; // animation frames (indices into the terrain PCK)
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
        public int HeBlock;
        public int DieMcd;
        public int Flammable;
        public int AltMcd;
        public int TLevel;   // signed
        public int PLevel;
        public int LightBlock;
        public int Footstep;
        public int TileType;
        public int HeType;
        public int HeStrength;
        public int SmokeBlockage;
        public int Fuel;
        public int LightSource;
        public int TargetType;
        public int XcomBase;
    }

    /// <summary>
    /// Decodes TERRAIN/*.MCD. Port of the 62-byte struct MCD in
    /// Mod/MapDataSet.cpp:115. Byte layout (0-indexed), derived by counting the
    /// struct members in order (Frame[8]=0..7, LOFT[12]=8..19, ScanG u16=20..21,
    /// then unused u23..u30 = bytes 22..29):
    ///   30 UFO_Door  31 Stop_LOS  32 No_Floor  33 Big_Wall  34 Gravlift
    ///   35 Door  36 Block_Fire  37 Block_Smoke  38 u39  39 TU_Walk
    ///   40 TU_Slide  41 TU_Fly  42 Armor  43 HE_Block  44 Die_MCD  45 Flammable
    ///   46 Alt_MCD  47 u48  48 T_Level(signed)  49 P_Level  50 u51  51 Light_Block
    ///   52 Footstep  53 Tile_Type  54 HE_Type  55 HE_Strength  56 Smoke_Blockage
    ///   57 Fuel  58 Light_Source  59 Target_Type  60 Xcom_Base  61 u62
    /// </summary>
    public static class McdDecoder
    {
        public const int RecordSize = 62;

        public static List<McdRecord> Load(byte[] mcd)
        {
            int count = mcd.Length / RecordSize;
            var records = new List<McdRecord>(count);
            for (int i = 0; i < count; i++)
            {
                int b = i * RecordSize;
                var r = new McdRecord();
                for (int f = 0; f < 8; f++) r.Frames[f] = mcd[b + f];
                r.ScanG        = mcd[b + 20] | (mcd[b + 21] << 8);
                r.IsUfoDoor    = mcd[b + 30] != 0;
                r.StopLOS      = mcd[b + 31] != 0;
                r.NoFloor      = mcd[b + 32] != 0;
                r.BigWall      = mcd[b + 33];
                r.Gravlift     = mcd[b + 34] != 0;
                r.IsDoor       = mcd[b + 35] != 0;
                r.BlockFire    = mcd[b + 36] != 0;
                r.BlockSmoke   = mcd[b + 37] != 0;
                r.TuWalk       = mcd[b + 39];
                r.TuSlide      = mcd[b + 40];
                r.TuFly        = mcd[b + 41];
                r.Armor        = mcd[b + 42];
                r.HeBlock      = mcd[b + 43];
                r.DieMcd       = mcd[b + 44];
                r.Flammable    = mcd[b + 45];
                r.AltMcd       = mcd[b + 46];
                r.TLevel       = (sbyte)mcd[b + 48];
                r.PLevel       = mcd[b + 49];
                r.LightBlock   = mcd[b + 51];
                r.Footstep     = mcd[b + 52];
                r.TileType     = mcd[b + 53];
                r.HeType       = mcd[b + 54];
                r.HeStrength   = mcd[b + 55];
                r.SmokeBlockage= mcd[b + 56];
                r.Fuel         = mcd[b + 57];
                r.LightSource  = mcd[b + 58];
                r.TargetType   = mcd[b + 59];
                r.XcomBase     = mcd[b + 60];
                records.Add(r);
            }
            return records;
        }
    }
}
```

> **Offset verification:** these offsets were counted from `struct MCD`
> (`Mod/MapDataSet.cpp:115`). The Task-4 test asserts `Stop_LOS` at byte 31,
> `TU_Walk` at 39, and `T_Level` at 48 — matching this decoder. If Step 4 fails on
> the offset test, re-count struct members against the real `.MCD` file rather than
> guessing, and fix code + test together.

- [ ] **Step 4: Run the test to verify it passes**

Run:
```bash
cd unity/Tests.Standalone && dotnet test --filter McdDecoderTests
```
Expected: PASS (3 tests). If the offset test fails, apply the caution note above,
then re-run until green.

- [ ] **Step 5: Commit**

```bash
git add unity/Xcom.Convert/Decoders/McdDecoder.cs unity/Tests.Standalone/Convert/McdDecoderTests.cs
git commit -m "feat(convert): decode MCD terrain tile records"
```

---

### Task 5: CLI wiring + manifest

Tie the decoders together behind a CLI that converts the palette, one terrain
(PCK+MCD → atlas + `tiles-*.json`), and one unit set (PCK → atlas), and writes a
`manifest.json` of produced files.

**Files:**
- Modify: `unity/Xcom.Convert/Program.cs`
- Create: `unity/Xcom.Convert/ConvertJob.cs`
- Test: `unity/Tests.Standalone/Convert/ConvertJobTests.cs`

**Interfaces:**
- Consumes: `PaletteDecoder`, `PckDecoder`, `McdDecoder`, `AtlasWriter`.
- Produces:
  - `Xcom.Convert.ConvertJob.Run(string dataDir, string outDir) -> IReadOnlyList<string>`
    returning the list of written file paths (relative to `outDir`), also written as
    `manifest.json`.

- [ ] **Step 1: Write the failing test**

Create `unity/Tests.Standalone/Convert/ConvertJobTests.cs`:
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
        public void Run_ProducesPaletteTerrainAndUnitOutputs()
        {
            string outDir = Path.Combine(Path.GetTempPath(), "xcomconv-" + System.Guid.NewGuid());
            var written = ConvertJob.Run(DataDir, outDir);

            Assert.Contains(written, p => p.EndsWith("palettes.json"));
            Assert.Contains(written, p => p.Contains("terrain") && p.EndsWith(".png"));
            Assert.Contains(written, p => p.StartsWith("tiles-"));
            Assert.Contains(written, p => p.Contains("units") && p.EndsWith(".png"));
            Assert.True(File.Exists(Path.Combine(outDir, "manifest.json")));

            Directory.Delete(outDir, recursive: true);
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run:
```bash
cd unity/Tests.Standalone && dotnet test --filter ConvertJobTests
```
Expected: FAIL — `ConvertJob` undefined.

- [ ] **Step 3: Write `ConvertJob` and wire `Program`**

Create `unity/Xcom.Convert/ConvertJob.cs`:
```csharp
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
```

Replace `unity/Xcom.Convert/Program.cs` with:
```csharp
using System;

namespace Xcom.Convert
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            string dataDir = ArgValue(args, "--data") ?? "../RawData/Resources/UFO";
            string outDir = ArgValue(args, "--out") ?? "../Assets/GameData";
            var written = ConvertJob.Run(dataDir, outDir);
            Console.WriteLine($"Wrote {written.Count} files to {outDir}");
            return 0;
        }

        private static string? ArgValue(string[] args, string flag)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == flag) return args[i + 1];
            return null;
        }
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run:
```bash
cd unity/Tests.Standalone && dotnet test --filter ConvertJobTests
```
Expected: PASS. If a `TERRAIN/CULTIVAT.*` or `UNITS/XCOM_0.*` filename is wrong,
correct it (see the notes in Tasks 2 and 4) and re-run.

- [ ] **Step 5: Run the CLI end-to-end and eyeball the atlases**

Run:
```bash
cd unity/Xcom.Convert
dotnet run -- --data ../RawData/Resources/UFO --out ../Assets/GameData
```
Expected: `Wrote 7 files to ../Assets/GameData`. Open
`unity/Assets/GameData/units-XCOM_0.png` — it should show recognizable X-COM
soldier sprites on transparency. This is the first visual confirmation the pipeline
is faithful.

- [ ] **Step 6: Full suite green + commit**

Run:
```bash
cd unity/Tests.Standalone && dotnet test
```
Expected: all tests pass (combat + converter).
```bash
git add unity/Xcom.Convert/ConvertJob.cs unity/Xcom.Convert/Program.cs unity/Tests.Standalone/Convert/ConvertJobTests.cs
git commit -m "feat(convert): CLI converts palette + terrain + unit set with manifest"
```

---

## Phase 1 done — exit criteria

- `dotnet test` is green (combat scaffold + converter).
- `dotnet run` produces `Assets/GameData/` with `palettes.json`,
  `terrain-CULTIVAT.{png,frames.json}`, `tiles-CULTIVAT.json`, `units-XCOM_0.{png,frames.json}`,
  `manifest.json`.
- Opening `units-XCOM_0.png` shows real X-COM soldier sprites.

**Next:** Phase 2 (static map render) gets its own plan — it adds the MAP/RMP
mapblock decoder and the Unity iso renderer that draws `tiles-CULTIVAT.json` +
`terrain-CULTIVAT.png` into a pannable battlefield.
