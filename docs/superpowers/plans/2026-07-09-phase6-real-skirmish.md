# Phase 6 (Real Data & First Editor Run) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the hardcoded stand-in unit/armor/weapon data with real converted `.rul` data (XCom soldier + Sectoid), and wire the code-complete Phase 1-5 skirmish loop into an actual Unity scene — 2 soldiers vs. 2 Sectoids, spawned, movable, able to shoot each other, with a working end-turn/AI/win-lose loop, visible and playable in the Unity Editor.

**Architecture:** `Xcom.Convert` gains a narrow YAML rule decoder (units/armors/items, six specific entry IDs) and a fourth sprite atlas (`SECTOID.PCK`); `OpenXcom.Core`'s `DataLoader` gains loaders that turn that JSON into real `RuleUnit`/`RuleArmor`/`RuleItem` instances; `OpenXcom.Unity` gains a collider fix, a `UnitRenderer`, and a `BattlescapeBootstrap` that assembles a real `BattleState` and binds it to the existing `BattleController`, in a new scene built live through the Coplay MCP plugin.

**Tech Stack:** .NET 8, xUnit, Newtonsoft.Json, YamlDotNet (new), Unity 2D (SpriteRenderer/BoxCollider), Coplay MCP tools.

## Global Constraints

- This plan builds on Phases 1-5 (merged to `oxce-plus`). Reuse exactly as-is, do not rename or re-declare: `BattleState` (`Grid`, `Units`, `Enqueue`/`DequeueEvents`, `TryMove`, `TryFire`, `EndPlayerTurn`, `IsBattleOver` — `unity/Assets/Scripts/Core/Battle/BattleState.cs`), `BattleUnit` (`Position`, `RightHand`/`LeftHand`, `Faction`, `IsAlive` — `Battle/BattleUnit.cs`), `BattleItem` (`Battle/BattleItem.cs`), `RuleUnit`/`RuleArmor`/`RuleItem`/`UnitStats`/`DamageType`/`Faction` (`Rules/RuleUnit.cs`, `Rules/RuleArmor.cs`, `Rules/RuleItem.cs`, `Rules/UnitStats.cs`, `Rules/Enums.cs`), `MapGenerator.Build`, `TileGrid`/`Tile`/`Position` (`Common/Position.cs`), `DataLoader.LoadTiles`/`LoadTerrain`/`LoadMapBlock` (`Rules/DataLoader.cs`), `PckDecoder`/`AtlasWriter` (`Xcom.Convert/Decoders`, `Xcom.Convert/Output`), `IsoProjection`, `TileRenderer`, `BattleController`, `BattlescapeMapView` (`Assets/Scripts/Unity/`).
- `OpenXcom.Core` (`unity/Assets/Scripts/Core/`) must have zero `UnityEngine` references. `Xcom.Convert` must have zero `UnityEngine` references and never runs inside the Editor.
- **CULTA00 has only one route node** (verified this session: `RawData/Resources/UFO/ROUTES/CULTA00.RMP` is exactly one 24-byte record). `BattleState.SpawnAtRouteNodes` cannot seat a 4-unit squad from it. Every tile in the 10×10 block is walkable (verified by resolving all 100 tiles' `Floor`/`NoFloor` this session), so the squad is placed at **fixed coordinates** instead: Soldier A `(1,1,0)`, Soldier B `(2,1,0)`, Sectoid A `(8,8,0)`, Sectoid B `(7,8,0)`. Use these exact coordinates in every task that references them.
- Ruleset source data lives at the **repo root**, not under `unity/`: `bin/standard/xcom1/{soldiers,units,armors,items}.rul`. This is a separate directory tree from `unity/RawData/` (the original DOS data) — `Xcom.Convert` needs a second input directory parameter for it.
- YamlDotNet version: pin to **18.1.0** (latest stable on nuget.org as of this session).
- Environment: the .NET SDK is at `~/.dotnet`; every `dotnet` command must be run as `export PATH="$HOME/.dotnet:$PATH" && dotnet ...`, from `unity/Tests.Standalone` (for tests) or `unity/` (for `dotnet run --project Xcom.Convert`).
- **A live Unity Editor instance is connected via the Coplay MCP plugin this session** (confirmed via `mcp__coplay-mcp__list_unity_project_roots` returning `unity`). Unity-side tasks (5-6) must be verified live: `check_compile_errors` after every change, `play_game` + `get_unity_logs` + `capture_scene_object` to confirm actual behavior — not just "written to the same standard as prior unverified phases." If re-run in a session with no Editor connected (re-check `list_unity_project_roots` — empty means none), fall back to writing the code to the existing standard of care and explicitly recording the verification gap, the way Phase 5's Task 4 did.
- Damage-type numbers: the `.rul` `damageType` integer (OXCE `ItemDamageType`) and Core's `DamageType` enum ordinal happen to agree for the values used here (1 = AP/"Armor", 5 = Plasma) — `Combat.cs` doesn't branch on `DamageType` at all yet, so this is a label only, not a behavior change. Don't build a mapping table; cast directly.
- No `alienDeployments.rul`/`alienRaces.rul` parsing, no ammo/clip inventory, no soldier stat randomization, no soldier armor conversion, no on-screen HUD, no unit sprite direction/animation — all explicitly deferred per the design spec (`docs/superpowers/specs/2026-07-09-phase6-real-skirmish-design.md` §6). Do not add them.

---

### Task 1: `Xcom.Convert` — `RuleYamlDecoder`

**Files:**
- Create: `unity/Xcom.Convert/Decoders/RuleYamlDecoder.cs`
- Modify: `unity/Xcom.Convert/Xcom.Convert.csproj` (add YamlDotNet)
- Modify: `unity/Tests.Standalone/TestPaths.cs` (add `RulesDir`)
- Test: `unity/Tests.Standalone/Convert/RuleYamlDecoderTests.cs` (new file)

**Interfaces:**
- Consumes: nothing from other tasks (first task).
- Produces (used by Task 2): `Xcom.Convert.Decoders.RuleYamlDecoder.LoadAlienUnit(string unitsRulPath, string typeId) -> ConvertedUnit`, `.LoadSoldierUnit(string soldiersRulPath, string typeId) -> ConvertedUnit`, `.LoadArmor(string armorsRulPath, string typeId) -> ConvertedArmor`, `.LoadWeapon(string itemsRulPath, string weaponTypeId, string clipTypeId) -> ConvertedItem`; the types `ConvertedUnit { string Id; ConvertedStats Stats; string ArmorId; }`, `ConvertedStats { int TimeUnits, Stamina, Health, Bravery, Reactions, Firing, Throwing, Strength, Melee; }`, `ConvertedArmor { string Id; int Front, Side, Rear, Under; }`, `ConvertedItem { string Id; bool TwoHanded; int Power, DamageType, AccuracySnap, AccuracyAimed, AccuracyAuto, TuSnap, TuAimed, TuAuto; }` — all plain public fields (matches this file's existing `DatasetInfo`-style convention so Newtonsoft serializes them without extra attributes).
- Also produces (used by Task 3, via JSON, not by reference): the field names above become the JSON key names Task 3's `DataLoader` reads.

- [ ] **Step 1: Add the YamlDotNet package reference**

In `unity/Xcom.Convert/Xcom.Convert.csproj`, add one line inside the existing `<ItemGroup>`:

```xml
    <PackageReference Include="YamlDotNet" Version="18.1.0" />
```

- [ ] **Step 2: Add `TestPaths.RulesDir`**

In `unity/Tests.Standalone/TestPaths.cs`, add this field to the `TestPaths` class, alongside `RawDataDir`:

```csharp
        public static readonly string RulesDir = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "bin", "standard", "xcom1");
```

(Verified this session: `AppContext.BaseDirectory` for this test assembly is
`unity/Tests.Standalone/bin/Debug/net8.0/`; five `..` reaches the repo root,
then `bin/standard/xcom1` — confirmed to exist and resolve correctly.)

- [ ] **Step 3: Write the failing tests**

Create `unity/Tests.Standalone/Convert/RuleYamlDecoderTests.cs`:

```csharp
using System.IO;
using Xcom.Convert.Decoders;
using Xunit;

namespace OpenXcom.Core.Tests.Convert
{
    public class RuleYamlDecoderTests
    {
        private static readonly string RulesDir = TestPaths.RulesDir;

        [Fact]
        public void LoadAlienUnit_ParsesSectoidSoldierStatsAndArmorId()
        {
            var unit = RuleYamlDecoder.LoadAlienUnit(
                Path.Combine(RulesDir, "units.rul"), "STR_SECTOID_SOLDIER");

            Assert.Equal("STR_SECTOID_SOLDIER", unit.Id);
            Assert.Equal(54, unit.Stats.TimeUnits);
            Assert.Equal(90, unit.Stats.Stamina);
            Assert.Equal(30, unit.Stats.Health);
            Assert.Equal(80, unit.Stats.Bravery);
            Assert.Equal(63, unit.Stats.Reactions);
            Assert.Equal(52, unit.Stats.Firing);
            Assert.Equal(58, unit.Stats.Throwing);
            Assert.Equal(30, unit.Stats.Strength);
            Assert.Equal(76, unit.Stats.Melee);
            Assert.Equal("SECTOID_ARMOR0", unit.ArmorId);
        }

        [Fact]
        public void LoadSoldierUnit_ParsesMinStatsAndLeavesArmorIdNull()
        {
            var unit = RuleYamlDecoder.LoadSoldierUnit(
                Path.Combine(RulesDir, "soldiers.rul"), "STR_SOLDIER");

            Assert.Equal("STR_SOLDIER", unit.Id);
            Assert.Equal(50, unit.Stats.TimeUnits);
            Assert.Equal(40, unit.Stats.Stamina);
            Assert.Equal(25, unit.Stats.Health);
            Assert.Equal(10, unit.Stats.Bravery);
            Assert.Equal(30, unit.Stats.Reactions);
            Assert.Equal(40, unit.Stats.Firing);
            Assert.Equal(50, unit.Stats.Throwing);
            Assert.Equal(20, unit.Stats.Strength);
            Assert.Equal(20, unit.Stats.Melee);
            Assert.Null(unit.ArmorId);
        }

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
        }

        [Fact]
        public void LoadWeapon_MergesWeaponAccuracyFieldsWithClipPowerAndDamageType()
        {
            var rifle = RuleYamlDecoder.LoadWeapon(
                Path.Combine(RulesDir, "items.rul"), "STR_RIFLE", "STR_RIFLE_CLIP");

            Assert.Equal("STR_RIFLE", rifle.Id);
            Assert.True(rifle.TwoHanded);
            Assert.Equal(60, rifle.AccuracySnap);
            Assert.Equal(110, rifle.AccuracyAimed);
            Assert.Equal(35, rifle.AccuracyAuto);
            Assert.Equal(25, rifle.TuSnap);
            Assert.Equal(80, rifle.TuAimed);
            Assert.Equal(35, rifle.TuAuto);
            Assert.Equal(30, rifle.Power);       // from STR_RIFLE_CLIP
            Assert.Equal(1, rifle.DamageType);   // from STR_RIFLE_CLIP

            var pistol = RuleYamlDecoder.LoadWeapon(
                Path.Combine(RulesDir, "items.rul"), "STR_PLASMA_PISTOL", "STR_PLASMA_PISTOL_CLIP");

            Assert.Equal("STR_PLASMA_PISTOL", pistol.Id);
            Assert.False(pistol.TwoHanded); // field absent in the .rul entry -> default false
            Assert.Equal(65, pistol.AccuracySnap);
            Assert.Equal(85, pistol.AccuracyAimed);
            Assert.Equal(50, pistol.AccuracyAuto);
            Assert.Equal(30, pistol.TuSnap);
            Assert.Equal(60, pistol.TuAimed);
            Assert.Equal(30, pistol.TuAuto);
            Assert.Equal(52, pistol.Power);      // from STR_PLASMA_PISTOL_CLIP
            Assert.Equal(5, pistol.DamageType);  // from STR_PLASMA_PISTOL_CLIP
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test --filter "FullyQualifiedName~RuleYamlDecoderTests"`
Expected: FAIL to build — `RuleYamlDecoder` does not exist yet.

- [ ] **Step 5: Write the implementation**

Create `unity/Xcom.Convert/Decoders/RuleYamlDecoder.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Xcom.Convert.Decoders
{
    public sealed class ConvertedStats
    {
        public int TimeUnits;
        public int Stamina;
        public int Health;
        public int Bravery;
        public int Reactions;
        public int Firing;
        public int Throwing;
        public int Strength;
        public int Melee;
    }

    public sealed class ConvertedUnit
    {
        public string Id;
        public ConvertedStats Stats;
        public string ArmorId; // null when this unit's armor isn't converted this slice
    }

    public sealed class ConvertedArmor
    {
        public string Id;
        public int Front;
        public int Side;
        public int Rear;
        public int Under;
    }

    public sealed class ConvertedItem
    {
        public string Id;
        public bool TwoHanded;
        public int Power;
        public int DamageType;
        public int AccuracySnap;
        public int AccuracyAimed;
        public int AccuracyAuto;
        public int TuSnap;
        public int TuAimed;
        public int TuAuto;
    }

    /// <summary>
    /// Reads specific named entries out of OXCE's ruleset YAML
    /// (bin/standard/xcom1/*.rul at the repo root) into Convert's own DTOs.
    /// Deliberately narrow: looks up exactly the IDs a caller asks for, not a
    /// general schema for every field/entry in these files - see the phase 6
    /// design spec §6 ("no general .rul YAML converter").
    /// </summary>
    public static class RuleYamlDecoder
    {
        private static readonly IDeserializer Deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        public static ConvertedUnit LoadAlienUnit(string unitsRulPath, string typeId)
        {
            var file = Deserializer.Deserialize<RawUnitsFile>(File.ReadAllText(unitsRulPath));
            var raw = file.Units.Find(u => u.Type == typeId)
                ?? throw new InvalidDataException($"{typeId} not found in {unitsRulPath}");
            return new ConvertedUnit { Id = raw.Type, Stats = ToStats(raw.Stats), ArmorId = raw.Armor };
        }

        public static ConvertedUnit LoadSoldierUnit(string soldiersRulPath, string typeId)
        {
            var file = Deserializer.Deserialize<RawSoldiersFile>(File.ReadAllText(soldiersRulPath));
            var raw = file.Soldiers.Find(s => s.Type == typeId)
                ?? throw new InvalidDataException($"{typeId} not found in {soldiersRulPath}");
            return new ConvertedUnit { Id = raw.Type, Stats = ToStats(raw.MinStats), ArmorId = null };
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
            };
        }

        public static ConvertedItem LoadWeapon(string itemsRulPath, string weaponTypeId, string clipTypeId)
        {
            var file = Deserializer.Deserialize<RawItemsFile>(File.ReadAllText(itemsRulPath));
            var weapon = file.Items.Find(i => i.Type == weaponTypeId)
                ?? throw new InvalidDataException($"{weaponTypeId} not found in {itemsRulPath}");
            var clip = file.Items.Find(i => i.Type == clipTypeId)
                ?? throw new InvalidDataException($"{clipTypeId} not found in {itemsRulPath}");
            return new ConvertedItem
            {
                Id = weapon.Type,
                TwoHanded = weapon.TwoHanded,
                Power = clip.Power,
                DamageType = clip.DamageType,
                AccuracySnap = weapon.AccuracySnap,
                AccuracyAimed = weapon.AccuracyAimed,
                AccuracyAuto = weapon.AccuracyAuto,
                TuSnap = weapon.TuSnap,
                TuAimed = weapon.TuAimed,
                TuAuto = weapon.TuAuto,
            };
        }

        private static ConvertedStats ToStats(RawStats s) => new()
        {
            TimeUnits = s.Tu, Stamina = s.Stamina, Health = s.Health, Bravery = s.Bravery,
            Reactions = s.Reactions, Firing = s.Firing, Throwing = s.Throwing,
            Strength = s.Strength, Melee = s.Melee,
        };

        // --- YAML-shaped DTOs matching bin/standard/xcom1/*.rul field names ---

        private sealed class RawStats
        {
            public int Tu { get; set; }
            public int Stamina { get; set; }
            public int Health { get; set; }
            public int Bravery { get; set; }
            public int Reactions { get; set; }
            public int Firing { get; set; }
            public int Throwing { get; set; }
            public int Strength { get; set; }
            public int Melee { get; set; }
        }

        private sealed class RawUnit
        {
            public string Type { get; set; } = "";
            public RawStats Stats { get; set; } = new();
            public string Armor { get; set; } = "";
        }

        private sealed class RawUnitsFile
        {
            public List<RawUnit> Units { get; set; } = new();
        }

        private sealed class RawSoldier
        {
            public string Type { get; set; } = "";
            public RawStats MinStats { get; set; } = new();
        }

        private sealed class RawSoldiersFile
        {
            public List<RawSoldier> Soldiers { get; set; } = new();
        }

        private sealed class RawArmor
        {
            public string Type { get; set; } = "";
            public int FrontArmor { get; set; }
            public int SideArmor { get; set; }
            public int RearArmor { get; set; }
            public int UnderArmor { get; set; }
        }

        private sealed class RawArmorsFile
        {
            public List<RawArmor> Armors { get; set; } = new();
        }

        private sealed class RawItem
        {
            public string Type { get; set; } = "";
            public bool TwoHanded { get; set; }
            public int AccuracySnap { get; set; }
            public int AccuracyAimed { get; set; }
            public int AccuracyAuto { get; set; }
            public int TuSnap { get; set; }
            public int TuAimed { get; set; }
            public int TuAuto { get; set; }
            public int Power { get; set; }
            public int DamageType { get; set; }
        }

        private sealed class RawItemsFile
        {
            public List<RawItem> Items { get; set; } = new();
        }
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test --filter "FullyQualifiedName~RuleYamlDecoderTests"`
Expected: PASS (4/4).

- [ ] **Step 7: Run the full test suite (regression check)**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test`
Expected: all tests pass.

- [ ] **Step 8: Commit**

```bash
git add unity/Xcom.Convert/Decoders/RuleYamlDecoder.cs unity/Xcom.Convert/Xcom.Convert.csproj unity/Tests.Standalone/TestPaths.cs unity/Tests.Standalone/Convert/RuleYamlDecoderTests.cs
git commit -m "feat(convert): RuleYamlDecoder - narrow .rul YAML reader for soldier/Sectoid/armor/weapon data"
```

---

### Task 2: `Xcom.Convert` — wire `ConvertJob` to emit real unit/armor/item data + Sectoid sprites

**Files:**
- Modify: `unity/Xcom.Convert/ConvertJob.cs`
- Modify: `unity/Xcom.Convert/Program.cs`
- Modify: `unity/Tests.Standalone/Convert/ConvertJobTests.cs`

**Interfaces:**
- Consumes: `RuleYamlDecoder.LoadAlienUnit`/`LoadSoldierUnit`/`LoadArmor`/`LoadWeapon` (Task 1), existing `PckDecoder.Load`/`AtlasWriter.Build`/`AtlasWriter.Save` (unchanged).
- Produces (used by Task 3): on-disk `units.json` (array of `ConvertedUnit`), `armors.json` (array of `ConvertedArmor`), `items.json` (array of `ConvertedItem`), and `units-SECTOID.png`/`units-SECTOID.frames.json` in the output `GameData` directory. Also: `ConvertJob.Run`'s new signature `Run(string dataDir, string rulesDir, string outDir)`.

- [ ] **Step 1: Write the failing test**

Modify `unity/Tests.Standalone/Convert/ConvertJobTests.cs` — replace the whole file:

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

        [Fact]
        public void Run_ProducesPaletteTerrainUnitRulesAndMapblockOutputs()
        {
            string outDir = Path.Combine(Path.GetTempPath(), "xcomconv-" + System.Guid.NewGuid());
            var written = ConvertJob.Run(DataDir, RulesDir, outDir);

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
            Assert.True(File.Exists(Path.Combine(outDir, "manifest.json")));

            Assert.Equal(20, written.Count);
            var manifestJson = File.ReadAllText(Path.Combine(outDir, "manifest.json"));
            var manifest = Newtonsoft.Json.Linq.JObject.Parse(manifestJson);
            var files = manifest["files"].Select(t => t.ToString()).ToList();
            Assert.Equal(20, files.Count);

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

- [ ] **Step 2: Run test to verify it fails**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test --filter "FullyQualifiedName~ConvertJobTests"`
Expected: FAIL to build — `ConvertJob.Run` doesn't accept 3 arguments yet.

- [ ] **Step 3: Write the implementation**

In `unity/Xcom.Convert/ConvertJob.cs`, add `using Xcom.Convert.Decoders;` at the top (it's likely already implicitly available via the same-project reference, but add the explicit `using` if not present), then change the `Run` signature and body:

```csharp
        public static IReadOnlyList<string> Run(string dataDir, string rulesDir, string outDir)
```

Immediately after the existing step 3 (unit sprite conversion — the block that writes `units-XCOM_0.png`/`.frames.json`), insert:

```csharp

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
```

Immediately after the existing step 4 (mapblock — the block that writes `mapblock-CULTA00.json`) and before the final manifest-writing block, insert:

```csharp

            // 5. Rules: XCom soldier + Sectoid stats, Sectoid armor, rifle + plasma pistol.
            var soldier = RuleYamlDecoder.LoadSoldierUnit(Path.Combine(rulesDir, "soldiers.rul"), "STR_SOLDIER");
            var sectoidUnit = RuleYamlDecoder.LoadAlienUnit(Path.Combine(rulesDir, "units.rul"), "STR_SECTOID_SOLDIER");
            var sectoidArmor = RuleYamlDecoder.LoadArmor(Path.Combine(rulesDir, "armors.rul"), "SECTOID_ARMOR0");
            var rifle = RuleYamlDecoder.LoadWeapon(Path.Combine(rulesDir, "items.rul"), "STR_RIFLE", "STR_RIFLE_CLIP");
            var plasmaPistol = RuleYamlDecoder.LoadWeapon(Path.Combine(rulesDir, "items.rul"), "STR_PLASMA_PISTOL", "STR_PLASMA_PISTOL_CLIP");

            File.WriteAllText(Path.Combine(outDir, "units.json"),
                JsonConvert.SerializeObject(new[] { soldier, sectoidUnit }, Formatting.Indented));
            written.Add("units.json");

            File.WriteAllText(Path.Combine(outDir, "armors.json"),
                JsonConvert.SerializeObject(new[] { sectoidArmor }, Formatting.Indented));
            written.Add("armors.json");

            File.WriteAllText(Path.Combine(outDir, "items.json"),
                JsonConvert.SerializeObject(new[] { rifle, plasmaPistol }, Formatting.Indented));
            written.Add("items.json");
```

(Leave the rest of the file — the manifest-writing block at the end — unchanged; it already writes whatever is in `written` at that point.)

In `unity/Xcom.Convert/Program.cs`, add a `--rules` argument:

```csharp
        public static int Main(string[] args)
        {
            string dataDir = ArgValue(args, "--data") ?? "../RawData/Resources/UFO";
            string rulesDir = ArgValue(args, "--rules") ?? "../../bin/standard/xcom1";
            string outDir = ArgValue(args, "--out") ?? "../Assets/GameData";
            var written = ConvertJob.Run(dataDir, rulesDir, outDir);
            Console.WriteLine($"Wrote {written.Count} files to {outDir}");
            return 0;
        }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test --filter "FullyQualifiedName~ConvertJobTests"`
Expected: PASS (1/1).

- [ ] **Step 5: Run the full test suite (regression check)**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test`
Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add unity/Xcom.Convert/ConvertJob.cs unity/Xcom.Convert/Program.cs unity/Tests.Standalone/Convert/ConvertJobTests.cs
git commit -m "feat(convert): wire units/armors/items + Sectoid sprites into ConvertJob"
```

---

### Task 3: `OpenXcom.Core` — `DataLoader.LoadUnits`/`LoadArmors`/`LoadItems`

**Files:**
- Modify: `unity/Assets/Scripts/Core/Rules/DataLoader.cs`
- Modify: `unity/Tests.Standalone/DataLoaderTests.cs`

**Interfaces:**
- Consumes: `RuleUnit`/`RuleArmor`/`RuleItem`/`UnitStats`/`DamageType` (existing), the JSON shape Task 2 emits (`Id`, `Stats.{TimeUnits,Stamina,Health,Bravery,Reactions,Firing,Throwing,Strength,Melee}`, `ArmorId` for units; `Id`,`Front`,`Side`,`Rear`,`Under` for armors; `Id`,`TwoHanded`,`Power`,`DamageType`,`AccuracySnap`,`AccuracyAimed`,`AccuracyAuto`,`TuSnap`,`TuAimed`,`TuAuto` for items).
- Produces (used by Task 6): `OpenXcom.Core.Rules.DataLoader.LoadArmors(string gameDataDir) -> List<RuleArmor>`, `.LoadUnits(string gameDataDir, IReadOnlyDictionary<string, RuleArmor> armorsById) -> List<RuleUnit>`, `.LoadItems(string gameDataDir) -> List<RuleItem>`.

- [ ] **Step 1: Write the failing tests**

Add these methods to `unity/Tests.Standalone/DataLoaderTests.cs` (inside the existing `DataLoaderTests` class, alongside the 3 existing tests — do not remove those; add `using System.Collections.Generic;` to the file's using block if not already present):

```csharp
        [Fact]
        public void LoadArmors_ParsesFields()
        {
            string json = @"[{ ""Id"": ""SECTOID_ARMOR0"", ""Front"": 4, ""Side"": 3, ""Rear"": 2, ""Under"": 2 }]";
            File.WriteAllText(Path.Combine(_dir, "armors.json"), json);

            var armors = DataLoader.LoadArmors(_dir);

            Assert.Single(armors);
            Assert.Equal("SECTOID_ARMOR0", armors[0].Id);
            Assert.Equal(4, armors[0].Front);
            Assert.Equal(3, armors[0].Side);
            Assert.Equal(2, armors[0].Rear);
            Assert.Equal(2, armors[0].Under);
        }

        [Fact]
        public void LoadUnits_ResolvesArmorIdAndFallsBackToNoneWhenMissing()
        {
            string json = @"[
                { ""Id"": ""STR_SECTOID_SOLDIER"", ""ArmorId"": ""SECTOID_ARMOR0"",
                  ""Stats"": { ""TimeUnits"": 54, ""Stamina"": 90, ""Health"": 30, ""Bravery"": 80,
                                ""Reactions"": 63, ""Firing"": 52, ""Throwing"": 58, ""Strength"": 30, ""Melee"": 76 } },
                { ""Id"": ""STR_SOLDIER"", ""ArmorId"": null,
                  ""Stats"": { ""TimeUnits"": 50, ""Stamina"": 40, ""Health"": 25, ""Bravery"": 10,
                                ""Reactions"": 30, ""Firing"": 40, ""Throwing"": 50, ""Strength"": 20, ""Melee"": 20 } }
            ]";
            File.WriteAllText(Path.Combine(_dir, "units.json"), json);
            var sectoidArmor = new RuleArmor("SECTOID_ARMOR0", front: 4, side: 3, rear: 2, under: 2);
            var armorsById = new Dictionary<string, RuleArmor> { { "SECTOID_ARMOR0", sectoidArmor } };

            var units = DataLoader.LoadUnits(_dir, armorsById);

            Assert.Equal(2, units.Count);
            var sectoid = units.Find(u => u.Id == "STR_SECTOID_SOLDIER");
            Assert.Same(sectoidArmor, sectoid.Armor);
            Assert.Equal(54, sectoid.Stats.TimeUnits);
            var soldier = units.Find(u => u.Id == "STR_SOLDIER");
            Assert.Equal(RuleArmor.None.Id, soldier.Armor.Id); // no ArmorId -> falls back to RuleArmor.None
            Assert.Equal(50, soldier.Stats.TimeUnits);
        }

        [Fact]
        public void LoadItems_ParsesWeaponFieldsIncludingClipDerivedPowerAndDamageType()
        {
            string json = @"[{
                ""Id"": ""STR_RIFLE"", ""TwoHanded"": true, ""Power"": 30, ""DamageType"": 1,
                ""AccuracySnap"": 60, ""AccuracyAimed"": 110, ""AccuracyAuto"": 35,
                ""TuSnap"": 25, ""TuAimed"": 80, ""TuAuto"": 35
            }]";
            File.WriteAllText(Path.Combine(_dir, "items.json"), json);

            var items = DataLoader.LoadItems(_dir);

            Assert.Single(items);
            var rifle = items[0];
            Assert.Equal("STR_RIFLE", rifle.Id);
            Assert.True(rifle.TwoHanded);
            Assert.Equal(30, rifle.Power);
            Assert.Equal(DamageType.Armor, rifle.DamageType); // raw rul value 1; Combat.cs doesn't branch on DamageType, so this is a label only
            Assert.Equal(110, rifle.AccuracyAimed);
            Assert.Equal(80, rifle.TuAimed);
        }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test --filter "FullyQualifiedName~DataLoaderTests"`
Expected: FAIL to build — `DataLoader.LoadArmors`/`LoadUnits`/`LoadItems` don't exist yet.

- [ ] **Step 3: Write the implementation**

In `unity/Assets/Scripts/Core/Rules/DataLoader.cs`, add these 3 public methods inside the `DataLoader` class, after `LoadMapBlock`:

```csharp
        public static List<RuleArmor> LoadArmors(string gameDataDir)
        {
            string path = Path.Combine(gameDataDir, "armors.json");
            string json = File.ReadAllText(path);
            var raw = JsonSerializer.Deserialize<List<RawArmorEntry>>(json, Options);

            var result = new List<RuleArmor>(raw.Count);
            foreach (var r in raw)
                result.Add(new RuleArmor(r.Id, r.Front, r.Side, r.Rear, r.Under));
            return result;
        }

        public static List<RuleUnit> LoadUnits(string gameDataDir, IReadOnlyDictionary<string, RuleArmor> armorsById)
        {
            string path = Path.Combine(gameDataDir, "units.json");
            string json = File.ReadAllText(path);
            var raw = JsonSerializer.Deserialize<List<RawUnitEntry>>(json, Options);

            var result = new List<RuleUnit>(raw.Count);
            foreach (var r in raw)
            {
                var armor = !string.IsNullOrEmpty(r.ArmorId) && armorsById.TryGetValue(r.ArmorId, out var a)
                    ? a : RuleArmor.None;
                var stats = new UnitStats
                {
                    TimeUnits = r.Stats.TimeUnits, Stamina = r.Stats.Stamina, Health = r.Stats.Health,
                    Bravery = r.Stats.Bravery, Reactions = r.Stats.Reactions, Firing = r.Stats.Firing,
                    Throwing = r.Stats.Throwing, Strength = r.Stats.Strength, Melee = r.Stats.Melee,
                };
                result.Add(new RuleUnit(r.Id, stats, armor));
            }
            return result;
        }

        public static List<RuleItem> LoadItems(string gameDataDir)
        {
            string path = Path.Combine(gameDataDir, "items.json");
            string json = File.ReadAllText(path);
            var raw = JsonSerializer.Deserialize<List<RawItemEntry>>(json, Options);

            var result = new List<RuleItem>(raw.Count);
            foreach (var r in raw)
            {
                result.Add(new RuleItem(r.Id)
                {
                    TwoHanded = r.TwoHanded,
                    Power = r.Power,
                    DamageType = (DamageType)r.DamageType,
                    AccuracySnap = r.AccuracySnap,
                    AccuracyAimed = r.AccuracyAimed,
                    AccuracyAuto = r.AccuracyAuto,
                    TuSnap = r.TuSnap,
                    TuAimed = r.TuAimed,
                    TuAuto = r.TuAuto,
                });
            }
            return result;
        }
```

Add these 5 private DTO classes inside the `DataLoader` class, alongside the existing `RawMcdRecord`/`RawDatasetInfo`/etc.:

```csharp
        private sealed class RawUnitStats
        {
            public int TimeUnits { get; set; }
            public int Stamina { get; set; }
            public int Health { get; set; }
            public int Bravery { get; set; }
            public int Reactions { get; set; }
            public int Firing { get; set; }
            public int Throwing { get; set; }
            public int Strength { get; set; }
            public int Melee { get; set; }
        }

        private sealed class RawUnitEntry
        {
            public string Id { get; set; }
            public RawUnitStats Stats { get; set; }
            public string ArmorId { get; set; }
        }

        private sealed class RawArmorEntry
        {
            public string Id { get; set; }
            public int Front { get; set; }
            public int Side { get; set; }
            public int Rear { get; set; }
            public int Under { get; set; }
        }

        private sealed class RawItemEntry
        {
            public string Id { get; set; }
            public bool TwoHanded { get; set; }
            public int Power { get; set; }
            public int DamageType { get; set; }
            public int AccuracySnap { get; set; }
            public int AccuracyAimed { get; set; }
            public int AccuracyAuto { get; set; }
            public int TuSnap { get; set; }
            public int TuAimed { get; set; }
            public int TuAuto { get; set; }
        }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test --filter "FullyQualifiedName~DataLoaderTests"`
Expected: PASS (6/6 — 3 existing + 3 new).

- [ ] **Step 5: Run the full test suite (regression check)**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test`
Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add unity/Assets/Scripts/Core/Rules/DataLoader.cs unity/Tests.Standalone/DataLoaderTests.cs
git commit -m "feat(core): DataLoader.LoadUnits/LoadArmors/LoadItems"
```

---

### Task 4: `OpenXcom.Unity` — `IsoProjection.PartRank.Unit`

This task is pure C# with no `UnityEngine` reference (same as the rest of
`Rendering/IsoProjection.cs`), so it's fully verifiable by `dotnet test` —
no Editor needed for this one.

**Files:**
- Modify: `unity/Assets/Scripts/Unity/Rendering/IsoProjection.cs`
- Modify: `unity/Tests.Standalone/Unity/IsoProjectionTests.cs`

**Interfaces:**
- Consumes: existing `IsoProjection.MapToScreen`/`SortingOrder`/`PartRank` (unchanged behavior for `Floor`/`WestWall`/`NorthWall`/`Object`).
- Produces (used by Task 5): `IsoProjection.PartRank.Unit`, and `SortingOrder`'s per-tile multiplier changing from 4 to 5 slots (still strictly monotonic tile-to-tile, so nothing that already used `SortingOrder` for relative Floor<WestWall<NorthWall<Object<next-tile ordering breaks).

- [ ] **Step 1: Write the failing test**

Add this test to `unity/Tests.Standalone/Unity/IsoProjectionTests.cs`, inside the existing `IsoProjectionTests` class:

```csharp
        [Fact]
        public void SortingOrder_UnitRankIsAboveObjectButBelowTheNextTilesFloor()
        {
            int obj = IsoProjection.SortingOrder(5, 5, 0, 10, 10, IsoProjection.PartRank.Object);
            int unit = IsoProjection.SortingOrder(5, 5, 0, 10, 10, IsoProjection.PartRank.Unit);
            int nextTileFloor = IsoProjection.SortingOrder(6, 5, 0, 10, 10, IsoProjection.PartRank.Floor);
            Assert.True(unit > obj);
            Assert.True(unit < nextTileFloor);
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test --filter "FullyQualifiedName~IsoProjectionTests"`
Expected: FAIL to build — `PartRank.Unit` doesn't exist yet.

- [ ] **Step 3: Write the implementation**

In `unity/Assets/Scripts/Unity/Rendering/IsoProjection.cs`, change the `PartRank` enum:

```csharp
        /// <summary>Tile-part draw rank within one tile: floor, west wall, north wall, object, unit.</summary>
        public enum PartRank
        {
            Floor = 0,
            WestWall = 1,
            NorthWall = 2,
            Object = 3,
            Unit = 4,
        }
```

And change `SortingOrder`'s per-tile multiplier from 4 to 5 (matches the parent spec's documented draw order, §5: "floor→west wall→north wall→object→unit→items"):

```csharp
        public static int SortingOrder(int x, int y, int z, int mapWidth, int mapLength, PartRank part)
        {
            long tileIndex = ((long)z * mapLength + y) * mapWidth + x;
            return (int)(tileIndex * 5 + (int)part);
        }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test --filter "FullyQualifiedName~IsoProjectionTests"`
Expected: PASS (6/6 — 5 existing + 1 new). The 5 existing tests all compare `SortingOrder` results relationally (`<`/`>`), not against hardcoded literals, so they remain valid under the new multiplier.

- [ ] **Step 5: Run the full test suite (regression check)**

Run: `export PATH="$HOME/.dotnet:$PATH" && cd unity/Tests.Standalone && dotnet test`
Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add unity/Assets/Scripts/Unity/Rendering/IsoProjection.cs unity/Tests.Standalone/Unity/IsoProjectionTests.cs
git commit -m "feat(unity): IsoProjection.PartRank.Unit - units draw above tile objects, below the next tile"
```

---

### Task 5: `OpenXcom.Unity` — `AtlasLoader`, `UnitRenderer`, `TileRenderer` collider

**This task requires a live Unity Editor.** Verify via Coplay MCP:
`mcp__coplay-mcp__check_compile_errors` after writing the code (fetch its
schema and neighbors via `ToolSearch` if not already loaded this session —
query `"select:mcp__coplay-mcp__check_compile_errors"`). There's no scene
yet to visually test in, so compile-clean is this task's bar; Task 6 is
where these components get exercised live.

**Files:**
- Create: `unity/Assets/Scripts/Unity/Rendering/AtlasLoader.cs`
- Create: `unity/Assets/Scripts/Unity/Rendering/UnitRenderer.cs`
- Modify: `unity/Assets/Scripts/Unity/Rendering/TileRenderer.cs` (add collider)
- Modify: `unity/Assets/Scripts/Unity/BattlescapeMapView.cs` (use `AtlasLoader`, expose `Grid`, load in `Awake`)

**Interfaces:**
- Consumes: `IsoProjection.MapToScreen`/`SortingOrder`/`PartRank.Unit` (Task 4), existing `TileRenderer.PixelsPerUnit`, `MapGenerator.Build`, `DataLoader.LoadTerrain`/`LoadTiles`/`LoadMapBlock`.
- Produces (used by Task 6): `OpenXcom.Unity.Rendering.AtlasLoader.Load(string gameDataDir, string baseName) -> (Texture2D texture, List<Rect> frameRects)`, `OpenXcom.Unity.Rendering.UnitRenderer.Setup(int x, int y, int z, int mapWidth, int mapLength, Sprite sprite)`, `OpenXcom.Unity.BattlescapeMapView.Grid` (public `TileGrid` property, populated in `Awake()` before any `Start()` runs), `TileRenderer` now carries a `BoxCollider` sized to one tile footprint.

- [ ] **Step 1: Extract `AtlasLoader` from `BattlescapeMapView`**

Create `unity/Assets/Scripts/Unity/Rendering/AtlasLoader.cs` — this is `BattlescapeMapView`'s existing private `LoadAtlas` method and its two `[System.Serializable]` structs, moved verbatim and made public:

```csharp
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace OpenXcom.Unity.Rendering
{
    /// <summary>
    /// Loads one converted sprite atlas (PNG + frame-rect JSON) from
    /// Assets/GameData/. Shared by BattlescapeMapView (tile atlases) and
    /// BattlescapeBootstrap (unit atlases) - extracted from
    /// BattlescapeMapView's original private LoadAtlas so both can use it
    /// without duplicating the atlas-frame-rect-flip logic.
    /// </summary>
    public static class AtlasLoader
    {
        public static (Texture2D texture, List<Rect> frameRects) Load(string gameDataDir, string baseName)
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

        [System.Serializable]
        private struct AtlasFrameJson { public int x, y, w, h; }

        [System.Serializable]
        private struct AtlasFramesJson { public AtlasFrameJson[] frames; }
    }
}
```

- [ ] **Step 2: Update `BattlescapeMapView` to use `AtlasLoader`, load in `Awake`, and expose `Grid`**

Replace the full contents of `unity/Assets/Scripts/Unity/BattlescapeMapView.cs`:

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
    /// Runs in Awake (not Start) so BattlescapeBootstrap - which needs this
    /// component's Grid to already exist - can safely read it from its own
    /// Start(): Unity guarantees every component's Awake() on a GameObject
    /// runs before any component's Start() on that same GameObject.
    /// </summary>
    public sealed class BattlescapeMapView : MonoBehaviour
    {
        [SerializeField] private string terrainName = "CULTA";
        [SerializeField] private string mapBlockName = "CULTA00";
        [SerializeField] private string[] datasetNames = { "BLANKS", "CULTIVAT", "BARN" };

        /// <summary>The battle grid built from the mapblock this view rendered. Populated by Awake().</summary>
        public TileGrid Grid { get; private set; }

        private void Awake()
        {
            string gameDataDir = Path.Combine(Application.dataPath, "GameData");

            var terrain = DataLoader.LoadTerrain(gameDataDir, terrainName);
            var datasetTiles = new Dictionary<string, List<MapDataTile>>();
            var datasetAtlases = new Dictionary<string, (Texture2D texture, List<Rect> frameRects)>();

            foreach (var name in datasetNames)
            {
                datasetTiles[name] = DataLoader.LoadTiles(gameDataDir, name);
                datasetAtlases[name] = AtlasLoader.Load(gameDataDir, $"terrain-{name}");
            }

            var block = DataLoader.LoadMapBlock(gameDataDir, mapBlockName);
            Grid = MapGenerator.Build(block, terrain, datasetTiles);

            for (int z = 0; z < Grid.Height; z++)
            {
                for (int y = 0; y < Grid.Length; y++)
                {
                    for (int x = 0; x < Grid.Width; x++)
                    {
                        var tile = Grid.At(x, y, z);
                        if (tile.Floor == null && tile.WestWall == null &&
                            tile.NorthWall == null && tile.Object == null)
                            continue;

                        var go = new GameObject($"Tile_{x}_{y}_{z}");
                        go.transform.SetParent(transform, worldPositionStays: false);
                        var renderer = go.AddComponent<TileRenderer>();

                        renderer.Setup(x, y, z, Grid.Width, Grid.Length,
                            floorSprite: SpriteFor(tile.Floor, datasetAtlases),
                            floorYOffsetPixels: tile.Floor?.YOffset ?? 0,
                            westWallSprite: SpriteFor(tile.WestWall, datasetAtlases),
                            northWallSprite: SpriteFor(tile.NorthWall, datasetAtlases),
                            objectSprite: SpriteFor(tile.Object, datasetAtlases));
                    }
                }
            }
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
            return Sprite.Create(texture, rect, new Vector2(0.5f, 0f), pixelsPerUnit: Rendering.TileRenderer.PixelsPerUnit);
        }
    }
}
```

(This deletes `BattlescapeMapView`'s old private `LoadAtlas` method and its
two nested `AtlasFrameJson`/`AtlasFramesJson` structs — they now live in
`AtlasLoader`. The only other changes are `Start` → `Awake`, `grid` (local)
→ `Grid` (public property), and calling `AtlasLoader.Load` instead of the
old private method.)

- [ ] **Step 3: Add a collider to `TileRenderer`**

In `unity/Assets/Scripts/Unity/Rendering/TileRenderer.cs`, add a `BoxCollider`
in `Awake` sized to roughly one tile's iso footprint (32×16px at
`PixelsPerUnit`=32 → 1×0.5 world units), centered on the tile origin used for
picking:

```csharp
        private BoxCollider _collider;

        private void Awake()
        {
            _floor = CreateChild("Floor");
            _westWall = CreateChild("WestWall");
            _northWall = CreateChild("NorthWall");
            _object = CreateChild("Object");

            _collider = gameObject.AddComponent<BoxCollider>();
            _collider.size = new Vector3(1f, 0.5f, 0.1f);
        }
```

(Add this `_collider` field near the other `_floor`/`_westWall`/etc. fields.
`BoxCollider` — 3D, not `BoxCollider2D` — because `BattleController.Update()`
already calls `Physics.Raycast`, the 3D physics API; a 2D collider would
never be hit by it.)

- [ ] **Step 4: Write `UnitRenderer`**

Create `unity/Assets/Scripts/Unity/Rendering/UnitRenderer.cs`:

```csharp
using UnityEngine;

namespace OpenXcom.Unity.Rendering
{
    /// <summary>
    /// Draws one unit as a single sprite, positioned via the same
    /// IsoProjection math TileRenderer uses. Pure display: takes an
    /// already-resolved Sprite and a tile position, makes no gameplay
    /// decisions. [SIMPLIFIED] one static frame - no direction/walk-phase
    /// animation this slice (parent spec §5 names that as later work).
    /// </summary>
    public sealed class UnitRenderer : MonoBehaviour
    {
        private SpriteRenderer _renderer;
        private BoxCollider _collider;

        private void Awake()
        {
            _renderer = gameObject.AddComponent<SpriteRenderer>();
            _collider = gameObject.AddComponent<BoxCollider>();
            _collider.size = new Vector3(0.6f, 1f, 0.1f);
        }

        public void Setup(int x, int y, int z, int mapWidth, int mapLength, Sprite sprite)
        {
            var (screenX, screenY) = IsoProjection.MapToScreen(x, y, z);
            transform.localPosition = new Vector3(screenX / TileRenderer.PixelsPerUnit, screenY / TileRenderer.PixelsPerUnit, 0f);

            _renderer.sprite = sprite;
            _renderer.sortingOrder = IsoProjection.SortingOrder(x, y, z, mapWidth, mapLength, IsoProjection.PartRank.Unit);
        }
    }
}
```

- [ ] **Step 5: Compile check in the Editor**

Use the Coplay MCP tool to confirm the whole project still compiles clean
(fetch its schema first if not already loaded this session):

```
ToolSearch(query: "select:mcp__coplay-mcp__check_compile_errors")
mcp__coplay-mcp__check_compile_errors()
```

Expected: "No compile errors". If there are errors, fix them before
proceeding — this task has no other verification gate.

- [ ] **Step 6: Commit**

```bash
git add unity/Assets/Scripts/Unity/Rendering/AtlasLoader.cs unity/Assets/Scripts/Unity/Rendering/UnitRenderer.cs unity/Assets/Scripts/Unity/Rendering/TileRenderer.cs unity/Assets/Scripts/Unity/BattlescapeMapView.cs
git commit -m "feat(unity): AtlasLoader, UnitRenderer, and TileRenderer/UnitRenderer colliders for input picking"
```

---

### Task 6: `OpenXcom.Unity` — `BattlescapeBootstrap` + scene + live play verification

**This is the task that actually closes the phase's goal.** Requires the
live Unity Editor connection. Regenerate real `GameData` first — none of
this renders without it.

**Files:**
- Create: `unity/Assets/Scripts/Unity/BattlescapeBootstrap.cs`
- Create (in the Editor, via Coplay, not by hand-authoring YAML): `unity/Assets/Scenes/Battlescape.unity`

**Interfaces:**
- Consumes: `BattlescapeMapView.Grid` (Task 5), `AtlasLoader.Load`, `UnitRenderer.Setup` (Task 5), `DataLoader.LoadArmors`/`LoadUnits`/`LoadItems` (Task 3), `BattleState` constructor + `Units`/`Grid` (existing), `BattleUnit` constructor + `Position`/`RightHand` (existing), `BattleItem` constructor (existing), `BattleController.Bind` (existing).
- Produces: nothing consumed by a later task — terminal task of this plan and this phase.

- [ ] **Step 1: Regenerate real `GameData`**

Run from `unity/`:

```bash
export PATH="$HOME/.dotnet:$PATH"
cd unity
dotnet run --project Xcom.Convert -- --data RawData/Resources/UFO --rules ../bin/standard/xcom1 --out Assets/GameData
```

Expected: `Wrote 20 files to Assets/GameData`. Confirm
`Assets/GameData/units-SECTOID.png`, `units.json`, `armors.json`,
`items.json` all exist (`ls unity/Assets/GameData/`).

- [ ] **Step 2: Write `BattlescapeBootstrap`**

Create `unity/Assets/Scripts/Unity/BattlescapeBootstrap.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenXcom.Core.Battle;
using OpenXcom.Core.Common;
using OpenXcom.Core.Rules;
using OpenXcom.Unity.Rendering;
using UnityEngine;

namespace OpenXcom.Unity
{
    /// <summary>
    /// Assembles a real BattleState (real soldier/Sectoid stats, real
    /// weapons) and binds it to BattleController. Requires
    /// BattlescapeMapView on the same GameObject: reads its already-built
    /// Grid rather than reloading/rebuilding the mapblock a second time.
    ///
    /// Squad is placed at fixed coordinates, not BattleState.SpawnAtRouteNodes
    /// - CULTA00's .RMP has only one route node (verified directly against
    /// the raw file: 24 bytes = one record), so route-node spawning can't
    /// seat a 4-unit squad. Every tile in CULTA00 is walkable, so any
    /// coordinates work; these three put the two soldiers together in one
    /// corner and the two Sectoids together in the opposite corner.
    /// </summary>
    [RequireComponent(typeof(BattlescapeMapView))]
    [RequireComponent(typeof(BattleController))]
    public sealed class BattlescapeBootstrap : MonoBehaviour
    {
        private static readonly Position SoldierAPos = new(1, 1, 0);
        private static readonly Position SoldierBPos = new(2, 1, 0);
        private static readonly Position SectoidAPos = new(8, 8, 0);
        private static readonly Position SectoidBPos = new(7, 8, 0);

        private void Start()
        {
            string gameDataDir = Path.Combine(Application.dataPath, "GameData");
            var grid = GetComponent<BattlescapeMapView>().Grid;

            var armorsById = DataLoader.LoadArmors(gameDataDir).ToDictionary(a => a.Id);
            var unitsById = DataLoader.LoadUnits(gameDataDir, armorsById).ToDictionary(u => u.Id);
            var itemsById = DataLoader.LoadItems(gameDataDir).ToDictionary(i => i.Id);

            var xcomAtlas = AtlasLoader.Load(gameDataDir, "units-XCOM_0");
            var sectoidAtlas = AtlasLoader.Load(gameDataDir, "units-SECTOID");

            var state = new BattleState(grid);
            var unitTransforms = new Dictionary<BattleUnit, Transform>();

            Spawn(state, grid, unitTransforms, unitsById["STR_SOLDIER"], itemsById["STR_RIFLE"],
                Faction.Player, "Soldier A", SoldierAPos, xcomAtlas);
            Spawn(state, grid, unitTransforms, unitsById["STR_SOLDIER"], itemsById["STR_RIFLE"],
                Faction.Player, "Soldier B", SoldierBPos, xcomAtlas);
            Spawn(state, grid, unitTransforms, unitsById["STR_SECTOID_SOLDIER"], itemsById["STR_PLASMA_PISTOL"],
                Faction.Hostile, "Sectoid A", SectoidAPos, sectoidAtlas);
            Spawn(state, grid, unitTransforms, unitsById["STR_SECTOID_SOLDIER"], itemsById["STR_PLASMA_PISTOL"],
                Faction.Hostile, "Sectoid B", SectoidBPos, sectoidAtlas);

            GetComponent<BattleController>().Bind(state, unitTransforms);
        }

        private void Spawn(BattleState state, OpenXcom.Core.Battle.TileGrid grid,
            Dictionary<BattleUnit, Transform> unitTransforms,
            RuleUnit ruleUnit, RuleItem weapon, Faction faction, string name, Position position,
            (Texture2D texture, List<Rect> frameRects) atlas)
        {
            var unit = new BattleUnit(ruleUnit, faction, name)
            {
                Position = position,
                RightHand = new BattleItem(weapon),
            };
            grid.At(position.X, position.Y, position.Z).Occupant = unit;
            state.Units.Add(unit);

            var go = new GameObject(name);
            go.transform.SetParent(transform, worldPositionStays: false);
            var renderer = go.AddComponent<UnitRenderer>();
            var sprite = Sprite.Create(atlas.texture, atlas.frameRects[0], new Vector2(0.5f, 0f), TileRenderer.PixelsPerUnit);
            renderer.Setup(position.X, position.Y, position.Z, grid.Width, grid.Length, sprite);

            unitTransforms[unit] = go.transform;
        }
    }
}
```

- [ ] **Step 3: Compile check**

```
mcp__coplay-mcp__check_compile_errors()
```

Expected: "No compile errors". Fix any before proceeding.

- [ ] **Step 4: Build the scene live via Coplay**

Fetch schemas first if not already loaded this session:
`ToolSearch(query: "select:mcp__coplay-mcp__create_scene,mcp__coplay-mcp__create_game_object,mcp__coplay-mcp__add_component,mcp__coplay-mcp__set_property,mcp__coplay-mcp__set_tag,mcp__coplay-mcp__save_scene")`

1. `create_scene(scene_name: "Battlescape", scene_path: "Assets/Scenes")`
2. `create_game_object(name: "Main Camera", position: "0,0,-10")`, then
   `add_component(gameobject_path: "Main Camera", component_type: "Camera")`,
   `set_property(gameobject_path: "Main Camera", component_type: "Camera", property_name: "orthographic", value: "true")`,
   `set_property(gameobject_path: "Main Camera", component_type: "Camera", property_name: "orthographicSize", value: "5")`,
   `set_tag(gameobject_path: "Main Camera", tag_name: "MainCamera")` (schema
   confirmed this session: `set_tag`'s parameters are `gameobject_path` and
   `tag_name`, not `tag`).
3. `create_game_object(name: "Battlescape", position: "0,0,0")`, then in
   order: `add_component(gameobject_path: "Battlescape", component_type: "BattlescapeMapView")`,
   `add_component(gameobject_path: "Battlescape", component_type: "BattleController")`,
   `add_component(gameobject_path: "Battlescape", component_type: "BattlescapeBootstrap")`
   (the `[RequireComponent]` attributes mean the latter two must go on
   after `BattlescapeMapView` exists on the object, and `BattleController`
   before `BattlescapeBootstrap` — this order satisfies that).
4. `set_property(gameobject_path: "Battlescape", component_type: "BattleController", property_name: "raycastCamera", value: "Main Camera")` —
   wires the `[SerializeField] private Camera raycastCamera` to the camera
   created in step 2.
5. `save_scene(scene_name: "Battlescape")`.

- [ ] **Step 5: Play and verify the loop end to end**

Fetch schemas if needed:
`ToolSearch(query: "select:mcp__coplay-mcp__play_game,mcp__coplay-mcp__stop_game,mcp__coplay-mcp__get_unity_logs,mcp__coplay-mcp__capture_scene_object")`

1. `play_game()`.
2. `get_unity_logs()` — confirm no errors/exceptions on scene load (no
   `NullReferenceException`, no "not found" from `DataLoader`, etc.).
3. `capture_scene_object()` — confirm the CULTA00 tile grid and all 4 unit
   sprites are visible, roughly in the two-corners layout described in
   `BattlescapeBootstrap`'s doc comment. If units aren't visible or are
   mispositioned, this is the point to debug — check `UnitRenderer.Setup`'s
   `PixelsPerUnit` division and `IsoProjection.MapToScreen` inputs first.
4. Manually drive input via the Unity Editor's Game view (Coplay doesn't
   simulate mouse/keyboard input) or `execute_script` to programmatically
   call `BattleController`'s private methods for a scripted smoke test if
   direct interaction isn't practical in this session — at minimum, verify
   via `get_unity_logs` that ending the turn (whichever mechanism is used)
   produces the expected `Debug.Log` sequence: a hostile `ProjectileFiredEvent`
   or `UnitMovedEvent` from `AiModule`, a `TurnChanged: Player` log, and
   that repeating end-turn until one side is wiped produces exactly one
   `Battle over: <Outcome>` log.
5. `stop_game()`.
6. Report the verification outcome explicitly — what was actually observed
   (screenshot description, log contents), not just "should work."

- [ ] **Step 6: Commit**

```bash
git add unity/Assets/Scripts/Unity/BattlescapeBootstrap.cs unity/Assets/Scenes/Battlescape.unity unity/Assets/Scenes/Battlescape.unity.meta
git commit -m "feat(unity): BattlescapeBootstrap + Battlescape scene - first live Editor run of the skirmish loop"
```

(Include the `.meta` file Unity generates for the new scene — `.meta` files
are how Unity tracks asset GUIDs and must be committed alongside their
asset.)
