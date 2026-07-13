# Phase 7 (Camera, HUD & Mouse Interaction) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Port OpenXcom/OXCE's Battlescape camera (edge/key scroll, center-on-unit,
no zoom), icon bar HUD, and tile-selector/path-preview mouse feedback onto the
Phase 6 skirmish scene, so it plays like a real Battlescape screen instead of a
debug harness.

**Architecture:** Two new pure-math C# classes (`CameraScroll`, `PathPreview`,
zero `UnityEngine` dependency, same pattern as `IsoProjection`) carry the
testable logic; `Xcom.Convert` gains 3 new sprite-atlas conversions
(`CURSOR.PCK`, `ICONS.PCK`, and the OXCE-bundled `Pathfinding.png` grid sheet);
`BattleController` gets a small refactor to expose `Selected`/`HoveredTile`/
`HoveredUnit` for the new views to read; 5 new MonoBehaviours
(`CameraController`, `HudBootstrap`, `IconBarView`, `SelectedUnitPanel`,
`TileCursorView`, `PathPreviewView`) do the Unity-side wiring, built live in
the connected Coplay MCP Unity Editor session the same way Phase 6 was.

**Tech Stack:** .NET 8, xUnit, Newtonsoft.Json, Unity 6000.4 uGUI
(`com.unity.ugui`, already installed — no TextMeshPro dependency added),
Coplay MCP tools.

## Global Constraints

- Builds on Phase 6 (merged to `oxce-plus`). Reuse exactly as-is, do not
  rename or re-declare: `BattleState`/`BattleUnit`/`BattleItem`/`Faction`/
  `BattleActionType` (`Core/Battle/BattleState.cs`, `BattleUnit.cs`,
  `Core/Rules/Enums.cs`), `Position`/`Directions`
  (`Core/Common/Position.cs`), `Pathfinding.FindPath`/`PathStep`
  (`Core/Battle/Pathfinding.cs` — already a pure, non-mutating query, reused
  directly for path preview, **no Core changes needed for path preview
  itself**), `IsoProjection`/`TileRenderer`/`UnitRenderer`/`AtlasLoader`
  (`Unity/Rendering/*.cs`), `BattlescapeMapView.Grid`, `BattlescapeBootstrap`,
  `PckDecoder.Load`/`AtlasWriter.Build`/`.Save` (`Xcom.Convert/Decoders`,
  `Xcom.Convert/Output`), `DataLoader.LoadArmors`/`LoadUnits`/`LoadItems`.
- `OpenXcom.Core` and `Xcom.Convert` must have zero `UnityEngine` references
  (unchanged rule). The two new pure-math classes this plan adds
  (`CameraScroll`, `PathPreview`) live under `Assets/Scripts/Unity/Rendering/`
  (matching where `IsoProjection` already lives — that folder is a
  `OpenXcom.Unity.Rendering` C# namespace, not a Unity-asset-only folder) and
  must also have zero `UnityEngine` reference, so they stay covered by
  `dotnet test` without the Editor.
- **A live Unity Editor instance is connected via the Coplay MCP plugin this
  session** (confirmed via `mcp__coplay-mcp__list_unity_project_roots`
  returning `unity`). Every Unity-side task (4 onward) must be verified live:
  `check_compile_errors` after every change, `play_game` + `get_unity_logs` +
  `capture_scene_object`/screenshot to confirm actual behavior. If re-run in a
  session with no Editor connected, fall back to writing the code to the
  existing standard of care and explicitly recording the verification gap
  (same fallback Phase 5/6 used).
- **Ground-truth constants, pinned from the C++ source this session — do not
  re-derive, do not guess different values:**
  - Edge-scroll trigger border: **5px** (`Camera::SCROLL_BORDER`, Camera.cpp).
  - Edge-scroll diagonal zone: **60px** (`Camera::SCROLL_DIAGONAL_EDGE`).
  - Scroll speed: **8** px per tick (`Options::battleScrollSpeed` default,
    Options.cpp:138).
  - Scroll tick interval: **15ms** (`Map::SCROLL_INTERVAL`, Map.h:63).
  - Edge-scroll mode: always on (`Options::battleEdgeScroll` = `SCROLL_AUTO`,
    the OXCE-specific default at Options.cpp:143, overriding vanilla OXC's
    `SCROLL_NONE` at line 140).
  - Drag-scroll: off by default (`Options::battleDragScrollButton` = `0`, the
    OXCE-specific default at Options.cpp:144) — not implemented this phase
    (see §"NOT included").
  - Pan keys: arrows (`keyBattleLeft/Right/Up/Down`, Options.cpp:318-321).
  - Center-on-unit key: **Home** (`keyBattleCenterUnit`, Options.cpp:324).
  - End-turn key: **Backspace** (`keyBattleEndTurn`, Options.cpp:333) — fixes
    `BattleController`'s current wrong `KeyCode.Space` binding.
  - No zoom — the original Battlescape has none.
  - Icon bar size: **320×56px** (`interfaces.rul` battlescape `icons` element,
    `bin/standard/xcom1/interfaces.rul`), positioned bottom-center of the
    screen; the map viewport is whatever screen height remains above it.
  - Icon bar button rects (all relative to the icon bar's own top-left
    corner, i.e. strip the `x +`/`y +` from `BattlescapeState.cpp`'s
    constructors — these are the literal hardcoded original coordinates, not
    ruleset-driven):
    | Button | Rect (x,y,w,h) |
    |---|---|
    | UnitUp | 48,0,32,16 |
    | UnitDown | 48,16,32,16 |
    | MapUp | 80,0,32,16 |
    | MapDown | 80,16,32,16 |
    | ShowMap | 112,0,32,16 |
    | Kneel | 112,16,32,16 |
    | Inventory | 144,0,32,16 |
    | Center | 144,16,32,16 |
    | NextSoldier | 176,0,32,16 |
    | PrevSoldier (NextStop) | 176,16,32,16 |
    | ShowLayers | 208,0,32,16 |
    | Help | 208,16,32,16 |
    | EndTurn | 240,0,32,16 |
    | Abort | 240,16,32,16 |
    | Stats panel (rank icon + name/TU/health block) | 107,33,164,23 |
    | Name text | 135,32,136,10 |
    | TU number | 136,42,15,5 | TU bar | 170,41,102,3 |
    | Energy number | 154,42,15,5 | Energy bar | 170,45,102,3 |
    | Health number | 136,50,15,5 | Health bar | 170,49,102,3 |
    (Morale intentionally omitted — see §"NOT included".)
  - `CURSOR.PCK` decodes at **32×40** (`Mod.cpp:5718`, same frame size
    `PckDecoder.Load` already uses for unit sprites — no new decode path).
    Tile-selector cursor (move mode, `CT_NORMAL`) is frames **0 and 1**,
    flashing (`Map.cpp:1560`'s `frame[CT_NORMAL=1] = 0`, animated by
    `+ (_animFrame/4)%2`). `_animFrame` increments once per
    `DEFAULT_ANIM_SPEED = 100ms` tick (`BattlescapeState.h:118`), so the
    2-frame flash toggles every **400ms** (0.4s).
  - `ICONS.PCK` has **no companion `.TAB`** (confirmed: no `ICONS.TAB` file
    exists) → `PckDecoder.Load` takes its existing `nframes = 1` branch when
    passed an empty `tab` array; decode at 320×56.
  - Path-preview arrows are **not** from `CURSOR.PCK` — they're OXCE's own
    bundled sheet, `bin/common/Resources/Pathfinding/Pathfinding.png` /
    `unity/RawData/Resources/common/Resources/Pathfinding/Pathfinding.png`
    (confirmed present, 384×80px, already a plain RGBA PNG — not an indexed
    `.PCK`, no palette needed), declared in `extraSprites.rul` as a 12-column
    grid of 32×40 frames (2 rows × 12 cols = 24 frames). Frame index = compass
    direction (0=N..7=NW, matching `Core.Common.Directions.Offsets`'
    ordering) for the base row; **this phase's simplified port uses only the
    base 8 direction frames (0-7), tinted red or yellow by cumulative TU
    affordability**, skipping the original's separate neutral-tint base pass
    + `+12`-offset colored overlay pass (`Map.cpp:1288-1301` and
    `:1635-1640`) — a deliberate scope simplification, not a mistake; see
    §"NOT included".
  - `Xcom.Convert`'s raw data lives under **two** sibling roots this phase:
    the existing `dataDir` (`unity/RawData/Resources/UFO`, used for
    `CURSOR.PCK`/`ICONS.PCK`) and a new `commonDir`
    (`unity/RawData/Resources/common`, used for `Pathfinding.png`) — mirrors
    how Phase 6 added a second root (`rulesDir`) for `bin/standard/xcom1`.
  - No `UnityEngine.UI.Text` font asset needed: use Unity's built-in
    `Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")`.
  - Environment: `export PATH="$HOME/.dotnet:$PATH"` before any `dotnet`
    command, run from `unity/Tests.Standalone` (tests) or `unity/` (convert).

---

### Task 1: `Xcom.Convert` — `GridSpriteSheet` + CURSOR/ICONS/Pathfinding conversions

**Files:**
- Create: `unity/Xcom.Convert/Output/GridSpriteSheet.cs`
- Modify: `unity/Xcom.Convert/ConvertJob.cs`
- Modify: `unity/Xcom.Convert/Program.cs`
- Modify: `unity/Tests.Standalone/TestPaths.cs`
- Modify: `unity/Tests.Standalone/Convert/ConvertJobTests.cs`
- Test: `unity/Tests.Standalone/Convert/GridSpriteSheetTests.cs` (new file)

**Interfaces:**
- Consumes: existing `PckDecoder.Load`, `AtlasWriter.Build`/`.Save`,
  `PaletteDecoder.Load` (unchanged).
- Produces (used by Tasks 6-8, via JSON/PNG on disk, not by reference):
  `cursor.png`/`cursor.frames.json` (17 frames, 32×40 — same schema
  `AtlasLoader.Load` already reads), `icons.png`/`icons.frames.json` (1 frame,
  320×56), `pathfinding.png`/`pathfinding.frames.json` (24 frames, 32×40).
  Also: `GridSpriteSheet.Convert(string srcPngPath, string outPngPath, string
  outFramesJsonPath, int frameWidth, int frameHeight, int columns, int rows)`.
  Also: `ConvertJob.Run`'s new signature
  `Run(string dataDir, string rulesDir, string commonDir, string outDir)`.

- [ ] **Step 1: Write the failing `GridSpriteSheet` test**

Create `unity/Tests.Standalone/Convert/GridSpriteSheetTests.cs`:

```csharp
using System.IO;
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
```

- [ ] **Step 2: Add `TestPaths.CommonDir`**

In `unity/Tests.Standalone/TestPaths.cs`, add alongside `RulesDir`:

```csharp
        public static readonly string CommonDir = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "RawData", "Resources", "common");
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test --filter "FullyQualifiedName~GridSpriteSheetTests"`
Expected: FAIL to build — `GridSpriteSheet` does not exist yet.

- [ ] **Step 4: Write `GridSpriteSheet`**

Create `unity/Xcom.Convert/Output/GridSpriteSheet.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Xcom.Convert.Output
{
    /// <summary>
    /// Converts a sprite sheet that's already a plain RGBA PNG laid out as a
    /// fixed grid of equal-size frames (row-major) — e.g. OXCE's bundled
    /// Resources/Pathfinding/Pathfinding.png — into the same
    /// (png, frames.json) pair AtlasWriter.Save produces for palette-decoded
    /// .PCK sprites, so AtlasLoader.Load can read either kind identically.
    /// No palette or PckDecoder involved: the source is already true-color,
    /// so this only copies bytes and computes frame rects.
    /// </summary>
    public static class GridSpriteSheet
    {
        public static void Convert(string srcPngPath, string outPngPath, string outFramesJsonPath,
            int frameWidth, int frameHeight, int columns, int rows)
        {
            string? outPngDir = Path.GetDirectoryName(outPngPath);
            if (!string.IsNullOrEmpty(outPngDir))
                Directory.CreateDirectory(outPngDir);
            File.Copy(srcPngPath, outPngPath, overwrite: true);

            var frames = new List<AtlasFrame>(columns * rows);
            for (int i = 0; i < columns * rows; i++)
            {
                int cx = (i % columns) * frameWidth;
                int cy = (i / columns) * frameHeight;
                frames.Add(new AtlasFrame { X = cx, Y = cy, W = frameWidth, H = frameHeight });
            }

            string? jsonDir = Path.GetDirectoryName(outFramesJsonPath);
            if (!string.IsNullOrEmpty(jsonDir))
                Directory.CreateDirectory(jsonDir);
            File.WriteAllText(outFramesJsonPath,
                JsonConvert.SerializeObject(new { frames }, Formatting.Indented));
        }
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test --filter "FullyQualifiedName~GridSpriteSheetTests"`
Expected: PASS (1/1).

- [ ] **Step 6: Update `ConvertJobTests` for the 3 new conversions**

Replace the whole `unity/Tests.Standalone/Convert/ConvertJobTests.cs` file — same as
the existing file, except: the call to `ConvertJob.Run` gains a `commonDir` argument,
6 new `Assert.Contains` lines are added, and the total-count assertions change from
20 to 26:

```csharp
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
            Assert.Contains(written, p => p == "items.json");
            Assert.Contains(written, p => p == "cursor.png");
            Assert.Contains(written, p => p == "cursor.frames.json");
            Assert.Contains(written, p => p == "icons.png");
            Assert.Contains(written, p => p == "icons.frames.json");
            Assert.Contains(written, p => p == "pathfinding.png");
            Assert.Contains(written, p => p == "pathfinding.frames.json");
            Assert.True(File.Exists(Path.Combine(outDir, "manifest.json")));

            Assert.Equal(26, written.Count);
            var manifestJson = File.ReadAllText(Path.Combine(outDir, "manifest.json"));
            var manifest = Newtonsoft.Json.Linq.JObject.Parse(manifestJson);
            var files = manifest["files"].Select(t => t.ToString()).ToList();
            Assert.Equal(26, files.Count);

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

            // armors.json: Sectoid armor only, this slice.
            var armorsJson = File.ReadAllText(Path.Combine(outDir, "armors.json"));
            var armors = Newtonsoft.Json.Linq.JArray.Parse(armorsJson);
            Assert.Single(armors);
            Assert.Equal("SECTOID_ARMOR0", armors[0]["Id"].ToString());
            Assert.Equal(4, (int)armors[0]["Front"]);

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
```

- [ ] **Step 7: Run test to verify it fails**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test --filter "FullyQualifiedName~ConvertJobTests"`
Expected: FAIL to build — `ConvertJob.Run` doesn't accept a `commonDir` argument yet.

- [ ] **Step 8: Update `ConvertJob.Run`**

In `unity/Xcom.Convert/ConvertJob.cs`, add `using Xcom.Convert.Output;` if not
already present (it is, via the existing `AtlasWriter` using), change the
signature, and insert 3 new conversions right after the existing step 3b
(Sectoid sprite) and before step 4 (mapblock):

```csharp
        public static IReadOnlyList<string> Run(string dataDir, string rulesDir, string commonDir, string outDir)
```

Insert after the existing `written.Add("units-SECTOID.frames.json");` line:

```csharp

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

            // 3d. ICONS.PCK: icon bar background, single 320x56 frame, no .TAB.
            var iconsFrames = PckDecoder.Load(
                File.ReadAllBytes(Path.Combine(dataDir, "UFOGRAPH", "ICONS.PCK")),
                System.Array.Empty<byte>(), 320, 56);
            var iconsAtlas = AtlasWriter.Build(iconsFrames, pal);
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
```

- [ ] **Step 9: Update `Program.cs` for the new `--common` argument**

Replace `unity/Xcom.Convert/Program.cs`:

```csharp
using System;

namespace Xcom.Convert
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            string dataDir = ArgValue(args, "--data") ?? "../RawData/Resources/UFO";
            string rulesDir = ArgValue(args, "--rules") ?? "../../bin/standard/xcom1";
            string commonDir = ArgValue(args, "--common") ?? "../RawData/Resources/common";
            string outDir = ArgValue(args, "--out") ?? "../Assets/GameData";
            var written = ConvertJob.Run(dataDir, rulesDir, commonDir, outDir);
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

- [ ] **Step 10: Run tests to verify they pass**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test --filter "FullyQualifiedName~ConvertJobTests"`
Expected: PASS (1/1).

- [ ] **Step 11: Run the full test suite (regression check)**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test`
Expected: all tests pass.

- [ ] **Step 12: Regenerate real `GameData` for the Unity-side tasks**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity && dotnet run --project Xcom.Convert`
Expected: `Wrote 26 files to ../Assets/GameData` (confirms `cursor.png`,
`icons.png`, `pathfinding.png` and their `.frames.json` now exist under
`unity/Assets/GameData/` for Tasks 6-8 to load at runtime).

- [ ] **Step 13: Commit**

```bash
git add unity/Xcom.Convert/Output/GridSpriteSheet.cs unity/Xcom.Convert/ConvertJob.cs \
  unity/Xcom.Convert/Program.cs unity/Tests.Standalone/TestPaths.cs \
  unity/Tests.Standalone/Convert/ConvertJobTests.cs unity/Tests.Standalone/Convert/GridSpriteSheetTests.cs
git commit -m "feat(convert): convert CURSOR.PCK, ICONS.PCK, and the Pathfinding arrow sheet"
```

---

### Task 2: `OpenXcom.Unity.Rendering` — `CameraScroll` (pure math, testable)

This task is pure C# with no `UnityEngine` reference (same as `IsoProjection`),
fully verifiable by `dotnet test` — no Editor needed.

**Files:**
- Create: `unity/Assets/Scripts/Unity/Rendering/CameraScroll.cs`
- Test: `unity/Tests.Standalone/Unity/CameraScrollTests.cs` (new file)

**Interfaces:**
- Consumes: nothing from other tasks.
- Produces (used by Task 5): `OpenXcom.Unity.Rendering.CameraScroll.EdgeScrollDirection(int mouseX, int mouseY, int viewportWidth, int viewportHeight, int scrollSpeed) -> (int dx, int dy)`,
  `.UnitsPerSecond(float scrollSpeedPxPerTick, float scrollIntervalMs, float pixelsPerUnit) -> float`.

- [ ] **Step 1: Write the failing tests**

Create `unity/Tests.Standalone/Unity/CameraScrollTests.cs`:

```csharp
using OpenXcom.Unity.Rendering;
using Xunit;

namespace OpenXcom.Core.Tests.Unity
{
    public class CameraScrollTests
    {
        // Viewport 300x200 throughout - big enough that the 60px diagonal
        // zone and 5px border zone don't overlap in the "no scroll" cases.
        private const int ViewportWidth = 300;
        private const int ViewportHeight = 200;
        private const int ScrollSpeed = 8;

        [Fact]
        public void EdgeScrollDirection_MouseInDeadZone_NoScroll()
        {
            var (dx, dy) = CameraScroll.EdgeScrollDirection(150, 100, ViewportWidth, ViewportHeight, ScrollSpeed);
            Assert.Equal(0, dx);
            Assert.Equal(0, dy);
        }

        [Fact]
        public void EdgeScrollDirection_ExactlyAtBorder_NoScroll()
        {
            // Camera::mouseOver's checks are strict "<"/">" against SCROLL_BORDER,
            // so a mouse position exactly AT the border (5) does not trigger.
            var (dx, dy) = CameraScroll.EdgeScrollDirection(5, 100, ViewportWidth, ViewportHeight, ScrollSpeed);
            Assert.Equal(0, dx);
            Assert.Equal(0, dy);
        }

        [Fact]
        public void EdgeScrollDirection_LeftEdgeVerticallyCentered_ScrollsRightOnly()
        {
            var (dx, dy) = CameraScroll.EdgeScrollDirection(2, 100, ViewportWidth, ViewportHeight, ScrollSpeed);
            Assert.Equal(ScrollSpeed, dx);
            Assert.Equal(0, dy);
        }

        [Fact]
        public void EdgeScrollDirection_RightEdgeVerticallyCentered_ScrollsLeftOnly()
        {
            var (dx, dy) = CameraScroll.EdgeScrollDirection(298, 100, ViewportWidth, ViewportHeight, ScrollSpeed);
            Assert.Equal(-ScrollSpeed, dx);
            Assert.Equal(0, dy);
        }

        [Fact]
        public void EdgeScrollDirection_TopEdgeHorizontallyCentered_ScrollsUpOnly()
        {
            var (dx, dy) = CameraScroll.EdgeScrollDirection(150, 2, ViewportWidth, ViewportHeight, ScrollSpeed);
            Assert.Equal(0, dx);
            Assert.Equal(ScrollSpeed, dy);
        }

        [Fact]
        public void EdgeScrollDirection_BottomEdgeHorizontallyCentered_ScrollsDownOnly()
        {
            var (dx, dy) = CameraScroll.EdgeScrollDirection(150, 198, ViewportWidth, ViewportHeight, ScrollSpeed);
            Assert.Equal(0, dx);
            Assert.Equal(-ScrollSpeed, dy);
        }

        [Fact]
        public void EdgeScrollDirection_LeftEdgeNearTop_ScrollsDiagonallyAtHalfSpeed()
        {
            // mouseY=30 is inside the 60px diagonal zone but not inside the 5px
            // top border, so only the X-branch's diagonal halving applies.
            var (dx, dy) = CameraScroll.EdgeScrollDirection(2, 30, ViewportWidth, ViewportHeight, ScrollSpeed);
            Assert.Equal(ScrollSpeed, dx);
            Assert.Equal(ScrollSpeed / 2, dy);
        }

        [Fact]
        public void EdgeScrollDirection_TopLeftCorner_ScrollsDiagonallyAtHalfSpeedBothWays()
        {
            // Both border zones trigger: X-branch sets (8,4), then the
            // Y-branch overrides dy=8 then halves it back to 4 and keeps dx=8
            // (Camera::mouseOver's up-branch's "upleft" case).
            var (dx, dy) = CameraScroll.EdgeScrollDirection(2, 2, ViewportWidth, ViewportHeight, ScrollSpeed);
            Assert.Equal(ScrollSpeed, dx);
            Assert.Equal(ScrollSpeed / 2, dy);
        }

        [Fact]
        public void UnitsPerSecond_MatchesScrollSpeedOverIntervalOverPixelsPerUnit()
        {
            // 8px / 15ms tick = 533.33 px/s; / 32 px-per-unit = 16.67 units/s.
            float result = CameraScroll.UnitsPerSecond(8f, 15f, 32f);
            Assert.Equal(16.6667f, result, 3);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test --filter "FullyQualifiedName~CameraScrollTests"`
Expected: FAIL to build — `CameraScroll` does not exist yet.

- [ ] **Step 3: Write `CameraScroll`**

Create `unity/Assets/Scripts/Unity/Rendering/CameraScroll.cs`:

```csharp
namespace OpenXcom.Unity.Rendering
{
    /// <summary>
    /// Pure-math port of Camera::mouseOver's edge-scroll direction/diagonal-
    /// blend logic (src/Battlescape/Camera.cpp:119-224), using OXCE's default
    /// SCROLL_AUTO mode (always-on edge scroll, no click-drag trigger needed
    /// - Options::battleEdgeScroll's OXCE default, Options.cpp:143).
    /// Intentionally stateless: the original accumulates _scrollMouseX/Y
    /// across calls, but a MonoBehaviour can just call this fresh every
    /// Update() with the current mouse position, since the original's own
    /// per-call logic already fully recomputes the scroll vector from
    /// scratch (the only cross-call state it has - the "else if (posX) reset
    /// to 0" branches - exists purely to leave a value untouched when this
    /// frame's position is exactly 0, which a fresh stateless call already
    /// defaults to 0 for anyway).
    /// </summary>
    public static class CameraScroll
    {
        public const int ScrollBorder = 5;
        public const int ScrollDiagonalEdge = 60;

        /// <summary>
        /// mouseX/mouseY and viewportWidth/viewportHeight must all be in the
        /// same coordinate space: the MAP VIEWPORT only (i.e. already
        /// excluding the icon bar) with (0,0) at the viewport's top-left -
        /// matches Camera.cpp's own _screenWidth/_screenHeight, which are the
        /// Map surface's own dimensions, not the full screen
        /// (Camera.cpp:41-42: "_screenWidth(map->getWidth())").
        /// </summary>
        public static (int dx, int dy) EdgeScrollDirection(
            int mouseX, int mouseY, int viewportWidth, int viewportHeight, int scrollSpeed)
        {
            int dx = 0, dy = 0;

            // Left / right (Camera::mouseOver's first if/else-if block).
            if (mouseX < ScrollBorder && mouseX >= 0)
            {
                dx = scrollSpeed;
                if (mouseY < ScrollDiagonalEdge && mouseY >= 0) dy = scrollSpeed / 2;
                else if (mouseY > viewportHeight - ScrollDiagonalEdge) dy = -scrollSpeed / 2;
            }
            else if (mouseX > viewportWidth - ScrollBorder)
            {
                dx = -scrollSpeed;
                if (mouseY <= ScrollDiagonalEdge && mouseY >= 0) dy = scrollSpeed / 2;
                else if (mouseY > viewportHeight - ScrollDiagonalEdge) dy = -scrollSpeed / 2;
            }

            // Up / down (Camera::mouseOver's second if/else-if block - can
            // override dx/dy set above, exactly like the original).
            if (mouseY < ScrollBorder && mouseY >= 0)
            {
                dy = scrollSpeed;
                if (mouseX < ScrollDiagonalEdge && mouseX >= 0) { dx = scrollSpeed; dy /= 2; }
                else if (mouseX > viewportWidth - ScrollDiagonalEdge) { dx = -scrollSpeed; dy /= 2; }
            }
            else if (mouseY > viewportHeight - ScrollBorder)
            {
                dy = -scrollSpeed;
                if (mouseX < ScrollDiagonalEdge && mouseX >= 0) { dx = scrollSpeed; dy /= 2; }
                else if (mouseX > viewportWidth - ScrollDiagonalEdge) { dx = -scrollSpeed; dy /= 2; }
            }

            return (dx, dy);
        }

        /// <summary>
        /// Converts the original's px-per-tick scroll speed into world
        /// units per second for a MonoBehaviour driven by Time.deltaTime
        /// instead of a 15ms Timer (Map::SCROLL_INTERVAL, Map.h:63).
        /// </summary>
        public static float UnitsPerSecond(float scrollSpeedPxPerTick, float scrollIntervalMs, float pixelsPerUnit)
        {
            float pxPerSecond = scrollSpeedPxPerTick / (scrollIntervalMs / 1000f);
            return pxPerSecond / pixelsPerUnit;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test --filter "FullyQualifiedName~CameraScrollTests"`
Expected: PASS (9/9).

- [ ] **Step 5: Run the full test suite (regression check)**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test`
Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add unity/Assets/Scripts/Unity/Rendering/CameraScroll.cs unity/Tests.Standalone/Unity/CameraScrollTests.cs
git commit -m "feat(unity): CameraScroll - pure-math port of Camera::mouseOver's edge-scroll logic"
```

---

### Task 3: `Core.Common.Directions.IndexOf` + `OpenXcom.Unity.Rendering.PathPreview` (pure math, testable)

Also pure C#, no `UnityEngine` reference, verifiable by `dotnet test`.

**Files:**
- Modify: `unity/Assets/Scripts/Core/Common/Position.cs`
- Create: `unity/Assets/Scripts/Unity/Rendering/PathPreview.cs`
- Test: `unity/Tests.Standalone/DirectionsIndexOfTests.cs` (new file)
- Test: `unity/Tests.Standalone/Unity/PathPreviewTests.cs` (new file)

**Interfaces:**
- Consumes: `Core.Battle.PathStep` (existing, `Battle/Pathfinding.cs`),
  `Core.Common.Position`/`Directions.Offsets` (existing).
- Produces (used by Task 8): `Directions.IndexOf(Position delta) -> int` (-1
  if not one of the 8 compass offsets), `PathPreview.Affordability` enum
  (`Affordable`, `Unaffordable`), `PathPreview.ComputeAffordability(
  IReadOnlyList<PathStep> path, int availableTu) -> Affordability[]`.

- [ ] **Step 1: Write the failing `Directions.IndexOf` test**

Create `unity/Tests.Standalone/DirectionsIndexOfTests.cs`:

```csharp
using OpenXcom.Core.Common;
using Xunit;

namespace OpenXcom.Core.Tests
{
    public class DirectionsIndexOfTests
    {
        [Theory]
        [InlineData(0, -1, 0)]  // N
        [InlineData(1, -1, 1)]  // NE
        [InlineData(1, 0, 2)]   // E
        [InlineData(1, 1, 3)]   // SE
        [InlineData(0, 1, 4)]   // S
        [InlineData(-1, 1, 5)]  // SW
        [InlineData(-1, 0, 6)]  // W
        [InlineData(-1, -1, 7)] // NW
        public void IndexOf_MatchesOffsetsArrayIndex(int dx, int dy, int expectedIndex)
        {
            Assert.Equal(expectedIndex, Directions.IndexOf(new Position(dx, dy)));
        }

        [Fact]
        public void IndexOf_NonCompassDelta_ReturnsMinusOne()
        {
            Assert.Equal(-1, Directions.IndexOf(new Position(2, 2)));
        }
    }
}
```

- [ ] **Step 2: Write the failing `PathPreview` test**

Create `unity/Tests.Standalone/Unity/PathPreviewTests.cs`:

```csharp
using System.Collections.Generic;
using OpenXcom.Core.Battle;
using OpenXcom.Core.Common;
using OpenXcom.Unity.Rendering;
using Xunit;

namespace OpenXcom.Core.Tests.Unity
{
    public class PathPreviewTests
    {
        private static List<PathStep> Path3StepsCosting4Each() => new()
        {
            new PathStep { Position = new Position(1, 0, 0), StepCost = 4 },
            new PathStep { Position = new Position(2, 0, 0), StepCost = 4 },
            new PathStep { Position = new Position(3, 0, 0), StepCost = 4 },
        };

        [Fact]
        public void ComputeAffordability_EnoughTuForWholePath_AllAffordable()
        {
            var result = PathPreview.ComputeAffordability(Path3StepsCosting4Each(), availableTu: 12);
            Assert.All(result, a => Assert.Equal(PathPreview.Affordability.Affordable, a));
        }

        [Fact]
        public void ComputeAffordability_TuRunsOutPartway_MarksTheRestUnaffordable()
        {
            // Cumulative costs: 4, 8, 12. With 10 TU: step 1&2 affordable (4,8 <= 10),
            // step 3 (12) is not.
            var result = PathPreview.ComputeAffordability(Path3StepsCosting4Each(), availableTu: 10);
            Assert.Equal(PathPreview.Affordability.Affordable, result[0]);
            Assert.Equal(PathPreview.Affordability.Affordable, result[1]);
            Assert.Equal(PathPreview.Affordability.Unaffordable, result[2]);
        }

        [Fact]
        public void ComputeAffordability_ZeroTu_FirstStepAlreadyUnaffordable()
        {
            var result = PathPreview.ComputeAffordability(Path3StepsCosting4Each(), availableTu: 0);
            Assert.Equal(PathPreview.Affordability.Unaffordable, result[0]);
        }

        [Fact]
        public void ComputeAffordability_EmptyPath_ReturnsEmptyArray()
        {
            var result = PathPreview.ComputeAffordability(new List<PathStep>(), availableTu: 10);
            Assert.Empty(result);
        }
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test --filter "FullyQualifiedName~DirectionsIndexOfTests|FullyQualifiedName~PathPreviewTests"`
Expected: FAIL to build — `Directions.IndexOf` and `PathPreview` don't exist yet.

- [ ] **Step 4: Add `Directions.IndexOf`**

In `unity/Assets/Scripts/Core/Common/Position.cs`, add this method inside the
existing `Directions` static class, alongside `IsDiagonal`:

```csharp
        /// <summary>Reverse lookup of Offsets: which of the 8 compass indices
        /// this delta is, or -1 if it isn't one of them. Used by path-preview
        /// rendering (Phase 7) to pick an arrow frame from a PathStep-to-
        /// PathStep delta.</summary>
        public static int IndexOf(Position delta)
        {
            for (int i = 0; i < Offsets.Length; i++)
                if (Offsets[i] == delta)
                    return i;
            return -1;
        }
```

- [ ] **Step 5: Write `PathPreview`**

Create `unity/Assets/Scripts/Unity/Rendering/PathPreview.cs`:

```csharp
using System.Collections.Generic;
using OpenXcom.Core.Battle;

namespace OpenXcom.Unity.Rendering
{
    /// <summary>
    /// TU-cost affordability per step of a previewed path, for path-preview
    /// arrow tinting. [SIMPLIFIED] port of the affordability half of
    /// Tile::getMarkerColor's role in Pathfinding::previewPath
    /// (src/Battlescape/Pathfinding.cpp, Tile.cpp) - the original also colors
    /// a 3rd state (green, for an already-selected destination) and draws a
    /// separate neutral-tint base layer under the colored overlay
    /// (Map.cpp:1288-1301, :1635-1640); this phase's port only needs the
    /// yellow/red distinction the design calls for, applied directly to a
    /// single arrow sprite per step rather than two layered passes.
    /// </summary>
    public static class PathPreview
    {
        public enum Affordability { Affordable, Unaffordable }

        /// <summary>
        /// One Affordability per element of `path`, in path order. Each
        /// step's affordability is based on the CUMULATIVE TU cost to reach
        /// it (a step past the point where TU runs out is Unaffordable even
        /// if its own StepCost alone would fit).
        /// </summary>
        public static Affordability[] ComputeAffordability(IReadOnlyList<PathStep> path, int availableTu)
        {
            var result = new Affordability[path.Count];
            int cumulative = 0;
            for (int i = 0; i < path.Count; i++)
            {
                cumulative += path[i].StepCost;
                result[i] = cumulative <= availableTu ? Affordability.Affordable : Affordability.Unaffordable;
            }
            return result;
        }
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test --filter "FullyQualifiedName~DirectionsIndexOfTests|FullyQualifiedName~PathPreviewTests"`
Expected: PASS (13/13 — 9 `DirectionsIndexOfTests` + 4 `PathPreviewTests`).

- [ ] **Step 7: Run the full test suite (regression check)**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test`
Expected: all tests pass.

- [ ] **Step 8: Commit**

```bash
git add unity/Assets/Scripts/Core/Common/Position.cs unity/Assets/Scripts/Unity/Rendering/PathPreview.cs \
  unity/Tests.Standalone/DirectionsIndexOfTests.cs unity/Tests.Standalone/Unity/PathPreviewTests.cs
git commit -m "feat(core,unity): Directions.IndexOf + PathPreview TU-affordability helper"
```

---

### Task 4: `BattleController` — expose `Selected`/`HoveredTile`/`HoveredUnit`, fix End Turn key

**This task requires a live Unity Editor.** Verify via
`mcp__coplay-mcp__check_compile_errors` after writing the code, then
`play_game` to confirm select/move/fire/end-turn still all work exactly as in
Phase 6 (this is a refactor, not new gameplay behavior) and that Backspace
(not Space) ends the turn.

**Files:**
- Modify: `unity/Assets/Scripts/Unity/BattleController.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces (used by Tasks 5, 7, 8): `BattleController.Selected -> BattleUnit`
  (the currently-selected unit, or null), `BattleController.HoveredTile ->
  Position?` (the tile under the mouse this frame, null if the cursor isn't
  over a tile), `BattleController.HoveredUnit -> BattleUnit` (the unit under
  the mouse this frame, or null).

- [ ] **Step 1: Replace `BattleController.cs`**

Replace the whole file — a refactor that extracts the existing per-click
raycasts into one per-frame hover pass (reused by both click handling and the
new views), fixes the End Turn key, and updates the stale header comment
(Phase 6's "cannot be compiled... has not been visually verified" note is no
longer true — Task 7/8 of the Phase 6 plan verified this class live):

```csharp
using System.Collections.Generic;
using OpenXcom.Core.Battle;
using OpenXcom.Core.Common;
using OpenXcom.Core.Rules;
using OpenXcom.Unity.Rendering;
using UnityEngine;

namespace OpenXcom.Unity
{
    /// <summary>
    /// Click a unit's GameObject to select it; click a tile to path there.
    /// Pure input + animation glue: all move legality/TU accounting lives in
    /// OpenXcom.Core.Battle.BattleState.TryMove - this class only translates
    /// mouse clicks into calls on it and drains/animates the resulting
    /// BattleEvents. Makes no gameplay decisions of its own.
    /// </summary>
    public sealed class BattleController : MonoBehaviour
    {
        [SerializeField] private Camera raycastCamera;
        [SerializeField] private float tilesPerSecond = 4f;

        private BattleState _state;
        private BattleUnit _selected;
        private readonly Dictionary<BattleUnit, Transform> _unitTransforms = new();

        /// <summary>The currently-selected unit, or null. Read by Phase 7's HUD/cursor views.</summary>
        public BattleUnit Selected => _selected;

        /// <summary>The tile under the mouse this frame, or null (cursor over
        /// the icon bar, over a unit instead, or off the map). Recomputed
        /// every Update() by UpdateHover - read by Phase 7's
        /// TileCursorView/PathPreviewView.</summary>
        public Position? HoveredTile { get; private set; }

        /// <summary>The unit under the mouse this frame, or null. Recomputed
        /// every Update() by UpdateHover.</summary>
        public BattleUnit HoveredUnit { get; private set; }

        /// <summary>One unit's in-progress walk animation. A turn can move
        /// several units (e.g. the AI's hostile turn), so this is a list, not
        /// a single slot - a single-slot design silently clobbers all but the
        /// last unit's animation whenever more than one UnitMovedEvent is
        /// drained in the same call.</summary>
        private sealed class UnitAnimation
        {
            public Transform Transform;
            public Queue<Position> Queue;
            public Vector3 Target;
        }

        private readonly List<UnitAnimation> _activeAnimations = new();

        /// <summary>Wires this controller to an already-populated battle. Called by whichever scene bootstrap owns squad setup.</summary>
        public void Bind(BattleState state, IReadOnlyDictionary<BattleUnit, Transform> unitTransforms)
        {
            _state = state;
            _unitTransforms.Clear();
            foreach (var kv in unitTransforms)
                _unitTransforms[kv.Key] = kv.Value;
        }

        private void Update()
        {
            UpdateHover();

            if (_activeAnimations.Count > 0)
            {
                AdvanceAnimations();
                return; // don't accept new input while any unit is mid-animation
            }

            if (_state == null)
                return;

            if (_state.IsBattleOver)
                return; // no further input once the battle is decided

            // keyBattleEndTurn's OXCE default (Options.cpp:333) - NOT Space.
            if (Input.GetKeyDown(KeyCode.Backspace))
            {
                _state.EndPlayerTurn();
                DrainAndAnimate();
                return;
            }

            if (Input.GetMouseButtonDown(1)) // right-click: fire at a targeted unit
            {
                HandleFireClick();
                return;
            }

            if (!Input.GetMouseButtonDown(0)) // left-click: select / move
                return;

            if (HoveredUnit != null)
            {
                _selected = HoveredUnit;
                return;
            }

            if (_selected == null || HoveredTile == null)
                return;

            var result = _state.TryMove(_selected, HoveredTile.Value);
            if (result.Outcome == MoveOutcome.Failed)
                return;

            DrainAndAnimate();
        }

        /// <summary>
        /// One raycast per frame from the current mouse position, resolving
        /// HoveredUnit/HoveredTile - replaces the old per-click raycasts in
        /// Update/HandleFireClick, so both click handling and Phase 7's
        /// hover-driven views (tile cursor, path preview) share one hit test
        /// instead of raycasting twice per frame.
        /// </summary>
        private void UpdateHover()
        {
            HoveredUnit = null;
            HoveredTile = null;

            if (raycastCamera == null)
                return;

            var ray = raycastCamera.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out var hit))
                return;

            HoveredUnit = FindUnitAt(hit.transform);
            if (HoveredUnit == null)
                HoveredTile = TileUnderCursor(hit);
        }

        private void HandleFireClick()
        {
            if (_selected == null || _selected.RightHand == null)
                return;

            if (HoveredUnit == null || HoveredUnit == _selected)
                return;

            _state.TryFire(_selected, _selected.RightHand, BattleActionType.AimedShot, HoveredUnit);
            DrainAndAnimate();
        }

        private BattleUnit FindUnitAt(Transform hitTransform)
        {
            foreach (var kv in _unitTransforms)
                if (kv.Value == hitTransform)
                    return kv.Key;
            return null;
        }

        private Position TileUnderCursor(RaycastHit hit)
        {
            // Inverse of IsoProjection.MapToScreen; left as a direct pixel/world
            // lookup against the hit tile's own TileRenderer name ("Tile_x_y_z").
            // Reads hit.transform directly, NOT .parent: TileRenderer's
            // BoxCollider sits on the Tile_x_y_z GameObject itself.
            var parts = hit.transform.name.Split('_');
            return new Position(int.Parse(parts[1]), int.Parse(parts[2]), int.Parse(parts[3]));
        }

        private void DrainAndAnimate()
        {
            foreach (var evt in _state.DequeueEvents())
            {
                if (evt is UnitMovedEvent moved && _unitTransforms.TryGetValue(moved.Unit, out var t))
                {
                    _activeAnimations.Add(new UnitAnimation
                    {
                        Transform = t,
                        Queue = new Queue<Position>(moved.Path),
                        Target = t.localPosition,
                    });
                }
                else if (evt is ProjectileFiredEvent fired)
                {
                    Debug.Log(fired.Hit
                        ? $"{fired.Attacker.Name} hits {fired.Defender.Name}"
                        : $"{fired.Attacker.Name} misses {fired.Defender.Name}");
                }
                else if (evt is UnitHitEvent hitEvent)
                {
                    Debug.Log($"{hitEvent.Unit.Name} takes {hitEvent.Damage} damage ({hitEvent.Side})");
                }
                else if (evt is UnitDiedEvent died && _unitTransforms.TryGetValue(died.Unit, out var deadTransform))
                {
                    deadTransform.gameObject.SetActive(false);
                    _unitTransforms.Remove(died.Unit);
                    if (_selected == died.Unit)
                        _selected = null;
                }
                else if (evt is TurnChangedEvent turnChanged)
                {
                    Debug.Log($"Turn changed: {turnChanged.Faction}");
                }
                else if (evt is BattleOverEvent battleOver)
                {
                    Debug.Log($"Battle over: {battleOver.Outcome}");
                }
            }
        }

        private void AdvanceAnimations()
        {
            for (int i = _activeAnimations.Count - 1; i >= 0; i--)
            {
                var anim = _activeAnimations[i];

                if (anim.Queue.Count == 0 && Vector3.Distance(anim.Transform.localPosition, anim.Target) < 0.01f)
                {
                    _activeAnimations.RemoveAt(i);
                    continue;
                }

                if (Vector3.Distance(anim.Transform.localPosition, anim.Target) < 0.01f)
                {
                    var next = anim.Queue.Dequeue();
                    var (sx, sy) = IsoProjection.MapToScreen(next.X, next.Y, next.Z);
                    anim.Target = new Vector3(sx / TileRenderer.PixelsPerUnit, sy / TileRenderer.PixelsPerUnit, 0f);
                }

                anim.Transform.localPosition = Vector3.MoveTowards(
                    anim.Transform.localPosition, anim.Target, tilesPerSecond * Time.deltaTime);
            }
        }
    }
}
```

Note the one small added behavior beyond the pure refactor: `_selected = null`
when the selected unit dies (previous code left a stale reference to a
disabled GameObject in `_selected`, harmless before since nothing read
`_selected` externally — now that `Selected` is public and the HUD panel
(Task 7) and cursor views (Task 8) read it every frame, a stale dead-unit
reference would show a disabled unit's stats/cursor forever).

- [ ] **Step 2: Compile check**

Use `mcp__coplay-mcp__check_compile_errors` (fetch its schema via `ToolSearch`
query `"select:mcp__coplay-mcp__check_compile_errors"` if not already loaded
this session). Expected: no errors.

- [ ] **Step 3: Play and verify no regression**

`mcp__coplay-mcp__play_game`, then: click-select a soldier, click-move it,
right-click-fire at a Sectoid, press **Backspace** (not Space) and confirm the
turn ends and the AI takes its turn. `get_unity_logs` to confirm the same
`Debug.Log` sequence Phase 6 produced. `mcp__coplay-mcp__stop_game`.

- [ ] **Step 4: Commit**

```bash
git add unity/Assets/Scripts/Unity/BattleController.cs
git commit -m "refactor(unity): BattleController - expose Selected/HoveredTile/HoveredUnit, fix End Turn key to Backspace"
```

---

### Task 5: `CameraController` — edge-scroll, arrow-key pan, center-on-unit, no zoom

**This task requires a live Unity Editor.**

**Files:**
- Create: `unity/Assets/Scripts/Unity/CameraController.cs`
- Modify: `unity/Assets/Scenes/Battlescape.unity` (add the component to Main
  Camera, live via Coplay — do not hand-edit the `.unity` YAML)

**Interfaces:**
- Consumes: `CameraScroll.EdgeScrollDirection`/`.UnitsPerSecond` (Task 2),
  `BattleController.Selected` (Task 4), `IsoProjection.MapToScreen`,
  `TileRenderer.PixelsPerUnit`, `BattlescapeMapView.Grid` (existing).
- Produces (used by Task 6's Center button): `CameraController.CenterOnSelectedUnit()` (public, no args).

- [ ] **Step 1: Write `CameraController`**

Create `unity/Assets/Scripts/Unity/CameraController.cs`:

```csharp
using OpenXcom.Unity.Rendering;
using UnityEngine;

namespace OpenXcom.Unity
{
    /// <summary>
    /// Ports Camera::mouseOver/keyboardPress/centerOnPosition
    /// (src/Battlescape/Camera.cpp) onto the Main Camera's transform, using
    /// OXCE's own defaults (Options.cpp): edge-scroll always on, arrow-key
    /// pan, Home centers on the selected unit, no zoom, drag-scroll off by
    /// default (not implemented this phase). [SIMPLIFIED] map-bounds
    /// clamping: the original keeps the exact screen-center map coordinate
    /// inside [0, mapSize-1] via an iterative inverse-projection check
    /// (Camera::scrollXY, Camera.cpp:326-350); this port instead clamps the
    /// camera's world position to the map's own screen-space bounding box
    /// (computed once from its 4 corners) plus a half-viewport margin -
    /// visually equivalent for CULTA00's flat 10x10 grid, cheaper, and
    /// doesn't need the original's per-frame inverse-projection iteration.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CameraController : MonoBehaviour
    {
        // Options.cpp / Camera.cpp / Map.h defaults - see the Phase 7 plan's
        // Global Constraints for the exact source lines these come from.
        private const int ScrollSpeed = 8;
        private const float ScrollIntervalMs = 15f;
        private const float IconBarHeightPixels = 56f;

        [SerializeField] private BattlescapeMapView mapView;

        private Camera _camera;
        private BattleController _battleController;
        private float _unitsPerSecond;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _unitsPerSecond = CameraScroll.UnitsPerSecond(ScrollSpeed, ScrollIntervalMs, TileRenderer.PixelsPerUnit);
        }

        private void Start()
        {
            // Deferred to Start (not Awake): BattleController lives on a
            // different GameObject than this camera, and Phase 6's
            // BattlescapeBootstrap.Start() is what finishes setting it up.
            _battleController = FindFirstObjectByType<BattleController>();
        }

        private void Update()
        {
            HandleEdgeScroll();
            HandleKeyScroll();
            if (Input.GetKeyDown(KeyCode.Home)) // keyBattleCenterUnit default
                CenterOnSelectedUnit();
            ClampToMapBounds();
        }

        private void HandleEdgeScroll()
        {
            float viewportHeightPixels = Screen.height - IconBarHeightPixels;
            float mouseYFromTop = viewportHeightPixels - Input.mousePosition.y;
            if (mouseYFromTop < 0f)
                return; // mouse is over the icon bar, not the map viewport

            var (dx, dy) = CameraScroll.EdgeScrollDirection(
                (int)Input.mousePosition.x, (int)mouseYFromTop,
                Screen.width, (int)viewportHeightPixels, ScrollSpeed);

            if (dx != 0 || dy != 0)
                Pan(dx, dy);
        }

        private void HandleKeyScroll()
        {
            int dx = 0, dy = 0;
            if (Input.GetKey(KeyCode.LeftArrow)) dx += ScrollSpeed;
            if (Input.GetKey(KeyCode.RightArrow)) dx -= ScrollSpeed;
            if (Input.GetKey(KeyCode.UpArrow)) dy += ScrollSpeed;
            if (Input.GetKey(KeyCode.DownArrow)) dy -= ScrollSpeed;

            if (dx != 0 || dy != 0)
                Pan(dx, dy);
        }

        private void Pan(int dxTickPixels, int dyTickPixels)
        {
            // dxTickPixels/dyTickPixels are already scrollSpeed-scaled
            // (matches +-ScrollSpeed or +-ScrollSpeed/2); normalize to a
            // -1..1 direction then scale by the units-per-second rate.
            float dirX = dxTickPixels / (float)ScrollSpeed;
            float dirY = dyTickPixels / (float)ScrollSpeed;
            transform.position += new Vector3(dirX, dirY, 0f) * _unitsPerSecond * Time.deltaTime;
        }

        /// <summary>Snaps the camera to the selected unit's tile. Called by Home and the HUD's Center button (Task 6).</summary>
        public void CenterOnSelectedUnit()
        {
            if (_battleController == null)
                _battleController = FindFirstObjectByType<BattleController>();
            if (_battleController?.Selected == null)
                return;

            var pos = _battleController.Selected.Position;
            var (screenX, screenY) = IsoProjection.MapToScreen(pos.X, pos.Y, pos.Z);
            transform.position = new Vector3(
                screenX / TileRenderer.PixelsPerUnit, screenY / TileRenderer.PixelsPerUnit, transform.position.z);
        }

        private void ClampToMapBounds()
        {
            if (mapView == null || mapView.Grid == null)
                return;

            var grid = mapView.Grid;
            var (minSx, minSy) = IsoProjection.MapToScreen(0, grid.Length - 1, 0);
            var (maxSx, maxSy) = IsoProjection.MapToScreen(grid.Width - 1, 0, 0);
            var (topSx, topSy) = IsoProjection.MapToScreen(0, 0, 0);
            var (botSx, botSy) = IsoProjection.MapToScreen(grid.Width - 1, grid.Length - 1, 0);

            float minX = Mathf.Min(minSx, maxSx, topSx, botSx) / TileRenderer.PixelsPerUnit;
            float maxX = Mathf.Max(minSx, maxSx, topSx, botSx) / TileRenderer.PixelsPerUnit;
            float minY = Mathf.Min(minSy, maxSy, topSy, botSy) / TileRenderer.PixelsPerUnit;
            float maxY = Mathf.Max(minSy, maxSy, topSy, botSy) / TileRenderer.PixelsPerUnit;

            var pos = transform.position;
            pos.x = Mathf.Clamp(pos.x, minX, maxX);
            pos.y = Mathf.Clamp(pos.y, minY, maxY);
            transform.position = pos;
        }
    }
}
```

- [ ] **Step 2: Compile check**

`mcp__coplay-mcp__check_compile_errors`. Expected: no errors.

- [ ] **Step 3: Add the component to Main Camera and wire `mapView`**

Via Coplay MCP (fetch schemas with `ToolSearch` query
`"select:mcp__coplay-mcp__add_component,mcp__coplay-mcp__set_property,mcp__coplay-mcp__list_game_objects_in_hierarchy"`
if not already loaded):
1. `list_game_objects_in_hierarchy` on `Assets/Scenes/Battlescape.unity` to
   get the exact GameObject paths for "Main Camera" and "Battlescape".
2. `add_component` — add `OpenXcom.Unity.CameraController` to "Main Camera".
3. `set_property` — set `CameraController.mapView` to the `BattlescapeMapView`
   component on the "Battlescape" GameObject.
4. `save_scene`.

- [ ] **Step 4: Play and verify live**

`play_game`. Move the mouse to each of the 4 screen edges (staying above the
bottom ~56px icon-bar area) and confirm the view pans; press each arrow key
and confirm the same; select a unit, move the camera away, press **Home**,
confirm it snaps back centered on that unit. **If the pan direction is
inverted** (moving the mouse to the right edge reveals content to the left
instead of the right, or vice versa), negate `dirX`/`dirY` in `Pan` and
re-test — this sign was derived on paper from `Camera.cpp`, not confirmed
against a running scene, so treat the first live run as the check for it, the
same way `IsoProjection.UnitSortingOrder`'s sign/range was caught and fixed
live in Phase 6. `capture_scene_object` or a screenshot to confirm visually.
`stop_game`.

- [ ] **Step 5: Commit**

```bash
git add unity/Assets/Scripts/Unity/CameraController.cs unity/Assets/Scenes/Battlescape.unity
git commit -m "feat(unity): CameraController - edge-scroll, arrow-key pan, Home to center, no zoom"
```

---

### Task 6: HUD `Canvas` + `IconBarView`

**This task requires a live Unity Editor.**

**Files:**
- Create: `unity/Assets/Scripts/Unity/UI/HudBootstrap.cs`
- Create: `unity/Assets/Scripts/Unity/UI/IconBarView.cs`
- Modify: `unity/Assets/Scenes/Battlescape.unity` (add a "HUD" GameObject +
  `EventSystem`, live via Coplay)

**Interfaces:**
- Consumes: `AtlasLoader.Load` (existing), `BattleController.Selected`,
  a reference to the scene's `CameraController` (Task 5) for the Center
  button, `BattleState.EndPlayerTurn` (existing, via `BattleController`
  — needs one more small addition: a public `EndPlayerTurn()` passthrough,
  since `BattleController` currently only calls `_state.EndPlayerTurn()`
  internally from its own `Update()`, with no public wrapper another
  MonoBehaviour can call from a UI Button's `onClick`).
- Produces (used by Task 7): `HudBootstrap` creates the Canvas that
  `SelectedUnitPanel` (Task 7) attaches its own elements under; exposes
  `public Transform PanelParent` (a child RectTransform of the icon bar
  reserved for the stats block, at the "Stats panel" rect from the Global
  Constraints table) for Task 7 to build into.

- [ ] **Step 1: Add a public End-Turn entry point to `BattleController`**

In `unity/Assets/Scripts/Unity/BattleController.cs`, add this method (public,
alongside `Bind`) — a thin passthrough so a UI Button's `onClick` (which needs
a public no-arg method) can trigger the same end-turn path `Update()`'s
Backspace handler uses:

```csharp
        /// <summary>Ends the player's turn. Same effect as pressing Backspace
        /// (see Update) - exposed so the HUD's End Turn button (Task 6) can
        /// call it from a UnityEvent, which requires a public no-arg method.</summary>
        public void EndTurnFromHud()
        {
            if (_state == null || _state.IsBattleOver)
                return;
            _state.EndPlayerTurn();
            DrainAndAnimate();
        }
```

(`DrainAndAnimate` and `_state` are already private members of this class —
this method lives in the same file, so it can call them directly.)

- [ ] **Step 2: Compile check**

`mcp__coplay-mcp__check_compile_errors`. Expected: no errors.

- [ ] **Step 3: Write `IconBarView`**

Create `unity/Assets/Scripts/Unity/UI/IconBarView.cs`:

```csharp
using System.IO;
using OpenXcom.Unity.Rendering;
using UnityEngine;
using UnityEngine.UI;

namespace OpenXcom.Unity.UI
{
    /// <summary>
    /// The bottom icon bar: the converted ICONS.PCK background at its real
    /// 320x56 size, plus every button at its exact original pixel rect
    /// (BattlescapeState.cpp's hardcoded constructors - see the Phase 7
    /// plan's Global Constraints table). Only End Turn and Center are wired
    /// to real BattleState/CameraController actions; every other button
    /// renders the real icon art but does nothing - Core has no kneel/
    /// inventory/reserve-TU/next-soldier/show-layers/abort/stats-popup
    /// action yet (user's explicit choice: full icon bar layout, functional
    /// buttons only for what Core supports).
    /// </summary>
    public sealed class IconBarView : MonoBehaviour
    {
        private const float IconBarWidth = 320f;
        private const float IconBarHeight = 56f;

        private static readonly (string name, Rect rect, bool functional)[] Buttons =
        {
            ("UnitUp", new Rect(48, 0, 32, 16), false),
            ("UnitDown", new Rect(48, 16, 32, 16), false),
            ("MapUp", new Rect(80, 0, 32, 16), false),
            ("MapDown", new Rect(80, 16, 32, 16), false),
            ("ShowMap", new Rect(112, 0, 32, 16), false),
            ("Kneel", new Rect(112, 16, 32, 16), false),
            ("Inventory", new Rect(144, 0, 32, 16), false),
            ("Center", new Rect(144, 16, 32, 16), true),
            ("NextSoldier", new Rect(176, 0, 32, 16), false),
            ("PrevSoldier", new Rect(176, 16, 32, 16), false),
            ("ShowLayers", new Rect(208, 0, 32, 16), false),
            ("Help", new Rect(208, 16, 32, 16), false),
            ("EndTurn", new Rect(240, 0, 32, 16), true),
            ("Abort", new Rect(240, 16, 32, 16), false),
        };

        /// <summary>Reserved area for Task 7's SelectedUnitPanel (the "Stats panel" rect: 107,33,164,23).</summary>
        public RectTransform PanelParent { get; private set; }

        /// <summary>
        /// battleController/cameraController are passed in (not
        /// [SerializeField]) because HudBootstrap - the single owner of both
        /// references - is the only caller; IconBarView stays a pure builder
        /// with no Inspector wiring of its own, matching
        /// BattlescapeBootstrap's existing "everything constructed in code"
        /// convention.
        /// </summary>
        public void Build(RectTransform canvasRoot, string gameDataDir,
            BattleController battleController, CameraController cameraController)
        {
            var barGo = new GameObject("IconBar", typeof(RectTransform));
            var barRect = barGo.GetComponent<RectTransform>();
            barRect.SetParent(canvasRoot, worldPositionStays: false);
            barRect.anchorMin = new Vector2(0.5f, 0f);
            barRect.anchorMax = new Vector2(0.5f, 0f);
            barRect.pivot = new Vector2(0.5f, 0f);
            barRect.sizeDelta = new Vector2(IconBarWidth, IconBarHeight);
            barRect.anchoredPosition = Vector2.zero;

            var (texture, frameRects) = AtlasLoader.Load(gameDataDir, "icons");
            var bgImage = barGo.AddComponent<Image>();
            bgImage.sprite = Sprite.Create(texture, frameRects[0], new Vector2(0.5f, 0.5f));
            bgImage.type = Image.Type.Simple;

            foreach (var (name, rect, functional) in Buttons)
                BuildButton(barRect, name, rect, functional, battleController, cameraController);

            PanelParent = BuildPanelParent(barRect);
        }

        private void BuildButton(RectTransform barRect, string name, Rect rect, bool functional,
            BattleController battleController, CameraController cameraController)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(barRect, worldPositionStays: false);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(rect.width, rect.height);
            rt.anchoredPosition = new Vector2(rect.x, -rect.y);

            var image = go.AddComponent<Image>();
            image.color = functional
                ? new Color(0.25f, 0.55f, 0.25f, 0.55f)  // functional buttons: faint green tint
                : new Color(0.15f, 0.15f, 0.15f, 0.35f); // inert buttons: faint dark tint, still visible as a button-shaped region

            if (!functional)
                return;

            var button = go.AddComponent<Button>();
            if (name == "EndTurn")
                button.onClick.AddListener(() => battleController.EndTurnFromHud());
            else if (name == "Center")
                button.onClick.AddListener(() => cameraController.CenterOnSelectedUnit());
        }

        private RectTransform BuildPanelParent(RectTransform barRect)
        {
            var go = new GameObject("StatsPanel", typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(barRect, worldPositionStays: false);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(164, 23);
            rt.anchoredPosition = new Vector2(107, -33);
            return rt;
        }
    }
}
```

- [ ] **Step 4: Write `HudBootstrap`**

Create `unity/Assets/Scripts/Unity/UI/HudBootstrap.cs`:

```csharp
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OpenXcom.Unity.UI
{
    /// <summary>
    /// Builds the Canvas (Constant Pixel Size, so the icon bar renders at
    /// its native resolution regardless of window size - OpenXcom itself
    /// supports arbitrary resolutions while keeping UI elements pixel-
    /// native, rather than uGUI's stretch-to-fit "Scale With Screen Size"),
    /// then the icon bar and selected-unit panel underneath it. Everything
    /// constructed in code, no Inspector drag-and-drop references required -
    /// matches BattlescapeBootstrap's existing convention.
    /// </summary>
    [RequireComponent(typeof(IconBarView))]
    [RequireComponent(typeof(SelectedUnitPanel))]
    public sealed class HudBootstrap : MonoBehaviour
    {
        [SerializeField] private BattleController battleController;
        [SerializeField] private CameraController cameraController;

        private void Start()
        {
            string gameDataDir = Path.Combine(Application.dataPath, "GameData");

            var canvasGo = new GameObject("HudCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, worldPositionStays: false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;

            canvasGo.AddComponent<GraphicRaycaster>();

            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var esGo = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            }

            var iconBar = GetComponent<IconBarView>();
            iconBar.Build(canvasGo.GetComponent<RectTransform>(), gameDataDir, battleController, cameraController);

            var panel = GetComponent<SelectedUnitPanel>();
            panel.Build(iconBar.PanelParent, battleController);
        }
    }
}
```

`battleController`/`cameraController` are wired via serialized-field
assignment in Step 6 below (Coplay `set_property`) — `HudBootstrap` is the
single owner of both references and passes them into `IconBarView.Build`/
`SelectedUnitPanel.Build`, so neither view does its own object lookup.

- [ ] **Step 5: Compile check**

`mcp__coplay-mcp__check_compile_errors`. Expected: no errors (Task 7 hasn't
written `SelectedUnitPanel` yet, so this will fail on the missing type until
Task 7 is done — that's expected and fine to leave uncompiled between these
two tasks if executing sequentially without a gap; if executing Task 6 fully
standalone, stub `SelectedUnitPanel` with an empty `Build(RectTransform,
BattleController)` method now and let Task 7 fill it in).

- [ ] **Step 6: Add the HUD GameObject to the scene**

Via Coplay MCP:
1. `create_game_object` — name "HUD", parented at scene root.
2. `add_component` — add `OpenXcom.Unity.UI.IconBarView`,
   `OpenXcom.Unity.UI.SelectedUnitPanel`, then `OpenXcom.Unity.UI.HudBootstrap`
   (order matters: `HudBootstrap`'s `[RequireComponent]` needs the other two
   present first, though Unity auto-adds required components if missing —
   either order works).
3. `set_property` — set `HudBootstrap.battleController` and
   `.cameraController` to the existing `BattleController`/`CameraController`
   components (on "Battlescape" and "Main Camera" respectively).
4. `save_scene`.

- [ ] **Step 7: Play and verify live**

`play_game`. Confirm the icon bar renders at the bottom-center of the game
view at its native 320×56 size. Click the End Turn button (green-tinted
rect at 240,0) and confirm the turn ends (same log output as pressing
Backspace). Select a unit, move the camera, click the Center button
(green-tinted rect at 144,16) and confirm the camera snaps back. Click any
other (dark-tinted) button and confirm nothing happens — no error, no
action. `capture_scene_object`/screenshot to confirm the layout visually
matches the original's icon positions. `stop_game`.

- [ ] **Step 8: Commit**

```bash
git add unity/Assets/Scripts/Unity/BattleController.cs unity/Assets/Scripts/Unity/UI/IconBarView.cs \
  unity/Assets/Scripts/Unity/UI/HudBootstrap.cs unity/Assets/Scenes/Battlescape.unity
git commit -m "feat(unity): HUD Canvas + IconBarView - real icon bar layout, End Turn/Center wired"
```

---

### Task 7: `SelectedUnitPanel` — name/TU/Health/Energy readouts

**This task requires a live Unity Editor.**

**Files:**
- Create: `unity/Assets/Scripts/Unity/UI/SelectedUnitPanel.cs`

**Interfaces:**
- Consumes: `BattleController.Selected` (Task 4), `IconBarView.PanelParent`
  (Task 6).
- Produces: nothing consumed by later tasks (last HUD piece).

- [ ] **Step 1: Write `SelectedUnitPanel`**

Create `unity/Assets/Scripts/Unity/UI/SelectedUnitPanel.cs`:

```csharp
using OpenXcom.Core.Battle;
using UnityEngine;
using UnityEngine.UI;

namespace OpenXcom.Unity.UI
{
    /// <summary>
    /// Name/TU/Health/Energy readouts for BattleController.Selected, placed
    /// inside IconBarView's reserved "Stats panel" rect (107,33,164,23 -
    /// BattlescapeState.cpp's hardcoded layout). No Morale bar: BattleUnit
    /// has no morale field (morale/panic is out of scope this project-wide,
    /// not just this phase) - the row is omitted rather than faked with a
    /// placeholder value. Numeric positions below are each field's rect from
    /// BattlescapeState.cpp, translated to be relative to the Stats panel's
    /// own origin (subtract 107,33 from each original x+107,y+33 pair).
    /// </summary>
    public sealed class SelectedUnitPanel : MonoBehaviour
    {
        private BattleController _battleController;
        private Text _nameText;
        private Text _tuText;
        private Text _energyText;
        private Text _healthText;
        private Slider _tuBar;
        private Slider _energyBar;
        private Slider _healthBar;

        public void Build(RectTransform panelParent, BattleController battleController)
        {
            _battleController = battleController;

            _nameText = BuildText(panelParent, "Name", new Vector2(28, -0), new Vector2(136, 10));
            _tuText = BuildText(panelParent, "TuText", new Vector2(29, -9), new Vector2(15, 5));
            _energyText = BuildText(panelParent, "EnergyText", new Vector2(47, -9), new Vector2(15, 5));
            _healthText = BuildText(panelParent, "HealthText", new Vector2(29, -17), new Vector2(15, 5));

            _tuBar = BuildBar(panelParent, "TuBar", new Vector2(63, -8), new Vector2(102, 3), new Color(0.25f, 0.35f, 0.85f));
            _energyBar = BuildBar(panelParent, "EnergyBar", new Vector2(63, -12), new Vector2(102, 3), new Color(0.85f, 0.65f, 0.15f));
            _healthBar = BuildBar(panelParent, "HealthBar", new Vector2(63, -16), new Vector2(102, 3), new Color(0.15f, 0.75f, 0.25f));
        }

        private static Text BuildText(RectTransform parent, string name, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;

            var text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 8;
            text.color = Color.white;
            text.alignment = TextAnchor.UpperLeft;
            return text;
        }

        private static Slider BuildBar(RectTransform parent, string name, Vector2 pos, Vector2 size, Color fillColor)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, worldPositionStays: false);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;

            var slider = go.AddComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.interactable = false;
            slider.transition = Selectable.Transition.None;

            var fillAreaGo = new GameObject("FillArea", typeof(RectTransform));
            var fillAreaRt = fillAreaGo.GetComponent<RectTransform>();
            fillAreaRt.SetParent(rt, worldPositionStays: false);
            fillAreaRt.anchorMin = Vector2.zero;
            fillAreaRt.anchorMax = Vector2.one;
            fillAreaRt.sizeDelta = Vector2.zero;

            var fillGo = new GameObject("Fill", typeof(RectTransform));
            var fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.SetParent(fillAreaRt, worldPositionStays: false);
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.sizeDelta = Vector2.zero;
            var fillImage = fillGo.AddComponent<Image>();
            fillImage.color = fillColor;

            slider.fillRect = fillRt;
            slider.targetGraphic = fillImage;
            return slider;
        }

        private void Update()
        {
            var unit = _battleController.Selected;
            bool hasSelection = unit != null;

            _nameText.text = hasSelection ? unit.Name : "";
            _tuText.text = hasSelection ? unit.TimeUnits.ToString() : "";
            _energyText.text = hasSelection ? unit.Energy.ToString() : "";
            _healthText.text = hasSelection ? unit.Health.ToString() : "";

            _tuBar.value = hasSelection && unit.Stats.TimeUnits > 0 ? (float)unit.TimeUnits / unit.Stats.TimeUnits : 0f;
            _energyBar.value = hasSelection && unit.Stats.Stamina > 0 ? (float)unit.Energy / unit.Stats.Stamina : 0f;
            _healthBar.value = hasSelection && unit.Stats.Health > 0 ? (float)unit.Health / unit.Stats.Health : 0f;
        }
    }
}
```

- [ ] **Step 2: Compile check**

`mcp__coplay-mcp__check_compile_errors`. Expected: no errors (this also
resolves Task 6 Step 5's deferred check, since `SelectedUnitPanel` now
exists for real).

- [ ] **Step 3: Play and verify live**

`play_game`. Select a soldier: confirm its name, TU, Energy, and Health
numbers and bars appear in the stats panel area (inside the icon bar, left
side). Move it (spend TU) and confirm the TU number/bar drop. Have it take
damage (right-click-fire from a Sectoid, or just fire back and forth) and
confirm the Health number/bar drop. Deselect (there's no explicit deselect
action this phase — instead select the other side's unit conceptually isn't
possible either; simplest check: after a unit dies, confirm the panel goes
blank rather than showing stale numbers, per Task 4's `_selected = null`
fix). `capture_scene_object`/screenshot. `stop_game`.

- [ ] **Step 4: Commit**

```bash
git add unity/Assets/Scripts/Unity/UI/SelectedUnitPanel.cs
git commit -m "feat(unity): SelectedUnitPanel - name/TU/Health/Energy bound to the real selected unit"
```

---

### Task 8: `TileCursorView` + `PathPreviewView` — tile selector + TU-colored path arrows

**This task requires a live Unity Editor.**

**Files:**
- Create: `unity/Assets/Scripts/Unity/TileCursorView.cs`
- Create: `unity/Assets/Scripts/Unity/PathPreviewView.cs`
- Modify: `unity/Assets/Scripts/Unity/BattlescapeBootstrap.cs` (instantiate
  both, load the `cursor`/`pathfinding` atlases)

**Interfaces:**
- Consumes: `BattleController.HoveredTile`/`.Selected` (Task 4),
  `Pathfinding.FindPath` (existing, `Core/Battle/Pathfinding.cs`),
  `PathPreview.ComputeAffordability` (Task 3), `Directions.IndexOf` (Task 3),
  `AtlasLoader.Load` (existing).
- Produces: nothing consumed by later tasks (last gameplay-feel piece).

- [ ] **Step 1: Write `TileCursorView`**

Create `unity/Assets/Scripts/Unity/TileCursorView.cs`:

```csharp
using System.Collections.Generic;
using OpenXcom.Unity.Rendering;
using UnityEngine;

namespace OpenXcom.Unity
{
    /// <summary>
    /// The flashing tile-selector box under the mouse (CT_NORMAL cursor,
    /// Map.cpp:1560's frame[CT_NORMAL] = 0, animated + (_animFrame/4)%2 -
    /// frames 0 and 1 of CURSOR.PCK, toggling every 400ms per
    /// DEFAULT_ANIM_SPEED=100ms * 4 ticks, BattlescapeState.h:118). Only the
    /// move cursor is ported this phase - no aim/psi/throw/waypoint cursor
    /// variants (frame[] indices 11/13/15 in the original), since this
    /// project's mouse interaction is move + fire only.
    /// </summary>
    public sealed class TileCursorView : MonoBehaviour
    {
        private const float FlashIntervalSeconds = 0.4f;

        private BattleController _battleController;
        private SpriteRenderer _renderer;
        private Sprite _frame0;
        private Sprite _frame1;

        public void Setup(BattleController battleController, (Texture2D texture, List<Rect> frameRects) cursorAtlas)
        {
            _battleController = battleController;

            var go = new GameObject("TileCursor");
            go.transform.SetParent(transform, worldPositionStays: false);
            _renderer = go.AddComponent<SpriteRenderer>();
            _renderer.sortingOrder = short.MaxValue; // always drawn on top

            _frame0 = Sprite.Create(cursorAtlas.texture, cursorAtlas.frameRects[0], new Vector2(0.5f, 0f), TileRenderer.PixelsPerUnit);
            _frame1 = Sprite.Create(cursorAtlas.texture, cursorAtlas.frameRects[1], new Vector2(0.5f, 0f), TileRenderer.PixelsPerUnit);
        }

        private void Update()
        {
            var hovered = _battleController.HoveredTile;
            if (hovered == null)
            {
                _renderer.enabled = false;
                return;
            }

            _renderer.enabled = true;
            var (screenX, screenY) = IsoProjection.MapToScreen(hovered.Value.X, hovered.Value.Y, hovered.Value.Z);
            _renderer.transform.localPosition = new Vector3(screenX / TileRenderer.PixelsPerUnit, screenY / TileRenderer.PixelsPerUnit, 0f);

            bool phase = Mathf.FloorToInt(Time.time / FlashIntervalSeconds) % 2 == 0;
            _renderer.sprite = phase ? _frame0 : _frame1;
        }
    }
}
```

- [ ] **Step 2: Write `PathPreviewView`**

Create `unity/Assets/Scripts/Unity/PathPreviewView.cs`:

```csharp
using System.Collections.Generic;
using OpenXcom.Core.Battle;
using OpenXcom.Core.Common;
using OpenXcom.Unity.Rendering;
using UnityEngine;

namespace OpenXcom.Unity
{
    /// <summary>
    /// TU-cost-colored path arrows from the selected unit to the hovered
    /// tile - yellow (affordable) or red (not), per-step
    /// (Rendering.PathPreview.ComputeAffordability). [SIMPLIFIED]: one arrow
    /// sprite per step, tinted directly, rather than the original's two-pass
    /// neutral-base + colored-overlay compositing (Map.cpp:1288-1301,
    /// :1635-1640) - see CameraController's and the Phase 7 plan's Global
    /// Constraints for why. Frame index = compass direction from the
    /// previous step (Directions.IndexOf), matching the Pathfinding.png
    /// sheet's direction-ordered first 8 frames (0=N..7=NW).
    /// </summary>
    public sealed class PathPreviewView : MonoBehaviour
    {
        private static readonly Color Affordable = new(1f, 0.85f, 0.1f, 0.9f);  // yellow
        private static readonly Color Unaffordable = new(0.9f, 0.15f, 0.1f, 0.9f); // red

        private BattleController _battleController;
        private BattleState _state;
        private (Texture2D texture, List<Rect> frameRects) _pathAtlas;
        private readonly List<SpriteRenderer> _arrows = new();

        public void Setup(BattleController battleController, BattleState state,
            (Texture2D texture, List<Rect> frameRects) pathAtlas)
        {
            _battleController = battleController;
            _state = state;
            _pathAtlas = pathAtlas;
        }

        private void Update()
        {
            ClearArrows();

            var selected = _battleController.Selected;
            var hovered = _battleController.HoveredTile;
            if (selected == null || hovered == null || selected.Position == hovered.Value)
                return;

            var path = Pathfinding.FindPath(_state.Grid, selected.Position, hovered.Value);
            if (path == null || path.Count == 0)
                return;

            var affordability = PathPreview.ComputeAffordability(path, selected.TimeUnits);

            var previous = selected.Position;
            for (int i = 0; i < path.Count; i++)
            {
                var step = path[i];
                int dirIndex = Directions.IndexOf(step.Position - previous);
                previous = step.Position;
                if (dirIndex < 0)
                    continue; // shouldn't happen (Pathfinding only takes 8-dir steps), skip defensively

                var arrowGo = new GameObject($"Arrow_{i}");
                arrowGo.transform.SetParent(transform, worldPositionStays: false);
                var renderer = arrowGo.AddComponent<SpriteRenderer>();
                renderer.sortingOrder = short.MaxValue - 1; // below the tile cursor, above everything else
                renderer.sprite = Sprite.Create(_pathAtlas.texture, _pathAtlas.frameRects[dirIndex], new Vector2(0.5f, 0f), TileRenderer.PixelsPerUnit);
                renderer.color = affordability[i] == PathPreview.Affordability.Affordable ? Affordable : Unaffordable;

                var (screenX, screenY) = IsoProjection.MapToScreen(step.Position.X, step.Position.Y, step.Position.Z);
                arrowGo.transform.localPosition = new Vector3(screenX / TileRenderer.PixelsPerUnit, screenY / TileRenderer.PixelsPerUnit, 0f);

                _arrows.Add(renderer);
            }
        }

        private void ClearArrows()
        {
            foreach (var arrow in _arrows)
                Destroy(arrow.gameObject);
            _arrows.Clear();
        }
    }
}
```

- [ ] **Step 3: Wire both views into `BattlescapeBootstrap`**

In `unity/Assets/Scripts/Unity/BattlescapeBootstrap.cs`, add
`[RequireComponent(typeof(TileCursorView))]` and
`[RequireComponent(typeof(PathPreviewView))]` to the class attributes
(alongside the existing two), then in `Start()`, after the existing
`GetComponent<BattleController>().Bind(state, unitTransforms);` line, add:

```csharp

            var cursorAtlas = AtlasLoader.Load(gameDataDir, "cursor");
            GetComponent<TileCursorView>().Setup(GetComponent<BattleController>(), cursorAtlas);

            var pathAtlas = AtlasLoader.Load(gameDataDir, "pathfinding");
            GetComponent<PathPreviewView>().Setup(GetComponent<BattleController>(), state, pathAtlas);
```

- [ ] **Step 4: Compile check**

`mcp__coplay-mcp__check_compile_errors`. Expected: no errors.

- [ ] **Step 5: Add the two components to the scene**

Via Coplay MCP: `add_component` — add `OpenXcom.Unity.TileCursorView` and
`OpenXcom.Unity.PathPreviewView` to the existing "Battlescape" GameObject
(the one already holding `BattlescapeMapView`/`BattleController`/
`BattlescapeBootstrap` — `[RequireComponent]` means Unity will also add them
automatically the next time the scene is saved/reloaded if skipped, but add
explicitly for clarity). `save_scene`.

- [ ] **Step 6: Play and verify live**

`play_game`. Hover over map tiles (not over a unit, not over the icon bar)
and confirm a flashing box cursor appears on the hovered tile. Select a
soldier, then hover over a few different reachable tiles at increasing
distance and confirm arrows trace the path from the soldier to the cursor,
starting yellow and turning red once the cumulative TU cost would exceed the
soldier's remaining TimeUnits. Hover over an unreachable tile (if any exist
on CULTA00 — likely none, since Phase 6 confirmed every tile is walkable, so
this may not be directly testable on this map; note that in the check
instead of forcing it) and confirm no arrows render past `Pathfinding.FindPath`
returning null (no exception). `get_unity_logs` to confirm no runtime errors.
`capture_scene_object`/screenshot. `stop_game`.

- [ ] **Step 7: Commit**

```bash
git add unity/Assets/Scripts/Unity/TileCursorView.cs unity/Assets/Scripts/Unity/PathPreviewView.cs \
  unity/Assets/Scripts/Unity/BattlescapeBootstrap.cs
git commit -m "feat(unity): TileCursorView + PathPreviewView - flashing tile cursor and TU-colored path preview"
```

---

### Task 9: Full live verification pass

**This task requires a live Unity Editor.** No new code — this is the
end-to-end pass tying every earlier task's live checks together into one
continuous playthrough, the same way Phase 6's Task 6 Step 5 did.

**Files:** none (verification only).

- [ ] **Step 1: Full playthrough**

`play_game`. In one continuous session:
1. Confirm the camera starts framing the squad, pans at all 4 edges and via
   arrow keys, and Home recenters on the selected unit.
2. Confirm the icon bar renders correctly and End Turn/Center both work from
   the HUD buttons (not just the keyboard).
3. Confirm the selected-unit panel tracks TU/Health/Energy through a move and
   a shot.
4. Confirm the tile cursor and path-preview arrows appear and color correctly
   while a unit is selected.
5. Play through to a win/loss (repeatedly fire until one side is wiped) and
   confirm `BattleOverEvent` still fires and further input is rejected
   (`_state.IsBattleOver` gate in `BattleController.Update`, unchanged from
   Phase 6).

`get_unity_logs` across the whole session to confirm no exceptions.
`capture_scene_object`/screenshot at a few points (idle, mid-move-with-path-
preview, HUD interaction) for a visual record. `stop_game`.

- [ ] **Step 2: Full regression test suite**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test`
Expected: all tests pass (Tasks 1-3's new tests plus every pre-existing test).

- [ ] **Step 3: Final commit (if Step 1 surfaced any fixes)**

If Step 1's live pass required any corrections (e.g. the camera-pan sign flip
flagged in Task 5, or any other live-only bug), commit them now with a
message describing what was wrong and how it was confirmed
(`play_game`/`get_unity_logs`/screenshot), matching Phase 6's Task 7/8
postmortem-commit convention. If no fixes were needed, skip this step — no
empty commit.
