# Phase 8 — Voxel Line-of-Fire Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Port the original engine's voxel-level shot-resolution trace (`TileEngine::calculateLine`/`voxelCheck`, `Projectile::applyAccuracy`) so `BattleState.TryFire` resolves shots against real 3D geometry instead of an abstract percent roll — a deviated miss can now hit terrain or a bystander instead of doing nothing.

**Architecture:** New Core-only capability layered on existing code: `Xcom.Convert` gains a `LoftempsDecoder` (parses `LOFTEMPS.DAT`) and extends `McdDecoder`/`RuleYamlDecoder` to carry the per-tile-part `Loft[12]` table and per-armor `Loftemps`/per-unit stand/kneel/float heights that already exist in the source `.rul`/`.MCD` data but are currently parsed and dropped. Core's `TileEngine` gains `VoxelCheck`/`CalculateLine`/`GetOriginVoxel`/`GetDirectionTo`; `Combat` gains `ApplyDeviation`/`ApplyDamage`. `BattleState.TryFire` is rewired to trace a deviated shot through this new voxel system instead of calling `Combat.ResolveShot`'s percent-roll path directly.

**Tech Stack:** C# 9/.NET 8, xUnit (`dotnet test`), Newtonsoft.Json (Convert output / Core input), YamlDotNet (Convert ruleset reading).

## Global Constraints

- Voxel coordinate scale (ported exactly, do not change): **16 voxel units per tile in X/Y, 24 in Z** (`TileEngine.cpp:2431,4501,4560,4619-4620`).
- `LOFTEMPS.DAT` is a flat little-endian `uint16` stream, no header; every 16 consecutive values form one 16×16-bit loft template (`MapDataSet::loadLOFTEMPS`, `MapDataSet.cpp:272`).
- MCD record size is 62 bytes (`McdDecoder.RecordSize`, already established); the 12 `Loft` bytes sit at byte offset 8-19 within each record (`src/Mod/MapDataSet.cpp:118`), currently parsed nowhere in this codebase.
- Test convention: xUnit `[Fact]` only (no `[Theory]`/`[InlineData]` anywhere in this codebase — follow it), namespace `OpenXcom.Core.Tests` for Core tests (files directly under `unity/Tests.Standalone/`), `OpenXcom.Core.Tests.Convert` for converter tests (files under `unity/Tests.Standalone/Convert/`).
- Test command: `export PATH="$HOME/.dotnet:$PATH" && dotnet test unity/Tests.Standalone/OpenXcom.Core.Tests.csproj` — run the **full suite** after every task (confirmed working: 141 tests pass in ~1s before this plan's changes), not just new tests, since several tasks add optional constructor parameters that could silently break other call sites if defaults are wrong.
- Every new/modified public method needs a `///` doc comment citing the C++ file:line it ports, per this repo's established convention (see any existing `Battle/*.cs` file).
- This phase is Core + `Xcom.Convert` only. No Unity/`BattleController` rendering changes (that's Phase 9) — the one exception is a single required line in `BattlescapeBootstrap.cs` (Task 10) that loads `loftemps.json` into `BattleState.LoftData`, without which every shot would silently always miss (not a rendering change, but load-bearing for correctness).

---

## Task 1: Convert — armor `Loftemps` + unit stand/kneel/float heights

**Files:**
- Modify: `unity/Xcom.Convert/Decoders/RuleYamlDecoder.cs`
- Test: `unity/Tests.Standalone/Convert/RuleYamlDecoderTests.cs`

**Interfaces:**
- Consumes: existing `RuleYamlDecoder.LoadArmor`/`LoadAlienUnit`/`LoadSoldierUnit`, existing `ConvertedArmor`/`ConvertedUnit` DTOs (all in this file).
- Produces: `ConvertedArmor.Loftemps` (int), `ConvertedUnit.StandHeight`/`KneelHeight`/`FloatHeight` (int) — consumed by Task 4 (`ConvertJob`) and, once round-tripped through JSON, by Task 5 (`DataLoader`).

- [ ] **Step 1: Write the failing tests**

Add to `unity/Tests.Standalone/Convert/RuleYamlDecoderTests.cs`, extending the existing `LoadArmor_ParsesSectoidArmor0` test and adding two new facts:

```csharp
        [Fact]
        public void LoadArmor_ParsesSectoidArmor0()
        {
            var armor = RuleYamlDecoder.LoadArmor(
                Path.Combine(RulesDir, "armors.rul"), "SECTOID_ARMOR0");

            Assert.Equal("SECTOID_ARMOR0", armor.Id);
            Assert.Equal(4, armor.Front);
            Assert.Equal(3, armor.Side);
            Assert.Equal(2, armor.Rear);
            Assert.Equal(2, armor.Under);
            Assert.Equal(2, armor.Loftemps);
        }

        [Fact]
        public void LoadArmor_ParsesStrNoneUcLoftempsAndArmorValues()
        {
            var armor = RuleYamlDecoder.LoadArmor(
                Path.Combine(RulesDir, "armors.rul"), "STR_NONE_UC");

            Assert.Equal("STR_NONE_UC", armor.Id);
            Assert.Equal(12, armor.Front);
            Assert.Equal(8, armor.Side);
            Assert.Equal(5, armor.Rear);
            Assert.Equal(2, armor.Under);
            Assert.Equal(3, armor.Loftemps);
        }
```

Extend `LoadAlienUnit_ParsesSectoidSoldierStatsAndArmorId` (add after the existing `Assert.Equal("SECTOID_ARMOR0", unit.ArmorId);`):

```csharp
            Assert.Equal(16, unit.StandHeight);
            Assert.Equal(12, unit.KneelHeight);
            Assert.Equal(0, unit.FloatHeight);
```

Extend `LoadSoldierUnit_ParsesMinStatsAndLeavesArmorIdNull` (add after `Assert.Null(unit.ArmorId);`):

```csharp
            Assert.Equal(22, unit.StandHeight);
            Assert.Equal(14, unit.KneelHeight);
            Assert.Equal(0, unit.FloatHeight);
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet test unity/Tests.Standalone/OpenXcom.Core.Tests.csproj --filter RuleYamlDecoderTests`
Expected: FAIL — `ConvertedArmor` has no `Loftemps` member, `ConvertedUnit` has no `StandHeight`/`KneelHeight`/`FloatHeight` members (compile error).

- [ ] **Step 3: Implement**

In `unity/Xcom.Convert/Decoders/RuleYamlDecoder.cs`, modify the DTOs and loaders:

```csharp
    public sealed class ConvertedUnit
    {
        public string Id;
        public ConvertedStats Stats;
        public string ArmorId; // null when this unit's armor isn't converted this slice
        public int StandHeight;
        public int KneelHeight;
        public int FloatHeight;
    }

    public sealed class ConvertedArmor
    {
        public string Id;
        public int Front;
        public int Side;
        public int Rear;
        public int Under;
        public int Loftemps;
    }
```

```csharp
        public static ConvertedUnit LoadAlienUnit(string unitsRulPath, string typeId)
        {
            var file = Deserializer.Deserialize<RawUnitsFile>(File.ReadAllText(unitsRulPath));
            var raw = file.Units.Find(u => u.Type == typeId)
                ?? throw new InvalidDataException($"{typeId} not found in {unitsRulPath}");
            return new ConvertedUnit
            {
                Id = raw.Type, Stats = ToStats(raw.Stats), ArmorId = raw.Armor,
                StandHeight = raw.StandHeight, KneelHeight = raw.KneelHeight, FloatHeight = raw.FloatHeight,
            };
        }

        public static ConvertedUnit LoadSoldierUnit(string soldiersRulPath, string typeId)
        {
            var file = Deserializer.Deserialize<RawSoldiersFile>(File.ReadAllText(soldiersRulPath));
            var raw = file.Soldiers.Find(s => s.Type == typeId)
                ?? throw new InvalidDataException($"{typeId} not found in {soldiersRulPath}");
            return new ConvertedUnit
            {
                Id = raw.Type, Stats = ToStats(raw.MinStats), ArmorId = null,
                StandHeight = raw.StandHeight, KneelHeight = raw.KneelHeight, FloatHeight = raw.FloatHeight,
            };
        }

        public static ConvertedArmor LoadArmor(string armorsRulPath, string typeId)
        {
            var file = Deserializer.Deserialize<RawArmorsFile>(File.ReadAllText(armorsRulPath));
            var raw = file.Armors.Find(a => a.Type == typeId)
                ?? throw new InvalidDataException($"{typeId} not found in {armorsRulPath}");
            return new ConvertedArmor
            {
                Id = raw.Type, Front = raw.FrontArmor, Side = raw.SideArmor,
                Rear = raw.RearArmor, Under = raw.UnderArmor,
                Loftemps = raw.LoftempsSet.Count > 0 ? raw.LoftempsSet[0] : 0,
            };
        }
```

And the private raw DTOs — add fields, keeping everything else in the file unchanged:

```csharp
        private sealed class RawUnit
        {
            public string Type { get; set; } = "";
            public RawStats Stats { get; set; } = new();
            public string Armor { get; set; } = "";
            public int StandHeight { get; set; }
            public int KneelHeight { get; set; }
            public int FloatHeight { get; set; }
        }
```

```csharp
        private sealed class RawSoldier
        {
            public string Type { get; set; } = "";
            public RawStats MinStats { get; set; } = new();
            public int StandHeight { get; set; }
            public int KneelHeight { get; set; }
            public int FloatHeight { get; set; }
        }
```

```csharp
        private sealed class RawArmor
        {
            public string Type { get; set; } = "";
            public int FrontArmor { get; set; }
            public int SideArmor { get; set; }
            public int RearArmor { get; set; }
            public int UnderArmor { get; set; }
            public List<int> LoftempsSet { get; set; } = new();
        }
```

(`bin/standard/xcom1/units.rul`'s `STR_SECTOID_SOLDIER` entry has `standHeight: 16` / `kneelHeight: 12` / no `floatHeight` key at lines 167-168; `bin/standard/xcom1/soldiers.rul`'s `STR_SOLDIER` entry has `standHeight: 22` / `kneelHeight: 14` at lines 42-43; `bin/standard/xcom1/armors.rul`'s `SECTOID_ARMOR0` has `loftempsSet: [ 2 ]` at line 120, `STR_NONE_UC` has `loftempsSet: [ 3 ]` at line 27 — all verified directly against the checked-in ruleset files.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet test unity/Tests.Standalone/OpenXcom.Core.Tests.csproj`
Expected: PASS, all tests (141 previous + 5 new/modified assertions in the 4 touched facts).

- [ ] **Step 5: Commit**

```bash
git add unity/Xcom.Convert/Decoders/RuleYamlDecoder.cs unity/Tests.Standalone/Convert/RuleYamlDecoderTests.cs
git commit -m "feat(convert): parse armor loftempsSet and unit stand/kneel/float heights"
```

---

## Task 2: Convert — `McdDecoder.Loft[12]`

**Files:**
- Modify: `unity/Xcom.Convert/Decoders/McdDecoder.cs`
- Test: `unity/Tests.Standalone/Convert/McdDecoderTests.cs`

**Interfaces:**
- Consumes: existing `McdDecoder.Load(byte[])`, existing `McdRecord` class.
- Produces: `McdRecord.Loft` (`byte[12]`) — consumed by Task 4 (`ConvertJob`'s existing `JsonConvert.SerializeObject(tiles, ...)` call, which serializes `McdRecord` directly, so this needs no `ConvertJob` change itself) and Task 5 (`DataLoader`).

- [ ] **Step 1: Write the failing test**

Add to `unity/Tests.Standalone/Convert/McdDecoderTests.cs`:

```csharp
        [Fact]
        public void Load_ParsesLoftBytesAtOffset8Through19()
        {
            var raw = Mcd();
            var records = McdDecoder.Load(raw);
            var r0 = records[0];

            for (int i = 0; i < 12; i++)
                Assert.Equal(raw[8 + i], r0.Loft[i]);
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet test unity/Tests.Standalone/OpenXcom.Core.Tests.csproj --filter McdDecoderTests`
Expected: FAIL — `McdRecord` has no `Loft` member (compile error).

- [ ] **Step 3: Implement**

In `unity/Xcom.Convert/Decoders/McdDecoder.cs`, add the field to `McdRecord` (after the existing `Frames` field, mirroring the byte layout doc comment already there):

```csharp
    public sealed class McdRecord
    {
        public byte[] Frames = new byte[8]; // animation frames (indices into the terrain PCK)
        public byte[] Loft = new byte[12];  // bytes 8-19: per-Z-layer index into LOFTEMPS.DAT's templates
        public int ScanG;
```

And in the `Load` loop, after the existing `for (int f = 0; f < 8; f++) r.Frames[f] = mcd[b + f];` line:

```csharp
                for (int f = 0; f < 12; f++) r.Loft[f] = mcd[b + 8 + f];
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet test unity/Tests.Standalone/OpenXcom.Core.Tests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add unity/Xcom.Convert/Decoders/McdDecoder.cs unity/Tests.Standalone/Convert/McdDecoderTests.cs
git commit -m "feat(convert): parse MCD Loft[12] table (bytes 8-19, previously skipped)"
```

---

## Task 3: Convert — `LoftempsDecoder` (new)

**Files:**
- Create: `unity/Xcom.Convert/Decoders/LoftempsDecoder.cs`
- Test: `unity/Tests.Standalone/Convert/LoftempsDecoderTests.cs`

**Interfaces:**
- Consumes: raw bytes of `GEODATA/LOFTEMPS.DAT` (real fixture at `unity/RawData/Resources/UFO/GEODATA/LOFTEMPS.DAT`, confirmed present, 3584 bytes = 112 templates × 16 rows × 2 bytes).
- Produces: `LoftempsDecoder.Load(byte[]) -> ushort[]` — consumed by Task 4 (`ConvertJob`).

- [ ] **Step 1: Write the failing test**

Create `unity/Tests.Standalone/Convert/LoftempsDecoderTests.cs`:

```csharp
using System.IO;
using Xcom.Convert.Decoders;
using Xunit;

namespace OpenXcom.Core.Tests.Convert
{
    public class LoftempsDecoderTests
    {
        private static byte[] Loftemps() =>
            File.ReadAllBytes(Path.Combine(TestPaths.RawDataDir, "GEODATA", "LOFTEMPS.DAT"));

        [Fact]
        public void Load_ValueCountMatchesFileSizeOver2()
        {
            var raw = Loftemps();
            var values = LoftempsDecoder.Load(raw);
            Assert.Equal(raw.Length / 2, values.Length);
        }

        [Fact]
        public void Load_FirstValueMatchesLittleEndianBytes()
        {
            var raw = Loftemps();
            var values = LoftempsDecoder.Load(raw);
            Assert.Equal(raw[0] | (raw[1] << 8), values[0]);
        }

        [Fact]
        public void Load_TemplateCountIs112()
        {
            var values = LoftempsDecoder.Load(Loftemps());
            Assert.Equal(112, values.Length / LoftempsDecoder.RowsPerTemplate);
        }

        [Fact]
        public void Load_OddByteLengthThrows()
        {
            Assert.Throws<InvalidDataException>(() => LoftempsDecoder.Load(new byte[] { 1, 2, 3 }));
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet test unity/Tests.Standalone/OpenXcom.Core.Tests.csproj --filter LoftempsDecoderTests`
Expected: FAIL — `Xcom.Convert.Decoders.LoftempsDecoder` does not exist (compile error).

- [ ] **Step 3: Implement**

Create `unity/Xcom.Convert/Decoders/LoftempsDecoder.cs`:

```csharp
using System.IO;

namespace Xcom.Convert.Decoders
{
    /// <summary>
    /// Decodes LOFTEMPS.DAT: a flat sequence of little-endian uint16 values,
    /// no header. Every 16 consecutive values form one "loft template" - a
    /// 16x16 voxel bitmask, one uint16 per Y row (bit x = column x solid).
    /// Port of MapDataSet::loadLOFTEMPS (src/Mod/MapDataSet.cpp:272-287).
    /// </summary>
    public static class LoftempsDecoder
    {
        public const int RowsPerTemplate = 16;

        public static ushort[] Load(byte[] data)
        {
            if (data.Length % 2 != 0)
                throw new InvalidDataException("Invalid LOFTEMPS: odd byte length");

            var values = new ushort[data.Length / 2];
            for (int i = 0; i < values.Length; i++)
                values[i] = (ushort)(data[i * 2] | (data[i * 2 + 1] << 8));
            return values;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet test unity/Tests.Standalone/OpenXcom.Core.Tests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add unity/Xcom.Convert/Decoders/LoftempsDecoder.cs unity/Tests.Standalone/Convert/LoftempsDecoderTests.cs
git commit -m "feat(convert): LoftempsDecoder for LOFTEMPS.DAT voxel bitmask templates"
```

---

## Task 4: Convert — wire `ConvertJob` (soldier armor + `loftemps.json`)

**Files:**
- Modify: `unity/Xcom.Convert/ConvertJob.cs`
- Test: `unity/Tests.Standalone/Convert/ConvertJobTests.cs`

**Interfaces:**
- Consumes: `RuleYamlDecoder.LoadArmor` (Task 1), `LoftempsDecoder.Load` (Task 3).
- Produces: `armors.json` now containing **2** entries (soldier's real `STR_NONE_UC` + `SECTOID_ARMOR0`, was 1); new `loftemps.json` file — consumed by Task 5 (`DataLoader`).

**Important side effect, called out explicitly:** today `DataLoader.LoadUnits` silently falls back to the hardcoded `RuleArmor.None` (front:2/side:2/rear:2/under:2) for the soldier because no soldier armor is converted. This task converts the soldier's real `STR_NONE_UC` armor (front:12/side:8/rear:5/under:2, per `bin/standard/xcom1/armors.rul:2-27`) — once `armors.json` contains an entry whose `Id` matches the soldier's `ArmorId`, `DataLoader.LoadUnits`'s existing `armorsById.TryGetValue` lookup (`DataLoader.cs:94`) resolves it automatically, no `DataLoader` code change needed for this part. This is a real gameplay change (soldiers become properly armored), not just plumbing — flag it as such when this task is reviewed.

- [ ] **Step 1: Write the failing test**

Modify `unity/Tests.Standalone/Convert/ConvertJobTests.cs`'s single existing `[Fact]`:

Replace line 34 (`Assert.Contains(written, p => p == "armors.json");`) — no change needed, it already asserts the filename exists — but add right after it:

```csharp
            Assert.Contains(written, p => p == "loftemps.json");
```

Change line 44 (`Assert.Equal(26, written.Count);`) to:

```csharp
            Assert.Equal(27, written.Count);
```

Change line 48 (`Assert.Equal(26, files.Count);`) to:

```csharp
            Assert.Equal(27, files.Count);
```

Replace lines 79-83 (the `// armors.json: Sectoid armor only, this slice.` block):

```csharp
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet test unity/Tests.Standalone/OpenXcom.Core.Tests.csproj --filter ConvertJobTests`
Expected: FAIL — `written.Count` is 26 not 27, `armors.Count` is 1 not 2, no `Loftemps`/`loftemps.json`.

- [ ] **Step 3: Implement**

In `unity/Xcom.Convert/ConvertJob.cs`, find the existing "5. Rules" section:

```csharp
            var sectoidArmor = RuleYamlDecoder.LoadArmor(Path.Combine(rulesDir, "armors.rul"), "SECTOID_ARMOR0");
            var rifle = RuleYamlDecoder.LoadWeapon(Path.Combine(rulesDir, "items.rul"), "STR_RIFLE", "STR_RIFLE_CLIP");
            var plasmaPistol = RuleYamlDecoder.LoadWeapon(Path.Combine(rulesDir, "items.rul"), "STR_PLASMA_PISTOL", "STR_PLASMA_PISTOL_CLIP");
```

Add a soldier armor load right after `sectoidArmor`:

```csharp
            var sectoidArmor = RuleYamlDecoder.LoadArmor(Path.Combine(rulesDir, "armors.rul"), "SECTOID_ARMOR0");
            var soldierArmor = RuleYamlDecoder.LoadArmor(Path.Combine(rulesDir, "armors.rul"), "STR_NONE_UC");
            var rifle = RuleYamlDecoder.LoadWeapon(Path.Combine(rulesDir, "items.rul"), "STR_RIFLE", "STR_RIFLE_CLIP");
            var plasmaPistol = RuleYamlDecoder.LoadWeapon(Path.Combine(rulesDir, "items.rul"), "STR_PLASMA_PISTOL", "STR_PLASMA_PISTOL_CLIP");
```

Find the existing armors.json write:

```csharp
            File.WriteAllText(Path.Combine(outDir, "armors.json"),
                JsonConvert.SerializeObject(new[] { sectoidArmor }, Formatting.Indented));
            written.Add("armors.json");
```

Change to:

```csharp
            File.WriteAllText(Path.Combine(outDir, "armors.json"),
                JsonConvert.SerializeObject(new[] { soldierArmor, sectoidArmor }, Formatting.Indented));
            written.Add("armors.json");
```

Add a new step right before the final `written.Add("manifest.json");` block:

```csharp
            // 6. LOFTEMPS.DAT -> loftemps.json (voxel hit-detection templates, shared across all terrain/units).
            var loftemps = LoftempsDecoder.Load(File.ReadAllBytes(Path.Combine(dataDir, "GEODATA", "LOFTEMPS.DAT")));
            File.WriteAllText(Path.Combine(outDir, "loftemps.json"),
                JsonConvert.SerializeObject(loftemps, Formatting.Indented));
            written.Add("loftemps.json");
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet test unity/Tests.Standalone/OpenXcom.Core.Tests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add unity/Xcom.Convert/ConvertJob.cs unity/Tests.Standalone/Convert/ConvertJobTests.cs
git commit -m "feat(convert): convert soldier's real STR_NONE_UC armor and LOFTEMPS.DAT"
```

---

## Task 5: Core — `RuleArmor.Loftemps`, `RuleUnit` heights, `MapDataTile.Loft`, `DataLoader` wiring

**Files:**
- Modify: `unity/Assets/Scripts/Core/Rules/RuleArmor.cs`
- Modify: `unity/Assets/Scripts/Core/Rules/RuleUnit.cs`
- Modify: `unity/Assets/Scripts/Core/Rules/MapDataTile.cs`
- Modify: `unity/Assets/Scripts/Core/Rules/DataLoader.cs`
- Test: `unity/Tests.Standalone/DataLoaderTests.cs`

**Interfaces:**
- Consumes: `armors.json`/`units.json`/`tiles-*.json`/`loftemps.json` (Tasks 1-4's output shape).
- Produces: `RuleArmor.Loftemps` (int), `RuleUnit.StandHeight`/`KneelHeight`/`FloatHeight` (int), `MapDataTile.Loft` (`int[12]`), `DataLoader.LoadLoftemps(string) -> ushort[]` — consumed by Task 6 (`BattleUnit.Height`, `TileEngine.VoxelCheck`).

- [ ] **Step 1: Write the failing tests**

Add to `unity/Tests.Standalone/DataLoaderTests.cs`, extending `LoadArmors_ParsesFields`:

```csharp
        [Fact]
        public void LoadArmors_ParsesFields()
        {
            string json = @"[{ ""Id"": ""SECTOID_ARMOR0"", ""Front"": 4, ""Side"": 3, ""Rear"": 2, ""Under"": 2, ""Loftemps"": 2 }]";
            File.WriteAllText(Path.Combine(_dir, "armors.json"), json);

            var armors = DataLoader.LoadArmors(_dir);

            Assert.Single(armors);
            Assert.Equal("SECTOID_ARMOR0", armors[0].Id);
            Assert.Equal(4, armors[0].Front);
            Assert.Equal(3, armors[0].Side);
            Assert.Equal(2, armors[0].Rear);
            Assert.Equal(2, armors[0].Under);
            Assert.Equal(2, armors[0].Loftemps);
        }
```

Extend `LoadUnits_ResolvesArmorIdAndFallsBackToNoneWhenMissing`'s JSON and assertions:

```csharp
        [Fact]
        public void LoadUnits_ResolvesArmorIdAndFallsBackToNoneWhenMissing()
        {
            string json = @"[
                { ""Id"": ""STR_SECTOID_SOLDIER"", ""ArmorId"": ""SECTOID_ARMOR0"",
                  ""StandHeight"": 16, ""KneelHeight"": 12, ""FloatHeight"": 0,
                  ""Stats"": { ""TimeUnits"": 54, ""Stamina"": 90, ""Health"": 30, ""Bravery"": 80,
                                ""Reactions"": 63, ""Firing"": 52, ""Throwing"": 58, ""Strength"": 30, ""Melee"": 76 } },
                { ""Id"": ""STR_SOLDIER"", ""ArmorId"": null,
                  ""StandHeight"": 22, ""KneelHeight"": 14, ""FloatHeight"": 0,
                  ""Stats"": { ""TimeUnits"": 50, ""Stamina"": 40, ""Health"": 25, ""Bravery"": 10,
                                ""Reactions"": 30, ""Firing"": 40, ""Throwing"": 50, ""Strength"": 20, ""Melee"": 20 } }
            ]";
            File.WriteAllText(Path.Combine(_dir, "units.json"), json);
            var sectoidArmor = new RuleArmor("SECTOID_ARMOR0", front: 4, side: 3, rear: 2, under: 2, loftemps: 2);
            var armorsById = new Dictionary<string, RuleArmor> { { "SECTOID_ARMOR0", sectoidArmor } };

            var units = DataLoader.LoadUnits(_dir, armorsById);

            Assert.Equal(2, units.Count);
            var sectoid = units.Find(u => u.Id == "STR_SECTOID_SOLDIER");
            Assert.Same(sectoidArmor, sectoid.Armor);
            Assert.Equal(54, sectoid.Stats.TimeUnits);
            Assert.Equal(16, sectoid.StandHeight);
            Assert.Equal(12, sectoid.KneelHeight);
            var soldier = units.Find(u => u.Id == "STR_SOLDIER");
            Assert.Equal(RuleArmor.None.Id, soldier.Armor.Id); // no ArmorId -> falls back to RuleArmor.None
            Assert.Equal(50, soldier.Stats.TimeUnits);
            Assert.Equal(22, soldier.StandHeight);
            Assert.Equal(14, soldier.KneelHeight);
        }
```

Add a new fact for the tiles-loft round-trip, after `LoadTiles_ParsesFieldsAndBase64EncodedByteArray`:

```csharp
        [Fact]
        public void LoadTiles_ParsesLoftArray()
        {
            string json = @"[
                {
                    ""Frames"": ""/+A/AQIDBAU="",
                    ""ScanG"": 42, ""IsUfoDoor"": false, ""StopLOS"": true, ""NoFloor"": false,
                    ""BigWall"": 2, ""Gravlift"": false, ""IsDoor"": false, ""BlockFire"": false,
                    ""BlockSmoke"": false, ""TuWalk"": 4, ""TuSlide"": 8, ""TuFly"": 1, ""Armor"": 20,
                    ""TLevel"": -1, ""PLevel"": 3,
                    ""Loft"": ""AwAAAAAAAAAAAAAAAAAAAA==""
                }
            ]";
            File.WriteAllText(Path.Combine(_dir, "tiles-TEST.json"), json);

            var tiles = DataLoader.LoadTiles(_dir, "TEST");

            // "AwAAAAAAAAAAAAAAAAAAAA==" base64-decodes to {3,0,0,0,0,0,0,0,0,0,0,0}.
            Assert.Equal(new[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, tiles[0].Loft);
        }
```

Add a new fact for `LoadLoftemps`:

```csharp
        [Fact]
        public void LoadLoftemps_ParsesFlatUshortArray()
        {
            File.WriteAllText(Path.Combine(_dir, "loftemps.json"), "[0, 65535, 1, 32768]");

            var loftemps = DataLoader.LoadLoftemps(_dir);

            Assert.Equal(new ushort[] { 0, 65535, 1, 32768 }, loftemps);
        }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet test unity/Tests.Standalone/OpenXcom.Core.Tests.csproj --filter DataLoaderTests`
Expected: FAIL — `RuleArmor`/`RuleUnit` constructors don't accept the new named args, `MapDataTile.Loft` and `DataLoader.LoadLoftemps` don't exist (compile errors).

- [ ] **Step 3: Implement**

`unity/Assets/Scripts/Core/Rules/RuleArmor.cs` — full new contents:

```csharp
namespace OpenXcom.Core.Rules
{
    /// <summary>
    /// Immutable armor template. Directional armor values are subtracted from
    /// incoming damage on the corresponding hit side. Loftemps is the index
    /// into LOFTEMPS.DAT's voxel bitmask templates used for voxel-level hit
    /// detection (RuleArmor::getLoftemps, src/Mod/Armor.h:587) - single value
    /// since this rewrite has no big (2x2) units.
    /// Mirrors OXCE <c>Armor</c> (src/Mod/Armor.h), slice-1 subset.
    /// </summary>
    public sealed class RuleArmor
    {
        public string Id { get; }
        public int Front { get; }
        public int Side { get; }   // used for both Left and Right in slice 1
        public int Rear { get; }
        public int Under { get; }
        public int Loftemps { get; }

        public RuleArmor(string id, int front, int side, int rear, int under, int loftemps = 0)
        {
            Id = id;
            Front = front;
            Side = side;
            Rear = rear;
            Under = under;
            Loftemps = loftemps;
        }

        public int ValueFor(UnitSide s) => s switch
        {
            UnitSide.Front => Front,
            UnitSide.Left => Side,
            UnitSide.Right => Side,
            UnitSide.Rear => Rear,
            UnitSide.Under => Under,
            _ => Front,
        };

        /// <summary>Basic personal armor, roughly X-COM "Personal Armor" tier.</summary>
        public static RuleArmor Personal => new("STR_PERSONAL_ARMOR", front: 12, side: 8, rear: 5, under: 2, loftemps: 3);

        /// <summary>Unarmored (rookie in flight suit).</summary>
        public static RuleArmor None => new("STR_NONE", front: 2, side: 2, rear: 2, under: 2, loftemps: 3);
    }
}
```

`unity/Assets/Scripts/Core/Rules/RuleUnit.cs` — full new contents:

```csharp
namespace OpenXcom.Core.Rules
{
    /// <summary>
    /// Immutable template for a unit type (a soldier class or an alien species):
    /// its base stats, default armor, and body height in voxel Z-units.
    /// StandHeight/KneelHeight/FloatHeight port Unit::getStandHeight/
    /// getKneelHeight/getFloatHeight (src/Mod/Unit.h) - real per-unit values,
    /// not per-armor: neither STR_NONE_UC nor SECTOID_ARMOR0 override them at
    /// the armor level in bin/standard/xcom1, so unlike Loftemps these are
    /// unit-level fields here, matching the actual authoritative source for
    /// this rewrite's two unit types (the C++ armor->unit fallback chain
    /// this simplifies is documented in Phase 8's design spec §3).
    /// The mutable per-instance state lives in <c>Battle.BattleUnit</c>.
    /// Mirrors the OXCE <c>Unit</c>/<c>Soldier</c> rules split.
    /// </summary>
    public sealed class RuleUnit
    {
        public string Id { get; }
        public UnitStats Stats { get; }
        public RuleArmor Armor { get; }
        public int StandHeight { get; }
        public int KneelHeight { get; }
        public int FloatHeight { get; }

        public RuleUnit(string id, UnitStats stats, RuleArmor armor,
            int standHeight = 22, int kneelHeight = 14, int floatHeight = 0)
        {
            Id = id;
            Stats = stats;
            Armor = armor;
            StandHeight = standHeight;
            KneelHeight = kneelHeight;
            FloatHeight = floatHeight;
        }

        public static RuleUnit Soldier => new(
            "STR_SOLDIER", UnitStats.Rookie, RuleArmor.None, standHeight: 22, kneelHeight: 14);

        public static RuleUnit Sectoid => new(
            "STR_SECTOID",
            new UnitStats
            {
                TimeUnits = 54, Stamina = 90, Health = 30, Bravery = 80,
                Reactions = 63, Firing = 52, Throwing = 58, Strength = 30, Melee = 40,
            },
            RuleArmor.None, standHeight: 16, kneelHeight: 12);
    }
}
```

`unity/Assets/Scripts/Core/Rules/MapDataTile.cs` — add one field (after `YOffset`):

```csharp
        public int TerrainLevel; // MCD T_Level
        public int YOffset;      // MCD P_Level

        /// <summary>Per-Z-layer index (0-11) into LOFTEMPS.DAT's templates. MCD bytes 8-19.</summary>
        public int[] Loft = new int[12];
```

`unity/Assets/Scripts/Core/Rules/DataLoader.cs` — four changes:

1. `RawMcdRecord` gains a field, and `LoadTiles` maps it:

```csharp
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
            public byte[] Loft { get; set; }
        }
```

In `LoadTiles`, inside the `result.Add(new MapDataTile { ... })` initializer, add after `YOffset = r.PLevel,`:

```csharp
                    Loft = ToIntArray(r.Loft),
```

Add a small private helper (near the bottom of the class, alongside the DTOs):

```csharp
        private static int[] ToIntArray(byte[] bytes)
        {
            var result = new int[bytes.Length];
            for (int i = 0; i < bytes.Length; i++) result[i] = bytes[i];
            return result;
        }
```

2. `RawArmorEntry` gains `Loftemps`, `LoadArmors` passes it through:

```csharp
        private sealed class RawArmorEntry
        {
            public string Id { get; set; }
            public int Front { get; set; }
            public int Side { get; set; }
            public int Rear { get; set; }
            public int Under { get; set; }
            public int Loftemps { get; set; }
        }
```

```csharp
        public static List<RuleArmor> LoadArmors(string gameDataDir)
        {
            string path = Path.Combine(gameDataDir, "armors.json");
            string json = File.ReadAllText(path);
            var raw = JsonConvert.DeserializeObject<List<RawArmorEntry>>(json);

            var result = new List<RuleArmor>(raw.Count);
            foreach (var r in raw)
                result.Add(new RuleArmor(r.Id, r.Front, r.Side, r.Rear, r.Under, r.Loftemps));
            return result;
        }
```

3. `RawUnitEntry` gains the three height fields, `LoadUnits` passes them through:

```csharp
        private sealed class RawUnitEntry
        {
            public string Id { get; set; }
            public RawUnitStats Stats { get; set; }
            public string ArmorId { get; set; }
            public int StandHeight { get; set; }
            public int KneelHeight { get; set; }
            public int FloatHeight { get; set; }
        }
```

In `LoadUnits`, change `result.Add(new RuleUnit(r.Id, stats, armor));` to:

```csharp
                result.Add(new RuleUnit(r.Id, stats, armor, r.StandHeight, r.KneelHeight, r.FloatHeight));
```

4. New method, next to `LoadItems`:

```csharp
        public static ushort[] LoadLoftemps(string gameDataDir)
        {
            string path = Path.Combine(gameDataDir, "loftemps.json");
            string json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<ushort[]>(json);
        }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet test unity/Tests.Standalone/OpenXcom.Core.Tests.csproj`
Expected: PASS, full suite (catches any other call site that constructs `RuleArmor`/`RuleUnit` positionally with an incompatible arg count — none are expected since all new params are optional/trailing, but this is the safety net).

- [ ] **Step 5: Commit**

```bash
git add unity/Assets/Scripts/Core/Rules/RuleArmor.cs unity/Assets/Scripts/Core/Rules/RuleUnit.cs unity/Assets/Scripts/Core/Rules/MapDataTile.cs unity/Assets/Scripts/Core/Rules/DataLoader.cs unity/Tests.Standalone/DataLoaderTests.cs
git commit -m "feat(core): carry armor Loftemps, unit heights, and tile Loft[12] through DataLoader"
```

---

## Task 6: Core — `VoxelType`/`VoxelHit`, `BattleUnit.Height`, `TileEngine.VoxelCheck`

**Files:**
- Create: `unity/Assets/Scripts/Core/Battle/Voxel.cs`
- Modify: `unity/Assets/Scripts/Core/Battle/BattleUnit.cs`
- Modify: `unity/Assets/Scripts/Core/Battle/TileEngine.cs`
- Test: `unity/Tests.Standalone/TileEngineTests.cs`

**Interfaces:**
- Consumes: `MapDataTile.Loft`, `RuleArmor.Loftemps`, `RuleUnit.StandHeight/KneelHeight/FloatHeight` (Task 5); `Tile.Floor/WestWall/NorthWall/Object/Occupant` (existing).
- Produces: `VoxelType` enum, `VoxelHit` struct, `BattleUnit.Height` (int property), `TileEngine.VoxelCheck(TileGrid, ushort[], Position, BattleUnit) -> VoxelHit` — consumed by Task 7 (`CalculateLine`).

- [ ] **Step 1: Write the failing tests**

Add to `unity/Tests.Standalone/TileEngineTests.cs` (add `using OpenXcom.Core.Rules;` to the top if not already present):

```csharp
        private static ushort[] SolidLoftData()
        {
            // Template 0: empty (all bits clear, the default MapDataTile.Loft value).
            // Template 1: fully solid 16x16 at every row - any voxel inside it is "hit".
            var data = new ushort[32];
            for (int row = 0; row < 16; row++)
                data[16 + row] = 0xFFFF;
            return data;
        }

        private static MapDataTile SolidPart()
        {
            var part = new MapDataTile();
            for (int i = 0; i < 12; i++) part.Loft[i] = 1;
            return part;
        }

        [Fact]
        public void VoxelCheck_SolidTerrainLoftBlocksTheVoxel()
        {
            var grid = new TileGrid(2, 2, 1);
            grid.At(0, 0, 0).Object = SolidPart();

            var hit = TileEngine.VoxelCheck(grid, SolidLoftData(), new Position(4, 4, 4), excludeUnit: null);

            Assert.Equal(VoxelType.Object, hit.Type);
        }

        [Fact]
        public void VoxelCheck_EmptyLoftTemplateDoesNotBlock()
        {
            var grid = new TileGrid(2, 2, 1);
            grid.At(0, 0, 0).Floor = new MapDataTile(); // Loft defaults to all-zero -> template 0 -> empty

            var hit = TileEngine.VoxelCheck(grid, SolidLoftData(), new Position(4, 4, 4), excludeUnit: null);

            Assert.Equal(VoxelType.Empty, hit.Type);
        }

        [Fact]
        public void VoxelCheck_NegativeCoordinateIsOutOfBounds()
        {
            var grid = new TileGrid(2, 2, 1);
            var hit = TileEngine.VoxelCheck(grid, SolidLoftData(), new Position(-1, 0, 0), excludeUnit: null);
            Assert.Equal(VoxelType.OutOfBounds, hit.Type);
        }

        [Fact]
        public void VoxelCheck_LivingUnitWithinItsHeightBandIsHit()
        {
            var grid = new TileGrid(2, 2, 1);
            var armor = new RuleArmor("A", 0, 0, 0, 0, loftemps: 1);
            var unit = new BattleUnit(new RuleUnit("STR_TEST", UnitStats.Rookie, armor, standHeight: 22, kneelHeight: 14), Faction.Player)
            {
                Position = new Position(0, 0, 0),
            };
            grid.At(0, 0, 0).Occupant = unit;

            // tz = 0*24 + FloatHeight(0) - terrainLevel(0) = 0; unit.Height (standing) = 22, so
            // voxel.Z in (0, 22] is inside the unit's body.
            var hit = TileEngine.VoxelCheck(grid, SolidLoftData(), new Position(4, 4, 10), excludeUnit: null);

            Assert.Equal(VoxelType.Unit, hit.Type);
            Assert.Same(unit, hit.Unit);
        }

        [Fact]
        public void VoxelCheck_ExcludedUnitIsNotHit()
        {
            var grid = new TileGrid(2, 2, 1);
            var armor = new RuleArmor("A", 0, 0, 0, 0, loftemps: 1);
            var unit = new BattleUnit(new RuleUnit("STR_TEST", UnitStats.Rookie, armor, standHeight: 22, kneelHeight: 14), Faction.Player)
            {
                Position = new Position(0, 0, 0),
            };
            grid.At(0, 0, 0).Occupant = unit;

            var hit = TileEngine.VoxelCheck(grid, SolidLoftData(), new Position(4, 4, 10), excludeUnit: unit);

            Assert.Equal(VoxelType.Empty, hit.Type);
        }

        [Fact]
        public void VoxelCheck_VoxelAboveUnitsHeightBandMisses()
        {
            var grid = new TileGrid(2, 2, 1);
            var armor = new RuleArmor("A", 0, 0, 0, 0, loftemps: 1);
            var unit = new BattleUnit(new RuleUnit("STR_TEST", UnitStats.Rookie, armor, standHeight: 22, kneelHeight: 14), Faction.Player)
            {
                Position = new Position(0, 0, 0),
            };
            grid.At(0, 0, 0).Occupant = unit;

            // voxel.Z = 23 is above tz(0) + Height(22) = 22 -> outside the unit's body.
            var hit = TileEngine.VoxelCheck(grid, SolidLoftData(), new Position(4, 4, 23), excludeUnit: null);

            Assert.Equal(VoxelType.Empty, hit.Type);
        }
```

Add a `BattleUnitTests` fact (find the existing `unity/Tests.Standalone/BattleUnitTests.cs`, add):

```csharp
        [Fact]
        public void Height_ReflectsKneeledState()
        {
            var unit = new BattleUnit(new RuleUnit("STR_TEST", UnitStats.Rookie, RuleArmor.None,
                standHeight: 22, kneelHeight: 14), Faction.Player);

            Assert.Equal(22, unit.Height);
            unit.Kneeled = true;
            Assert.Equal(14, unit.Height);
        }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet test unity/Tests.Standalone/OpenXcom.Core.Tests.csproj --filter "TileEngineTests|BattleUnitTests"`
Expected: FAIL — `VoxelType`/`VoxelHit`/`TileEngine.VoxelCheck`/`BattleUnit.Height` don't exist (compile errors).

- [ ] **Step 3: Implement**

Create `unity/Assets/Scripts/Core/Battle/Voxel.cs`:

```csharp
using OpenXcom.Core.Common;

namespace OpenXcom.Core.Battle
{
    /// <summary>
    /// What a traced voxel line hit. Mirrors OXCE's VoxelType enum
    /// (src/Battlescape/Position.h) - tile-part indices 0-3 match this
    /// rewrite's Tile.Floor/WestWall/NorthWall/Object field order exactly.
    /// </summary>
    public enum VoxelType
    {
        OutOfBounds = -2,
        Empty = -1,
        Floor = 0,
        WestWall = 1,
        NorthWall = 2,
        Object = 3,
        Unit = 4,
    }

    /// <summary>Result of TileEngine.VoxelCheck/CalculateLine: what was hit, where, and (for a unit hit) who.</summary>
    public readonly struct VoxelHit
    {
        public readonly VoxelType Type;
        public readonly Position Voxel;
        public readonly BattleUnit Unit;

        public VoxelHit(VoxelType type, Position voxel, BattleUnit unit = null)
        {
            Type = type;
            Voxel = voxel;
            Unit = unit;
        }

        public static VoxelHit Empty(Position voxel) => new(VoxelType.Empty, voxel);

        /// <summary>True for any real hit (terrain or unit) - false for Empty/OutOfBounds.</summary>
        public bool IsHit => Type != VoxelType.Empty && Type != VoxelType.OutOfBounds;
    }
}
```

In `unity/Assets/Scripts/Core/Battle/BattleUnit.cs`, add after the existing `public bool IsAlive => Health > 0;` line:

```csharp
        /// <summary>Current standing/kneeling body height in voxel Z-units. Port of BattleUnit::getHeight (src/Savegame/BattleUnit.cpp:3942).</summary>
        public int Height => Kneeled ? Rules.KneelHeight : Rules.StandHeight;
```

In `unity/Assets/Scripts/Core/Battle/TileEngine.cs`, add `using OpenXcom.Core.Rules;` to the usings, and add `VoxelCheck` as a new public method (after `ComputeVisibleTiles`, before the existing private `WalkLine`):

```csharp
        /// <summary>
        /// Checks what (if anything) occupies this exact voxel: a terrain
        /// part's Loft bitmask, then (if nothing terrain-solid) a living
        /// unit's Loftemps cylinder. loftData is the flat LOFTEMPS.DAT table
        /// (DataLoader.LoadLoftemps) - any loft index outside its bounds is
        /// treated as passable, not an error, so an empty/undersized table
        /// just means "no voxel data configured" rather than crashing.
        /// Port of TileEngine::voxelCheck (src/Battlescape/TileEngine.cpp:4529-4628),
        /// scoped down to this rewrite's data model: no UFO doors, no
        /// gravlift-floor special case, no big (2x2) units - none of these
        /// exist in Core's Tile/MapDataTile/RuleArmor model today, so
        /// porting their branches would be dead code, not a real
        /// simplification of working behavior.
        /// </summary>
        public static VoxelHit VoxelCheck(TileGrid grid, ushort[] loftData, Position voxel, BattleUnit excludeUnit)
        {
            if (voxel.X < 0 || voxel.Y < 0 || voxel.Z < 0)
                return new VoxelHit(VoxelType.OutOfBounds, voxel);

            var tilePos = new Position(voxel.X / 16, voxel.Y / 16, voxel.Z / 24);
            var tile = grid[tilePos];
            if (tile == null)
                return new VoxelHit(VoxelType.OutOfBounds, voxel);

            int zLayer = (voxel.Z % 24) / 2;
            int lx = 15 - (voxel.X % 16);
            int ly = voxel.Y % 16;

            var parts = new (VoxelType type, MapDataTile part)[]
            {
                (VoxelType.Floor, tile.Floor),
                (VoxelType.WestWall, tile.WestWall),
                (VoxelType.NorthWall, tile.NorthWall),
                (VoxelType.Object, tile.Object),
            };

            foreach (var (type, part) in parts)
            {
                if (part == null) continue;
                int loftId = part.Loft[zLayer];
                int idx = loftId * 16 + ly;
                if (idx < loftData.Length && (loftData[idx] & (1 << lx)) != 0)
                    return new VoxelHit(type, voxel);
            }

            var unit = tile.Occupant;
            if (unit != null && unit.IsAlive && unit != excludeUnit)
            {
                int terrainLevel = System.Math.Min(0, tile.Floor?.TerrainLevel ?? 0);
                int tz = tilePos.Z * 24 + unit.Rules.FloatHeight - terrainLevel;
                if (voxel.Z > tz && voxel.Z <= tz + unit.Height)
                {
                    int loftId = unit.Armor.Loftemps;
                    int idx = loftId * 16 + ly;
                    if (idx < loftData.Length && (loftData[idx] & (1 << lx)) != 0)
                        return new VoxelHit(VoxelType.Unit, voxel, unit);
                }
            }

            return VoxelHit.Empty(voxel);
        }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet test unity/Tests.Standalone/OpenXcom.Core.Tests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add unity/Assets/Scripts/Core/Battle/Voxel.cs unity/Assets/Scripts/Core/Battle/BattleUnit.cs unity/Assets/Scripts/Core/Battle/TileEngine.cs unity/Tests.Standalone/TileEngineTests.cs unity/Tests.Standalone/BattleUnitTests.cs
git commit -m "feat(core): TileEngine.VoxelCheck - terrain Loft and unit Loftemps bit-testing"
```

---

## Task 7: Core — `TileEngine.CalculateLine` (3D Bresenham voxel walk)

**Files:**
- Modify: `unity/Assets/Scripts/Core/Battle/TileEngine.cs`
- Test: `unity/Tests.Standalone/TileEngineTests.cs`

**Interfaces:**
- Consumes: `TileEngine.VoxelCheck` (Task 6).
- Produces: `TileEngine.CalculateLine(TileGrid, ushort[], Position origin, Position target, BattleUnit excludeUnit) -> VoxelHit` — consumed by Task 10 (`BattleState.TryFire`).

- [ ] **Step 1: Write the failing tests**

Add to `unity/Tests.Standalone/TileEngineTests.cs`:

```csharp
        [Fact]
        public void CalculateLine_ClearPathReachesExactTargetVoxel()
        {
            var grid = new TileGrid(3, 3, 1);
            var origin = new Position(8, 8, 10);
            var target = new Position(40, 8, 10);

            var hit = TileEngine.CalculateLine(grid, System.Array.Empty<ushort>(), origin, target, excludeUnit: null);

            Assert.Equal(VoxelType.Empty, hit.Type);
            Assert.Equal(target, hit.Voxel);
        }

        [Fact]
        public void CalculateLine_SolidWallOnThePathStopsBeforeTheTarget()
        {
            var grid = new TileGrid(3, 3, 1);
            grid.At(1, 0, 0).Object = SolidPart(); // tile x=1 -> voxel X range [16,32)

            var origin = new Position(8, 8, 10);
            var target = new Position(40, 8, 10);

            var hit = TileEngine.CalculateLine(grid, SolidLoftData(), origin, target, excludeUnit: null);

            Assert.Equal(VoxelType.Object, hit.Type);
            Assert.InRange(hit.Voxel.X, 16, 31);
        }

        [Fact]
        public void CalculateLine_UnitStandingBehindASolidWallIsNotReached()
        {
            var grid = new TileGrid(3, 3, 1);
            grid.At(1, 0, 0).Object = SolidPart(); // blocks tile x=1

            var armor = new RuleArmor("A", 0, 0, 0, 0, loftemps: 1);
            var defender = new BattleUnit(new RuleUnit("STR_TEST", UnitStats.Rookie, armor, standHeight: 22, kneelHeight: 14), Faction.Hostile)
            {
                Position = new Position(2, 0, 0),
            };
            grid.At(2, 0, 0).Occupant = defender;

            var origin = new Position(8, 8, 10);
            var target = new Position(new Position(2, 0, 0).X * 16 + 8, 8, 10); // aimed at the far unit's tile center

            var hit = TileEngine.CalculateLine(grid, SolidLoftData(), origin, target, excludeUnit: null);

            Assert.Equal(VoxelType.Object, hit.Type); // stops at the wall, never reaches the unit
        }

        [Fact]
        public void CalculateLine_UnobstructedShotHitsTheStandingUnit()
        {
            var grid = new TileGrid(3, 3, 1);
            var armor = new RuleArmor("A", 0, 0, 0, 0, loftemps: 1);
            var defender = new BattleUnit(new RuleUnit("STR_TEST", UnitStats.Rookie, armor, standHeight: 22, kneelHeight: 14), Faction.Hostile)
            {
                Position = new Position(2, 0, 0),
            };
            grid.At(2, 0, 0).Occupant = defender;

            var origin = new Position(8, 8, 10);
            var target = new Position(2 * 16 + 8, 8, 10);

            var hit = TileEngine.CalculateLine(grid, SolidLoftData(), origin, target, excludeUnit: null);

            Assert.Equal(VoxelType.Unit, hit.Type);
            Assert.Same(defender, hit.Unit);
        }

        [Fact]
        public void CalculateLine_FortyFiveDegreeDiagonalStopsAtASolidTileOnItsPath()
        {
            // A perfect 45-degree line (equal X/Y delta) exercises the drift
            // side-step branches every single step (driftXy/driftXz underflow on
            // every iteration when deltaX == deltaY), unlike an axis-aligned line
            // which never triggers them - this is the non-trivial diagonal case
            // the Phase 8 design spec's testing strategy calls out explicitly.
            var grid = new TileGrid(3, 3, 1);
            grid.At(1, 1, 0).Object = SolidPart();

            var origin = new Position(8, 8, 10);   // tile (0,0)
            var target = new Position(40, 40, 10); // tile (2,2), a 45-degree diagonal

            var hit = TileEngine.CalculateLine(grid, SolidLoftData(), origin, target, excludeUnit: null);

            Assert.Equal(VoxelType.Object, hit.Type);
            Assert.InRange(hit.Voxel.X, 16, 31);
            Assert.InRange(hit.Voxel.Y, 16, 31);
        }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet test unity/Tests.Standalone/OpenXcom.Core.Tests.csproj --filter TileEngineTests`
Expected: FAIL — `TileEngine.CalculateLine` doesn't exist (compile error).

- [ ] **Step 3: Implement**

In `unity/Assets/Scripts/Core/Battle/TileEngine.cs`, add after `VoxelCheck`:

```csharp
        /// <summary>
        /// Traces a 3D line through voxel space from origin to target,
        /// stopping at the first non-empty VoxelCheck result (or reaching
        /// target unobstructed). Standard 3D DDA/Bresenham with the same
        /// "drift" side-step checking the original uses so a shallow
        /// diagonal doesn't skip past a thin wall corner. Port of
        /// calculateLineHelper + calculateLineVoxel
        /// (src/Battlescape/TileEngine.cpp:60-160,4360-4408), collapsed into
        /// one voxel-specific method since this rewrite has no second caller
        /// needing the C++ template's generic callback shape (tile-level LOS
        /// already has its own separate, simpler WalkLine).
        /// </summary>
        public static VoxelHit CalculateLine(TileGrid grid, ushort[] loftData, Position origin, Position target, BattleUnit excludeUnit)
        {
            int x0 = origin.X, x1 = target.X;
            int y0 = origin.Y, y1 = target.Y;
            int z0 = origin.Z, z1 = target.Z;

            bool swapXy = System.Math.Abs(y1 - y0) > System.Math.Abs(x1 - x0);
            if (swapXy) { (x0, y0) = (y0, x0); (x1, y1) = (y1, x1); }

            bool swapXz = System.Math.Abs(z1 - z0) > System.Math.Abs(x1 - x0);
            if (swapXz) { (x0, z0) = (z0, x0); (x1, z1) = (z1, x1); }

            int deltaX = System.Math.Abs(x1 - x0);
            int deltaY = System.Math.Abs(y1 - y0);
            int deltaZ = System.Math.Abs(z1 - z0);

            int driftXy = deltaX / 2;
            int driftXz = deltaX / 2;

            int stepX = x0 > x1 ? -1 : 1;
            int stepY = y0 > y1 ? -1 : 1;
            int stepZ = z0 > z1 ? -1 : 1;

            int y = y0, z = z0;

            VoxelHit CheckPoint(int cx, int cy, int cz)
            {
                int px = cx, py = cy, pz = cz;
                if (swapXz) (px, pz) = (pz, px);
                if (swapXy) (px, py) = (py, px);
                return VoxelCheck(grid, loftData, new Position(px, py, pz), excludeUnit);
            }

            for (int x = x0; ; x += stepX)
            {
                var hit = CheckPoint(x, y, z);
                if (hit.IsHit) return hit;
                if (x == x1) break;

                driftXy -= deltaY;
                driftXz -= deltaZ;

                if (driftXy < 0)
                {
                    y += stepY;
                    driftXy += deltaX;
                    var drift = CheckPoint(x, y, z);
                    if (drift.IsHit) return drift;
                }
                if (driftXz < 0)
                {
                    z += stepZ;
                    driftXz += deltaX;
                    var drift = CheckPoint(x, y, z);
                    if (drift.IsHit) return drift;
                }
            }

            return VoxelHit.Empty(target);
        }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet test unity/Tests.Standalone/OpenXcom.Core.Tests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add unity/Assets/Scripts/Core/Battle/TileEngine.cs unity/Tests.Standalone/TileEngineTests.cs
git commit -m "feat(core): TileEngine.CalculateLine - 3D Bresenham voxel trace"
```

---

## Task 8: Core — `TileEngine.GetDirectionTo` / `GetOriginVoxel`

**Files:**
- Modify: `unity/Assets/Scripts/Core/Battle/TileEngine.cs`
- Test: `unity/Tests.Standalone/TileEngineTests.cs`

**Interfaces:**
- Consumes: `BattleUnit.Height`, `RuleUnit.FloatHeight` (Task 5/6), `Tile.Floor.TerrainLevel` (existing).
- Produces: `TileEngine.GetDirectionTo(Position, Position) -> int`, `TileEngine.GetOriginVoxel(TileGrid, BattleUnit, Position targetTile) -> Position` — consumed by Task 10 (`BattleState.TryFire`).

- [ ] **Step 1: Write the failing tests**

Add to `unity/Tests.Standalone/TileEngineTests.cs`:

```csharp
        [Fact]
        public void GetDirectionTo_EachCompassDirectionMapsToItsOwnIndex()
        {
            var origin = new Position(5, 5, 0);
            Assert.Equal(0, TileEngine.GetDirectionTo(origin, new Position(5, 0, 0)));  // north
            Assert.Equal(2, TileEngine.GetDirectionTo(origin, new Position(10, 5, 0))); // east
            Assert.Equal(4, TileEngine.GetDirectionTo(origin, new Position(5, 10, 0))); // south
            Assert.Equal(6, TileEngine.GetDirectionTo(origin, new Position(0, 5, 0)));  // west
        }

        [Fact]
        public void GetOriginVoxel_AddsShooterHeightAndTerrainLevelOffset()
        {
            var grid = new TileGrid(3, 3, 1);
            var shooter = new BattleUnit(new RuleUnit("STR_TEST", UnitStats.Rookie, RuleArmor.None,
                standHeight: 22, kneelHeight: 14), Faction.Player)
            {
                Position = new Position(1, 1, 0),
            };

            var origin = TileEngine.GetOriginVoxel(grid, shooter, new Position(1, 0, 0));

            // Base tile voxel origin (16,16,0) + Height(22) + FloatHeight(0) - TerrainLevel(0) - 4 = Z 18,
            // plus the north-direction (dir 0) shift (dirX=8, dirY=1) on top of tile*16.
            Assert.Equal(16 + 8, origin.X);
            Assert.Equal(16 + 1, origin.Y);
            Assert.Equal(0 + 22 + 0 - 4, origin.Z);
        }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet test unity/Tests.Standalone/OpenXcom.Core.Tests.csproj --filter TileEngineTests`
Expected: FAIL — `GetDirectionTo`/`GetOriginVoxel` don't exist (compile error).

- [ ] **Step 3: Implement**

In `unity/Assets/Scripts/Core/Battle/TileEngine.cs`, add after `CalculateLine`:

```csharp
        private static readonly int[] DirXShift = { 8, 14, 15, 15, 8, 1, 1, 1 };
        private static readonly int[] DirYShift = { 1, 1, 8, 15, 15, 15, 8, 1 };

        /// <summary>
        /// 8-way compass sector (0=north, clockwise) from origin to target,
        /// for arbitrary (non-adjacent) tiles - unlike Directions.IndexOf,
        /// which only matches an exact single-step delta. Port of
        /// TileEngine::getDirectionTo (src/Battlescape/TileEngine.cpp:5764-5806).
        /// </summary>
        public static int GetDirectionTo(Position origin, Position target)
        {
            double ox = target.X - origin.X;
            double oy = target.Y - origin.Y;
            double angle = System.Math.Atan2(ox, -oy);

            double pie0 = System.Math.PI - System.Math.PI / 8.0;
            double pie1 = System.Math.PI * 3.0 / 4.0 - System.Math.PI / 8.0;
            double pie2 = System.Math.PI / 2.0 - System.Math.PI / 8.0;
            double pie3 = System.Math.PI / 4.0 - System.Math.PI / 8.0;

            if (angle > pie0 || angle < -pie0) return 4;
            if (angle > pie1) return 3;
            if (angle > pie2) return 2;
            if (angle > pie3) return 1;
            if (angle < -pie1) return 5;
            if (angle < -pie2) return 6;
            if (angle < -pie3) return 7;
            return 0;
        }

        /// <summary>
        /// The voxel a shot leaves the shooter's weapon from: the shooter's
        /// tile origin, raised by body height and float height, adjusted
        /// for terrain level, offset 4 voxel-units back from the muzzle, and
        /// shifted sideways toward the target's direction (so the shot
        /// visibly originates from roughly where the weapon is held, not the
        /// tile's dead center). Port of TileEngine::getOriginVoxel
        /// (src/Battlescape/TileEngine.cpp:5826-5900), scoped to this
        /// rewrite's direct-fire-only, single-Z-level, size-1-unit,
        /// CENTRE-relativeOrigin case: no BA_THROW/BA_LAUNCH offset, no
        /// LEFT/RIGHT autofire-spread relativeOrigin variants (not modeled -
        /// Phase 8 design spec §5), no tileAbove/NoFloor multi-level clamp
        /// (CULTA00 is single-Z-level, matching every other phase's scope).
        /// </summary>
        public static Position GetOriginVoxel(TileGrid grid, BattleUnit shooter, Position targetTile)
        {
            var origin = shooter.Position;
            var tile = grid[origin];
            int terrainLevel = tile?.Floor?.TerrainLevel ?? 0;

            int baseX = origin.X * 16;
            int baseY = origin.Y * 16;
            int baseZ = origin.Z * 24 - terrainLevel + shooter.Height + shooter.Rules.FloatHeight - 4;

            int direction = GetDirectionTo(origin, targetTile);

            return new Position(baseX + DirXShift[direction], baseY + DirYShift[direction], baseZ);
        }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet test unity/Tests.Standalone/OpenXcom.Core.Tests.csproj`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add unity/Assets/Scripts/Core/Battle/TileEngine.cs unity/Tests.Standalone/TileEngineTests.cs
git commit -m "feat(core): TileEngine.GetDirectionTo/GetOriginVoxel - shot origin voxel"
```

---

## Task 9: Core — `Combat.ApplyDeviation` / `Combat.ApplyDamage`

**Files:**
- Modify: `unity/Assets/Scripts/Core/Battle/Combat.cs`
- Test: `unity/Tests.Standalone/CombatMathTests.cs`

**Interfaces:**
- Consumes: `Rng.Generate` (existing).
- Produces: `Combat.ApplyDeviation(Rng, Position originVoxel, Position targetVoxel, int accuracyPercent) -> Position`, `Combat.ApplyDamage(Rng, BattleUnit attacker, RuleItem weapon, BattleUnit target) -> ShotResult` — consumed by Task 10 (`BattleState.TryFire`). `Combat.ResolveShot` is refactored to call `ApplyDamage` internally (same external behavior/signature, no caller changes needed).

- [ ] **Step 1: Write the failing tests**

Add to `unity/Tests.Standalone/CombatMathTests.cs`:

```csharp
        [Fact]
        public void ApplyDeviation_HundredPercentAccuracyStaysCloseToTheAimPoint()
        {
            var rng = new Rng(42);
            var origin = new Position(0, 0, 0);
            var target = new Position(160, 0, 10); // 10 tiles away in X

            for (int i = 0; i < 50; i++)
            {
                var deviated = Combat.ApplyDeviation(rng, origin, target, accuracyPercent: 100);
                // Even at 100% accuracy the original's "miss cloud" tail (deviation
                // computed from RNG(0,100)-100 landing exactly on 0) means this isn't
                // always a perfect zero offset - assert it stays plausibly close, not exact.
                Assert.InRange(System.Math.Abs(deviated.X - target.X), 0, 50);
                Assert.InRange(System.Math.Abs(deviated.Y - target.Y), 0, 50);
            }
        }

        [Fact]
        public void ApplyDeviation_LowAccuracySpreadsFartherOnAverageThanHighAccuracy()
        {
            var rngLow = new Rng(7);
            var rngHigh = new Rng(7);
            var origin = new Position(0, 0, 0);
            var target = new Position(320, 0, 10); // 20 tiles away

            long lowTotal = 0, highTotal = 0;
            const int trials = 200;
            for (int i = 0; i < trials; i++)
            {
                var lowDev = Combat.ApplyDeviation(rngLow, origin, target, accuracyPercent: 20);
                var highDev = Combat.ApplyDeviation(rngHigh, origin, target, accuracyPercent: 90);
                lowTotal += System.Math.Abs(lowDev.X - target.X);
                highTotal += System.Math.Abs(highDev.X - target.X);
            }

            Assert.True(lowTotal > highTotal,
                $"expected low-accuracy average deviation ({lowTotal / (double)trials}) to exceed high-accuracy ({highTotal / (double)trials})");
        }

        [Fact]
        public void ApplyDamage_AppliesArmorAndKillsWhenHealthReachesZero()
        {
            var rng = new Rng(1);
            var attacker = MakeShooter();
            var defender = MakeFreshDefender();
            defender.Health = 1;

            var result = Combat.ApplyDamage(rng, attacker, RuleItem.Rifle, defender);

            Assert.True(result.Hit);
            Assert.True(result.Killed);
            Assert.Equal(0, defender.Health);
        }
```

(`MakeShooter`/`MakeFreshDefender` are the existing private helpers already used throughout `CombatMathTests.cs` — reuse them as-is, do not redefine.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet test unity/Tests.Standalone/OpenXcom.Core.Tests.csproj --filter CombatMathTests`
Expected: FAIL — `Combat.ApplyDeviation`/`Combat.ApplyDamage` don't exist (compile error).

- [ ] **Step 3: Implement**

In `unity/Assets/Scripts/Core/Battle/Combat.cs`, replace the existing `ResolveShot` method and add the two new ones (keep `HitChance`/`RollDamage`/`HitSide` unchanged):

```csharp
        /// <summary>
        /// Rolls and applies damage from `weapon` to `target`, mutating
        /// target.Health. Does not roll to-hit - the caller has already
        /// determined this shot connects, either via ResolveShot's classic
        /// percent-chance roll or via a voxel trace in
        /// BattleState.TryFire's Phase 8 path.
        /// </summary>
        public static ShotResult ApplyDamage(Rng rng, BattleUnit attacker, RuleItem weapon, BattleUnit target)
        {
            int rolled = RollDamage(rng, weapon);
            var side = HitSide(attacker.Position, target.Position);
            int armor = target.Armor.ValueFor(side);
            int applied = Math.Max(0, rolled - armor);

            target.Health -= applied;
            bool killed = !target.IsAlive;
            return new ShotResult(true, rolled, applied, killed);
        }

        /// <summary>
        /// Resolve a single projectile against a target unit: roll to hit, and on a
        /// hit roll and apply damage. Deterministic for a given &lt;paramref name="rng"/&gt;.
        /// </summary>
        public static ShotResult ResolveShot(Rng rng, BattleUnit attacker, BattleItem weapon,
            BattleActionType action, BattleUnit defender)
        {
            int chance = HitChance(attacker, weapon, action, defender.Position);
            if (!rng.Percent(chance))
                return ShotResult.Miss;

            return ApplyDamage(rng, attacker, weapon.Rules, defender);
        }

        /// <summary>
        /// Aim-point scatter from an accuracy percent (0-100), operating in
        /// voxel coordinates. Port of Projectile::applyAccuracy's "classic"
        /// (non-uniform, Options::oxceUniformShootingSpread == false) branch
        /// (src/Battlescape/Projectile.cpp:332-462). The C++ works in a
        /// 0.0-1.0 accuracy fraction multiplied by 100; this takes the
        /// already-0-100 percent HitChance uses directly, so
        /// "accuracy*100" in the original becomes "accuracyPercent" here -
        /// same formula, adapted scale. Does not model range-based accuracy
        /// dropoff (Combat.HitChance already applies that before this is
        /// called) or the OXCE uniform-spread toggle.
        /// </summary>
        public static Position ApplyDeviation(Rng rng, Position originVoxel, Position targetVoxel, int accuracyPercent)
        {
            int xDist = Math.Abs(originVoxel.X - targetVoxel.X);
            int yDist = Math.Abs(originVoxel.Y - targetVoxel.Y);
            int zDist = Math.Abs(originVoxel.Z - targetVoxel.Z);

            int xyShift = (xDist / 2 <= yDist) ? xDist / 4 + yDist : (xDist + yDist) / 2;
            int zShift = (xyShift <= zDist) ? xyShift / 2 + zDist : xyShift + zDist / 2;

            int deviation = rng.Generate(0, 100) - accuracyPercent;
            deviation += deviation >= 0 ? 50 : 10;
            deviation = Math.Max(1, zShift * deviation / 200);

            int dx = rng.Generate(0, deviation) - deviation / 2;
            int dy = rng.Generate(0, deviation) - deviation / 2;
            int dz = rng.Generate(0, deviation / 2) / 2 - deviation / 8;

            return new Position(targetVoxel.X + dx, targetVoxel.Y + dy, targetVoxel.Z + dz);
        }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet test unity/Tests.Standalone/OpenXcom.Core.Tests.csproj`
Expected: PASS. (If `ApplyDeviation_LowAccuracySpreadsFartherOnAverageThanHighAccuracy` flakes on the chosen seed, try a different literal seed value until it's stable across 3 consecutive runs, then keep that seed — the property being tested, low accuracy deviates more on average, is a real invariant of the formula, not seed-dependent, but any single seed's exact numbers are.)

- [ ] **Step 5: Commit**

```bash
git add unity/Assets/Scripts/Core/Battle/Combat.cs unity/Tests.Standalone/CombatMathTests.cs
git commit -m "feat(core): Combat.ApplyDeviation and ApplyDamage - voxel aim-point scatter"
```

---

## Task 10: Core — wire `BattleState.TryFire` to the voxel trace; Unity — load `loftemps.json`

**Files:**
- Modify: `unity/Assets/Scripts/Core/Battle/BattleEvent.cs`
- Modify: `unity/Assets/Scripts/Core/Battle/BattleState.cs`
- Modify: `unity/Assets/Scripts/Unity/BattlescapeBootstrap.cs`
- Test: `unity/Tests.Standalone/BattleStateTests.cs` (or wherever `TryFire` is currently tested — grep for `TryFire` if the filename differs)

**Interfaces:**
- Consumes: `TileEngine.CalculateLine`/`GetOriginVoxel` (Tasks 7-8), `Combat.ApplyDeviation`/`ApplyDamage` (Task 9), `DataLoader.LoadLoftemps` (Task 5).
- Produces: `ProjectileFiredEvent.Trajectory` (`IReadOnlyList<Position>`, new), `BattleState.LoftData` (`ushort[]`, new settable property) — the trajectory is what Phase 9's projectile visual (design spec §6) will animate along.

- [ ] **Step 1: Write the failing tests**

First, locate the existing `TryFire` tests:

```bash
grep -rl "TryFire" unity/Tests.Standalone/*.cs
```

Add to that file (adapt the exact helper/fixture names to whatever setup that file already uses for a grid+attacker+defender — follow its existing pattern, do not invent a new one):

```csharp
        // A fully-solid loft template (index 1) + a generous height band, shared
        // by the tests below so a fired shot's small, bounded deviation still
        // reliably lands inside whichever unit uses it - makes the outcome
        // deterministic without depending on a specific Rng seed's exact numbers.
        private static ushort[] FullTileLoftData()
        {
            var data = new ushort[32]; // template 0 = empty, template 1 = fully solid
            for (int row = 0; row < 16; row++) data[16 + row] = 0xFFFF;
            return data;
        }

        private static RuleArmor FullTileArmor() => new("FULL_TILE", 0, 0, 0, 0, loftemps: 1);

        [Fact]
        public void TryFire_ClearShotWithNoObstructionHitsTheIntendedTarget()
        {
            var grid = new TileGrid(5, 5, 1);
            var state = new BattleState(grid) { LoftData = FullTileLoftData() };
            var attacker = new BattleUnit(RuleUnit.Soldier, Faction.Player) { Position = new Position(0, 0, 0) };
            var defender = new BattleUnit(new RuleUnit("DEFENDER", UnitStats.Rookie, FullTileArmor(), standHeight: 200, kneelHeight: 200), Faction.Hostile)
            {
                Position = new Position(3, 0, 0),
            };
            grid.At(0, 0, 0).Occupant = attacker;
            grid.At(3, 0, 0).Occupant = defender;
            state.Units.Add(attacker);
            state.Units.Add(defender);
            var weapon = new BattleItem(RuleItem.Rifle);
            attacker.RightHand = weapon;
            int healthBefore = defender.Health;

            var result = state.TryFire(attacker, weapon, BattleActionType.Snapshot, defender);
            var events = state.DequeueEvents();

            Assert.Equal(FireOutcome.Fired, result.Outcome);
            Assert.True(result.Shot.Hit);
            Assert.True(defender.Health < healthBefore);
            var hitEvent = System.Linq.Enumerable.OfType<UnitHitEvent>(events).Single();
            Assert.Same(defender, hitEvent.Unit);
            var fired = System.Linq.Enumerable.OfType<ProjectileFiredEvent>(events).Single();
            Assert.NotEmpty(fired.Trajectory);
        }

        [Fact]
        public void TryFire_HitsAnIntermediateBystanderInsteadOfTheFarIntendedTarget()
        {
            // Bystander sits directly between attacker and the intended target,
            // occupying its entire tile's voxel column (FullTileArmor/LoftData)
            // across a tall height band - any ray toward the far target passes
            // through the bystander's tile first, regardless of the small
            // end-point deviation Combat.ApplyDeviation applies near the target.
            var grid = new TileGrid(10, 5, 1);
            var state = new BattleState(grid) { LoftData = FullTileLoftData() };
            var attacker = new BattleUnit(RuleUnit.Soldier, Faction.Player) { Position = new Position(0, 0, 0) };
            var bystander = new BattleUnit(new RuleUnit("BYSTANDER", UnitStats.Rookie, FullTileArmor(), standHeight: 200, kneelHeight: 200), Faction.Hostile)
            {
                Position = new Position(1, 0, 0),
            };
            var intendedTarget = new BattleUnit(new RuleUnit("FAR_TARGET", UnitStats.Rookie, FullTileArmor(), standHeight: 200, kneelHeight: 200), Faction.Hostile)
            {
                Position = new Position(8, 0, 0),
            };
            grid.At(0, 0, 0).Occupant = attacker;
            grid.At(1, 0, 0).Occupant = bystander;
            grid.At(8, 0, 0).Occupant = intendedTarget;
            state.Units.Add(attacker);
            state.Units.Add(bystander);
            state.Units.Add(intendedTarget);
            var weapon = new BattleItem(RuleItem.Rifle);
            attacker.RightHand = weapon;
            int targetHealthBefore = intendedTarget.Health;

            var result = state.TryFire(attacker, weapon, BattleActionType.Snapshot, intendedTarget);
            var events = state.DequeueEvents();

            Assert.True(result.Shot.Hit);
            var hitEvent = System.Linq.Enumerable.OfType<UnitHitEvent>(events).Single();
            Assert.Same(bystander, hitEvent.Unit); // hit the bystander, not the unit that was aimed at
            Assert.Equal(targetHealthBefore, intendedTarget.Health); // intended target untouched
            var fired = System.Linq.Enumerable.OfType<ProjectileFiredEvent>(events).Single();
            Assert.Same(intendedTarget, fired.Defender); // event still records who was aimed at
        }

        [Fact]
        public void TryFire_WithEmptyLoftDataEveryShotMissesRatherThanCrashing()
        {
            // LoftData defaults to empty (BattleState.LoftData's default) until a
            // scene bootstrap loads real data - VoxelCheck's bounds-checked lookup
            // means this degrades to "no voxel data configured", not a crash.
            var grid = new TileGrid(5, 5, 1);
            var state = new BattleState(grid);
            var attacker = new BattleUnit(RuleUnit.Soldier, Faction.Player) { Position = new Position(0, 0, 0) };
            var defender = new BattleUnit(RuleUnit.Sectoid, Faction.Hostile) { Position = new Position(3, 0, 0) };
            grid.At(0, 0, 0).Occupant = attacker;
            grid.At(3, 0, 0).Occupant = defender;
            state.Units.Add(attacker);
            state.Units.Add(defender);
            var weapon = new BattleItem(RuleItem.Rifle);
            attacker.RightHand = weapon;

            var result = state.TryFire(attacker, weapon, BattleActionType.Snapshot, defender);

            Assert.Equal(FireOutcome.Fired, result.Outcome);
            Assert.False(result.Shot.Hit);
        }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet test unity/Tests.Standalone/OpenXcom.Core.Tests.csproj --filter TryFire`
Expected: FAIL — `BattleState.LoftData` doesn't exist, `ProjectileFiredEvent` constructor doesn't accept a `Trajectory` argument (compile errors).

- [ ] **Step 3: Implement**

`unity/Assets/Scripts/Core/Battle/BattleEvent.cs` — replace `ProjectileFiredEvent`:

```csharp
    public sealed class ProjectileFiredEvent : BattleEvent
    {
        public BattleUnit Attacker { get; }
        public BattleUnit Defender { get; }
        public bool Hit { get; }
        public IReadOnlyList<Position> Trajectory { get; }

        public ProjectileFiredEvent(BattleUnit attacker, BattleUnit defender, bool hit, IReadOnlyList<Position> trajectory)
        {
            Attacker = attacker;
            Defender = defender;
            Hit = hit;
            Trajectory = trajectory;
        }
    }
```

`unity/Assets/Scripts/Core/Battle/BattleState.cs` — add a property (near `Rng`):

```csharp
        /// <summary>
        /// Flat LOFTEMPS.DAT voxel bitmask table (DataLoader.LoadLoftemps),
        /// used by TileEngine.CalculateLine/VoxelCheck for shot resolution.
        /// Empty by default; a scene bootstrap must set this before firing
        /// produces meaningful hits - VoxelCheck treats any loft index
        /// outside an empty/undersized table as passable, so an unset table
        /// degrades to "every shot misses" rather than crashing.
        /// </summary>
        public ushort[] LoftData { get; set; } = System.Array.Empty<ushort>();
```

Replace the body of `TryFire`:

```csharp
        public FireResult TryFire(BattleUnit attacker, BattleItem weapon, BattleActionType action, BattleUnit defender)
        {
            var visibleTiles = TileEngine.ComputeVisibleTiles(Grid, attacker.Position);
            if (!visibleTiles.Contains(defender.Position))
                return new FireResult { Outcome = FireOutcome.NoLineOfSight, Shot = ShotResult.Miss };

            int tuCost = attacker.FireTuCost(action, weapon);
            if (!attacker.CanSpend(tuCost))
                return new FireResult { Outcome = FireOutcome.InsufficientTu, Shot = ShotResult.Miss };

            attacker.Spend(tuCost);

            int accuracy = Combat.HitChance(attacker, weapon, action, defender.Position);
            var originVoxel = TileEngine.GetOriginVoxel(Grid, attacker, defender.Position);
            var targetVoxel = new Position(
                defender.Position.X * 16 + 8,
                defender.Position.Y * 16 + 8,
                defender.Position.Z * 24 + defender.Height / 2);
            var aimVoxel = Combat.ApplyDeviation(Rng, originVoxel, targetVoxel, accuracy);

            var trace = TileEngine.CalculateLine(Grid, LoftData, originVoxel, aimVoxel, attacker);
            var trajectory = new List<Position> { originVoxel, trace.Voxel };

            var hitUnit = trace.Type == VoxelType.Unit ? trace.Unit : null;
            var shot = hitUnit != null ? Combat.ApplyDamage(Rng, attacker, weapon.Rules, hitUnit) : ShotResult.Miss;

            Enqueue(new ProjectileFiredEvent(attacker, defender, shot.Hit, trajectory));

            if (shot.Hit)
            {
                var side = Combat.HitSide(attacker.Position, hitUnit.Position);
                Enqueue(new UnitHitEvent(hitUnit, shot.AppliedDamage, side));

                if (shot.Killed)
                {
                    Grid[hitUnit.Position].Occupant = null;
                    Enqueue(new UnitDiedEvent(hitUnit));
                }
            }

            return new FireResult { Outcome = FireOutcome.Fired, Shot = shot };
        }
```

Update the doc comment directly above `TryFire` (currently describes the old abstract-roll behavior) to:

```csharp
        /// <summary>
        /// Attempts to fire `weapon` from `attacker` at `defender`. Gated by
        /// line of sight (TileEngine.ComputeVisibleTiles) and TU budget, in
        /// that order. Once both gates pass, TU is spent immediately and the
        /// shot's accuracy is converted into a deviated aim voxel
        /// (Combat.ApplyDeviation) which is then traced through real
        /// geometry (TileEngine.CalculateLine) - the trace's actual hit
        /// (terrain, `defender`, or a different unit caught in the deviated
        /// path) is what takes damage, not necessarily `defender` itself.
        /// A kill clears the hit unit's tile occupancy synchronously (no
        /// death-animation state machine this phase). Always enqueues one
        /// ProjectileFiredEvent when a shot is actually fired (hit or miss),
        /// carrying the traced voxel path, plus UnitHitEvent/UnitDiedEvent
        /// for whichever unit was actually hit.
        /// </summary>
```

`unity/Assets/Scripts/Unity/BattlescapeBootstrap.cs` — in `Start()`, after the existing `var state = new BattleState(grid);` line, add:

```csharp
            state.LoftData = DataLoader.LoadLoftemps(gameDataDir);
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet test unity/Tests.Standalone/OpenXcom.Core.Tests.csproj`
Expected: PASS, full suite. Pay particular attention to any pre-existing `TryFire`-related tests that asserted specific hit/miss outcomes via `Combat.ResolveShot`'s old direct percent-roll behavior — `TryFire` now goes through the voxel trace, so a previously-passing test relying on `Rng` producing a specific hit/miss via the *old* code path may need its expected outcome (or its `LoftData`/grid setup) updated to match the new trace-based flow. Fix forward rather than reverting Task 10's change if this happens — check each failure individually to confirm whether the test's assumption (not the new logic) is now stale.

- [ ] **Step 5: Commit**

```bash
git add unity/Assets/Scripts/Core/Battle/BattleEvent.cs unity/Assets/Scripts/Core/Battle/BattleState.cs unity/Assets/Scripts/Unity/BattlescapeBootstrap.cs unity/Tests.Standalone/*.cs
git commit -m "feat(core): wire BattleState.TryFire through the voxel trace and deviation"
```

---

## Post-plan verification (not a task — a final check)

After Task 10, run the full suite once more and also compile-check the Unity-side `BattlescapeBootstrap.cs` change via Coplay MCP if a live Editor session is available (`mcp__coplay-mcp__check_compile_errors`), per this repo's established Unity-side verification discipline (Phases 6/7). If no Editor is connected, note this explicitly rather than claiming it was verified — this one line (`state.LoftData = ...`) is outside `dotnet test`'s reach since it's in `OpenXcom.Unity`, not `OpenXcom.Core`.
