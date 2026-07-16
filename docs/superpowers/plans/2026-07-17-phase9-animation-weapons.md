# Phase 9 — Animation & Weapon Visuals Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Units turn to face where they move/shoot, walk through a real animation cycle, die visibly instead of vanishing, and the two starter weapons (Rifle, Plasma Pistol) render in soldiers'/Sectoids' hands with a firing pose and a real sprite-based bullet that flies along Phase 8's traced voxel path.

**Architecture:** Pure frame-index arithmetic lives in a new Core-testable `UnitSpriteFrames` helper (mirroring `IsoProjection`'s "no UnityEngine dependency" pattern); `UnitRenderer` (Unity) becomes frame-driven (`SetFrame`/`SetDeathFrame`) instead of a one-shot static `Setup`; `BattleController` drives frame changes from the events it already drains (`UnitMovedEvent`, `ProjectileFiredEvent`, `UnitDiedEvent`) using one shared timed-sequence mechanism for the two duration-based animations (firing pose, death) plus per-tile-step-driven walk-phase advancement (a different, position-driven trigger, kept separate deliberately — see Task 6). `Xcom.Convert` gains two new sprite-sheet conversions (`HANDOB.PCK`, `BulletSprites.png`) reusing existing decoder/writer code paths unchanged.

**Tech Stack:** C#/.NET 8, Unity (SDL-original-data era assets), xUnit (`Tests.Standalone`), Newtonsoft.Json, ImageSharp (Xcom.Convert only).

## Global Constraints

- Port, don't approximate: every frame constant/formula below is cited to `src/Battlescape/UnitSprite.cpp` (or the cited sibling file); every place this plan deliberately cuts fidelity is marked **[SIMPLIFIED]** with what's cut and why, matching Phase 7/8's discipline.
- Direction convention: 0=N, 1=NE, 2=E, 3=SE, 4=S, 5=SW, 6=W, 7=NW, clockwise (`OpenXcom.Core.Common.Directions`, already exists, matches OXCE).
- Core code must use `Newtonsoft.Json`, never `System.Text.Json` (Unity's scripting backend can't load `System.Text.Json` — this bit Phase 8 Task 3, see `.superpowers/sdd/progress.md`).
- Weapons in scope: **Rifle (`STR_RIFLE`, two-handed) and Plasma Pistol (`STR_PLASMA_PISTOL`, one-handed) only** — no other `RuleItem` gets a hand sprite or bullet sprite this phase.
- Test command (Core tasks): `export PATH="$HOME/.dotnet:$PATH" && dotnet test unity/Tests.Standalone/OpenXcom.Core.Tests.csproj --filter <FilterExpr>` then the full suite with no filter before committing.
- Unity-only files (`unity/Assets/Scripts/Unity/**`, excluding the specific pure-math files this plan adds to `Tests.Standalone`'s `.csproj`) are **not** compiled by `dotnet test` — verify those via a live Coplay-MCP-connected Unity Editor (`check_compile_errors`, then `play_game`/`get_unity_logs`/`stop_game`) per Phase 6/7/8 precedent. If no Editor is connected when a task needing this verification runs, say so explicitly rather than claiming it was checked.
- Never work in a git worktree for this project — continue directly on `oxce-plus` (standing user preference, all of Phase 8 was done this way).
- All new/changed public members get an XML `///` doc comment citing the C++ source, matching every existing file in this codebase.

---

## Task 1: Core facing — `BattleState.TryMove`/`TryFire` set `BattleUnit.Direction`

`BattleUnit.Direction` (`unity/Assets/Scripts/Core/Battle/BattleUnit.cs:19`) already exists and is already unused. `Directions.IndexOf` (`unity/Assets/Scripts/Core/Common/Position.cs:59`) and `TileEngine.GetDirectionTo` (`unity/Assets/Scripts/Core/Battle/TileEngine.cs:215`, Phase 8) already compute everything needed — this task only wires them in.

**Files:**
- Modify: `unity/Assets/Scripts/Core/Battle/BattleEvent.cs` (`UnitMovedEvent` gains a `From` position)
- Modify: `unity/Assets/Scripts/Core/Battle/BattleState.cs` (`TryMove`, `TryFire`)
- Modify: `unity/Tests.Standalone/BattleStateTests.cs` (fix one existing 2-arg `UnitMovedEvent(...)` call site; add 2 new tests)
- Modify: `unity/Tests.Standalone/BattleStateFireTests.cs` (add 1 new test)

**Interfaces:**
- Consumes: `Directions.IndexOf(Position) -> int` (exact 8-neighbor match only; every `Pathfinding.FindPath` step is guaranteed to be exactly one of `Directions.Offsets`, per `Pathfinding.cs:92`'s `neighbor = current + Directions.Offsets[dir]`, so this never returns -1 for a movement step). `TileEngine.GetDirectionTo(Position origin, Position target) -> int` (nearest-of-8, works for any two positions, already used by Phase 8's `GetOriginVoxel`).
- Produces: `UnitMovedEvent.From` (the tile the unit started this move from, before any step) — Task 6's `BattleController` needs this to reconstruct each step's facing direction, since by the time the event is drained, `BattleUnit.Position`/`Direction` already reflect the *final* step, not each intermediate one.

- [ ] **Step 1: Write the failing tests**

In `unity/Tests.Standalone/BattleStateTests.cs`, fix the existing 2-arg call (this will otherwise fail to compile once `UnitMovedEvent`'s constructor changes in Step 3) and add two new tests. Replace:

```csharp
            state.Enqueue(new UnitMovedEvent(unit, new List<Position> { new(1, 0, 0) }));
```

with:

```csharp
            state.Enqueue(new UnitMovedEvent(unit, new Position(0, 0, 0), new List<Position> { new(1, 0, 0) }));
```

Then add these two tests at the end of the class, right before the final closing `}`:

```csharp
        [Fact]
        public void TryMove_EachStepSetsUnitDirectionAndEventCarriesTheStartPosition()
        {
            var grid = new TileGrid(5, 1, 1);
            var floor = new MapDataTile { TuWalk = 4 };
            for (int x = 0; x < 5; x++) grid.At(x, 0, 0).Floor = floor;

            var unit = new BattleUnit(RuleUnit.Soldier, Faction.Player) { Position = new Position(0, 0, 0) };
            unit.TimeUnits = 100;
            grid.At(0, 0, 0).Occupant = unit;
            var state = new BattleState(grid);
            state.Units.Add(unit);

            state.TryMove(unit, new Position(3, 0, 0)); // 3 steps east: direction 2 every step

            Assert.Equal(2, unit.Direction);
            var moved = Assert.IsType<UnitMovedEvent>(state.DequeueEvents()[0]);
            Assert.Equal(new Position(0, 0, 0), moved.From);
        }

        [Fact]
        public void TryMove_PartialMoveStillSetsDirectionForStepsActuallyWalked()
        {
            var grid = new TileGrid(5, 1, 1);
            var floor = new MapDataTile { TuWalk = 4 };
            for (int x = 0; x < 5; x++) grid.At(x, 0, 0).Floor = floor;

            var unit = new BattleUnit(RuleUnit.Soldier, Faction.Player) { Position = new Position(0, 0, 0) };
            unit.TimeUnits = 9; // enough for 2 steps only (see TryMove_InsufficientBudget... above)
            grid.At(0, 0, 0).Occupant = unit;
            var state = new BattleState(grid);
            state.Units.Add(unit);

            state.TryMove(unit, new Position(3, 0, 0));

            Assert.Equal(2, unit.Direction); // still facing east after the 2 affordable steps
        }
```

In `unity/Tests.Standalone/BattleStateFireTests.cs`, add this test (place it near the other `TryFire_...` tests, using the file's existing `MakeGuaranteedHitAttacker`/`MakeFullTileDefender`/`MakeOpenBattle` helpers as-is):

```csharp
        [Fact]
        public void TryFire_SetsAttackerDirectionToFaceTheDefender()
        {
            var (grid, state) = MakeOpenBattle(width: 25);
            state.LoftData = FullTileLoftData();
            var attacker = MakeGuaranteedHitAttacker(new Position(5, 0, 0));
            var defender = MakeFullTileDefender(new Position(5, 3, 0), health: 100); // due south of attacker
            grid.At(5, 0, 0).Occupant = attacker;
            grid.At(5, 3, 0).Occupant = defender;
            for (int y = 0; y <= 3; y++) grid.At(5, y, 0).Floor = new MapDataTile { StopLOS = false };
            state.Units.Add(attacker);
            state.Units.Add(defender);

            state.TryFire(attacker, attacker.RightHand, BattleActionType.AimedShot, defender);

            Assert.Equal(4, attacker.Direction); // south, matching TileEngine.GetDirectionTo's own citation-backed test
        }
```

If `FullTileLoftData()`/`FullTileArmor()` aren't visible from this test's position in the file (they're private static helpers already in this class per Phase 8 Task 10), no change needed — they're already file-scoped and usable from any test in the same class.

- [ ] **Step 2: Run tests to verify they fail**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet test unity/Tests.Standalone/OpenXcom.Core.Tests.csproj --filter "FullyQualifiedName~TryMove_EachStepSetsUnitDirectionAndEventCarriesTheStartPosition|FullyQualifiedName~TryMove_PartialMoveStillSetsDirectionForStepsActuallyWalked|FullyQualifiedName~TryFire_SetsAttackerDirectionToFaceTheDefender"`
Expected: compile error (`UnitMovedEvent` has no 3-arg constructor yet; `BattleUnit.Direction` is never set to anything but 0) or, once the 2-arg-call-site fix alone is reverted, a straightforward assertion failure. Either way, confirm it's not passing yet before implementing.

- [ ] **Step 3: Implement**

In `unity/Assets/Scripts/Core/Battle/BattleEvent.cs`, replace the `UnitMovedEvent` class:

```csharp
    public sealed class UnitMovedEvent : BattleEvent
    {
        public BattleUnit Unit { get; }
        public Position From { get; }
        public IReadOnlyList<Position> Path { get; }

        public UnitMovedEvent(BattleUnit unit, Position from, IReadOnlyList<Position> path)
        {
            Unit = unit;
            From = from;
            Path = path;
        }
    }
```

In `unity/Assets/Scripts/Core/Battle/BattleState.cs`, in `TryMove`, replace:

```csharp
            var walked = new List<Position>();
            var previous = unit.Position;

            foreach (var step in fullPath)
            {
                if (!unit.CanSpend(step.StepCost))
                    break;

                unit.Spend(step.StepCost);
                Grid[previous].Occupant = null;
                unit.Position = step.Position;
                Grid[step.Position].Occupant = unit;
                walked.Add(step.Position);
                previous = step.Position;
            }

            if (walked.Count == 0)
                return new MoveResult { Outcome = MoveOutcome.Failed, Path = System.Array.Empty<Position>() };

            Enqueue(new UnitMovedEvent(unit, walked));
```

with:

```csharp
            var walked = new List<Position>();
            var previous = unit.Position;
            var startPosition = previous;

            foreach (var step in fullPath)
            {
                if (!unit.CanSpend(step.StepCost))
                    break;

                unit.Spend(step.StepCost);
                Grid[previous].Occupant = null;
                // Every Pathfinding step is exactly one of Directions.Offsets
                // (Pathfinding.cs:92: neighbor = current + Directions.Offsets[dir]),
                // so IndexOf here is never -1.
                unit.Direction = Directions.IndexOf(step.Position - previous);
                unit.Position = step.Position;
                Grid[step.Position].Occupant = unit;
                walked.Add(step.Position);
                previous = step.Position;
            }

            if (walked.Count == 0)
                return new MoveResult { Outcome = MoveOutcome.Failed, Path = System.Array.Empty<Position>() };

            Enqueue(new UnitMovedEvent(unit, startPosition, walked));
```

In the same file, in `TryFire`, right after `attacker.Spend(tuCost);` add:

```csharp
            // Face the defender before resolving the shot (parent design spec
            // §2) - reuses Phase 8's own nearest-of-8 helper rather than a new
            // one, since attacker-to-defender deltas are rarely an exact
            // Directions.Offsets match the way adjacent movement steps are.
            attacker.Direction = TileEngine.GetDirectionTo(attacker.Position, defender.Position);
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet test unity/Tests.Standalone/OpenXcom.Core.Tests.csproj`
Expected: full suite passes (175 pre-existing + 3 new = 178).

- [ ] **Step 5: Commit**

```bash
git add unity/Assets/Scripts/Core/Battle/BattleEvent.cs unity/Assets/Scripts/Core/Battle/BattleState.cs unity/Tests.Standalone/BattleStateTests.cs unity/Tests.Standalone/BattleStateFireTests.cs
git commit -m "feat(core): wire BattleUnit.Direction from TryMove/TryFire"
```

---

## Task 2: `Xcom.Convert` — `HANDOB.PCK` + `BulletSprites.png` conversion, `handSprite`/`bulletSprite` parsing

**Files:**
- Modify: `unity/Xcom.Convert/Decoders/RuleYamlDecoder.cs` (`ConvertedItem`, `RawItem`, `LoadWeapon`)
- Modify: `unity/Xcom.Convert/ConvertJob.cs` (2 new conversion steps)
- Modify: `unity/Tests.Standalone/Convert/RuleYamlDecoderTests.cs` (extend the existing `LoadWeapon_...` test)
- Modify: `unity/Tests.Standalone/Convert/ConvertJobTests.cs` (fix the `written.Count` assertion; add new file assertions)

**Interfaces:**
- Consumes: `PckDecoder.Load(byte[] pck, byte[] tab, int width, int height)` and `AtlasWriter.Build`/`.Save` (unchanged, already used 3x in `ConvertJob.cs` for `XCOM_0`/`SECTOID`/`CURSOR`). `GridSpriteSheet.Convert(string srcPngPath, string outPngPath, string outFramesJsonPath, int frameWidth, int frameHeight, int columns, int rows)` (unchanged, already used for `pathfinding.png`).
- Produces: `handob.png`/`handob.frames.json` (128 frames, 32×40, same shape as `units-XCOM_0.png`). `bulletsprites.png`/`bulletsprites.frames.json` (385 frames, 3×3, 35 cols × 11 rows). `ConvertedItem.HandSprite`/`.BulletSprite` (both `int`), consumed by Task 3.

Ground truth: `HANDOB.PCK`/`.TAB` already exist at `unity/RawData/Resources/UFO/UNITS/` (confirmed 128 frames: TAB's first 4 bytes are non-zero → 16-bit offsets → 256/2=128). `bin/standard/xcom1/items.rul` confirms `STR_RIFLE` has `handSprite: 0, bulletSprite: 2, twoHanded: true`; `STR_PLASMA_PISTOL` has `handSprite: 104, bulletSprite: 8` (no `twoHanded` key → false). `bin/standard/xcom1/Resources/BulletSprites/BulletSprites.png` is a 105×33 8-bit-indexed PNG (confirmed via `file`), grid 35×11 of 3×3 frames (`bin/standard/xcom1/extraSprites.rul:83-89`, `type: Projectiles, width: 105, height: 33, subX: 3, subY: 3`). `src/Mod/RuleItem.cpp:352-353`'s own comment: *"Projectiles: 0-384 entries ((105\*33)/(3\*3)) (35 sprites per projectile (0-34), 11 projectiles (0-10))"* — `loadSpriteOffset(..., "Projectiles", 35)` multiplies the raw `bulletSprite` YAML value by 35 to get the real atlas base offset, so Rifle's raw `2` → base offset `70`, Pistol's raw `8` → base offset `280`.

- [ ] **Step 1: Write the failing tests**

In `unity/Tests.Standalone/Convert/RuleYamlDecoderTests.cs`, extend the existing `LoadWeapon_MergesWeaponAccuracyFieldsWithClipPowerAndDamageType` test by adding these two assertions right after `Assert.Equal(1, rifle.DamageType);`:

```csharp
            Assert.Equal(0, rifle.HandSprite);
            Assert.Equal(70, rifle.BulletSprite); // raw bulletSprite=2 * 35 (RuleItem.cpp:353 loadSpriteOffset multiplier)
```

and right after `Assert.Equal(5, pistol.DamageType);`:

```csharp
            Assert.Equal(104, pistol.HandSprite);
            Assert.Equal(280, pistol.BulletSprite); // raw bulletSprite=8 * 35
```

In `unity/Tests.Standalone/Convert/ConvertJobTests.cs`, change `Assert.Equal(27, written.Count);` (both occurrences — the one right after the `Assert.Contains` block, and the one checking `manifest["files"]`) to `Assert.Equal(31, written.Count);` / `Assert.Equal(31, files.Count);`, and add these `Assert.Contains` lines right after the existing `pathfinding.frames.json` one:

```csharp
            Assert.Contains(written, p => p == "handob.png");
            Assert.Contains(written, p => p == "handob.frames.json");
            Assert.Contains(written, p => p == "bulletsprites.png");
            Assert.Contains(written, p => p == "bulletsprites.frames.json");
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet test unity/Tests.Standalone/OpenXcom.Core.Tests.csproj --filter "FullyQualifiedName~RuleYamlDecoderTests|FullyQualifiedName~ConvertJobTests"`
Expected: `RuleYamlDecoderTests` fails to compile (`ConvertedItem`/`RawItem` have no `HandSprite`/`BulletSprite` yet); `ConvertJobTests` fails on the `27` vs actual-27-still assertions (the new `Assert.Contains` lines fail since those files don't exist yet).

- [ ] **Step 3: Implement**

In `unity/Xcom.Convert/Decoders/RuleYamlDecoder.cs`, add two fields to `ConvertedItem`:

```csharp
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
        public int HandSprite;
        public int BulletSprite;
    }
```

Add two properties to the private `RawItem` DTO:

```csharp
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
            public int HandSprite { get; set; }
            public int BulletSprite { get; set; }
        }
```

In `LoadWeapon`, change the returned object:

```csharp
        /// <summary>
        /// Reads a weapon plus its clip's power/damageType, and the weapon's
        /// own handSprite/bulletSprite (RuleItem.h:390) - bulletSprite is
        /// stored already multiplied by 35 (RuleItem.cpp:352-353's
        /// loadSpriteOffset(..., "Projectiles", 35)), matching the real
        /// engine's actual atlas base-offset value, not the raw .rul number.
        /// </summary>
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
                HandSprite = weapon.HandSprite,
                BulletSprite = weapon.BulletSprite * 35,
            };
        }
```

In `unity/Xcom.Convert/ConvertJob.cs`, right after the existing "3e. Pathfinding.png" block (right before the `// 4. Mapblock CULTA00` comment), insert:

```csharp
            // 3f. HANDOB.PCK: weapon-in-hand sprites (Rifle handSprite=0,
            // Plasma Pistol handSprite=104 - both fit inside this set's 128
            // frames), same 32x40 PckDecoder/AtlasWriter pipeline as
            // XCOM_0.PCK/SECTOID.PCK above (UnitSprite.cpp:96-99 selectItem).
            var handobFrames = PckDecoder.Load(
                File.ReadAllBytes(Path.Combine(dataDir, "UNITS", "HANDOB.PCK")),
                File.ReadAllBytes(Path.Combine(dataDir, "UNITS", "HANDOB.TAB")), 32, 40);
            var handobAtlas = AtlasWriter.Build(handobFrames, pal);
            AtlasWriter.Save(handobAtlas,
                Path.Combine(outDir, "handob.png"),
                Path.Combine(outDir, "handob.frames.json"));
            written.Add("handob.png");
            written.Add("handob.frames.json");

            // 3g. BulletSprites.png: bullet-trail dot sheet (bin/standard/xcom1's
            // own extraSprites.rul "Projectiles" entry, RuleItem.cpp:352-353),
            // 105x33 8-bit indexed PNG, 35 cols x 11 rows of 3x3 frames - same
            // indexed-PNG colorkey handling GridSpriteSheet.Convert already
            // applies to Pathfinding.png.
            GridSpriteSheet.Convert(
                Path.Combine(rulesDir, "Resources", "BulletSprites", "BulletSprites.png"),
                Path.Combine(outDir, "bulletsprites.png"),
                Path.Combine(outDir, "bulletsprites.frames.json"),
                frameWidth: 3, frameHeight: 3, columns: 35, rows: 11);
            written.Add("bulletsprites.png");
            written.Add("bulletsprites.frames.json");
```

(`GridSpriteSheet` is already `using Xcom.Convert.Output;` in this file — no new `using` needed.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet test unity/Tests.Standalone/OpenXcom.Core.Tests.csproj`
Expected: full suite passes (178 + however many were added in Step 1 of this task; recount at commit time via the test runner's own summary — don't hardcode a guess here).

- [ ] **Step 5: Commit**

```bash
git add unity/Xcom.Convert/Decoders/RuleYamlDecoder.cs unity/Xcom.Convert/ConvertJob.cs unity/Tests.Standalone/Convert/RuleYamlDecoderTests.cs unity/Tests.Standalone/Convert/ConvertJobTests.cs
git commit -m "feat(convert): HANDOB.PCK + BulletSprites.png conversion, handSprite/bulletSprite parsing"
```

---

## Task 3: Core `RuleItem.HandSprite`/`.BulletSprite` + `DataLoader` wiring

**Files:**
- Modify: `unity/Assets/Scripts/Core/Rules/RuleItem.cs`
- Modify: `unity/Assets/Scripts/Core/Rules/DataLoader.cs`
- Modify: `unity/Tests.Standalone/DataLoaderTests.cs`

**Interfaces:**
- Consumes: Task 2's `items.json` shape (now includes `HandSprite`/`BulletSprite` per entry).
- Produces: `RuleItem.HandSprite`/`.BulletSprite` (both `{ get; init; }` int, default 0), consumed by Task 5 (`UnitRenderer`'s held-item layer) and Task 7 (`ProjectileView`'s bullet-atlas frame lookup).

- [ ] **Step 1: Write the failing test**

In `unity/Tests.Standalone/DataLoaderTests.cs`, extend the existing `LoadItems_ParsesWeaponFieldsIncludingClipDerivedPowerAndDamageType` test: change the JSON literal to include the two new keys, and add two assertions. Replace:

```csharp
            string json = @"[{
                ""Id"": ""STR_RIFLE"", ""TwoHanded"": true, ""Power"": 30, ""DamageType"": 1,
                ""AccuracySnap"": 60, ""AccuracyAimed"": 110, ""AccuracyAuto"": 35,
                ""TuSnap"": 25, ""TuAimed"": 80, ""TuAuto"": 35
            }]";
```

with:

```csharp
            string json = @"[{
                ""Id"": ""STR_RIFLE"", ""TwoHanded"": true, ""Power"": 30, ""DamageType"": 1,
                ""AccuracySnap"": 60, ""AccuracyAimed"": 110, ""AccuracyAuto"": 35,
                ""TuSnap"": 25, ""TuAimed"": 80, ""TuAuto"": 35,
                ""HandSprite"": 0, ""BulletSprite"": 70
            }]";
```

and add, right after `Assert.Equal(80, rifle.TuAimed);`:

```csharp
            Assert.Equal(0, rifle.HandSprite);
            Assert.Equal(70, rifle.BulletSprite);
```

- [ ] **Step 2: Run test to verify it fails**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet test unity/Tests.Standalone/OpenXcom.Core.Tests.csproj --filter "FullyQualifiedName~LoadItems_ParsesWeaponFieldsIncludingClipDerivedPowerAndDamageType"`
Expected: FAIL — `RuleItem` has no `HandSprite`/`BulletSprite` members yet (compile error).

- [ ] **Step 3: Implement**

In `unity/Assets/Scripts/Core/Rules/RuleItem.cs`, add two properties after `LowerLimit`:

```csharp
        public int LowerLimit { get; init; } = 0;

        /// <summary>Base frame index into the hand-sprite atlas (HANDOB.PCK) for
        /// this weapon held at direction 0; add direction (0-7) to get the
        /// actual frame. Port of RuleItem::getHandSprite (RuleItem.h:390,
        /// RuleItem.cpp:1128-ish; used at UnitSprite.cpp:96-99).</summary>
        public int HandSprite { get; init; }

        /// <summary>Base frame index into the bullet-trail atlas
        /// (BulletSprites.png / the "Projectiles" surface set) for this
        /// weapon's shots, already multiplied by the 35-frames-per-projectile
        /// stride (RuleItem.cpp:352-353's loadSpriteOffset(..., "Projectiles",
        /// 35); see Xcom.Convert.Decoders.RuleYamlDecoder.LoadWeapon).</summary>
        public int BulletSprite { get; init; }
```

In `unity/Assets/Scripts/Core/Rules/DataLoader.cs`, add two properties to `RawItemEntry`:

```csharp
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
            public int HandSprite { get; set; }
            public int BulletSprite { get; set; }
        }
```

In `LoadItems`, extend the object initializer:

```csharp
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
                    HandSprite = r.HandSprite,
                    BulletSprite = r.BulletSprite,
                });
```

- [ ] **Step 4: Run test to verify it passes**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet test unity/Tests.Standalone/OpenXcom.Core.Tests.csproj`
Expected: full suite passes.

- [ ] **Step 5: Commit**

```bash
git add unity/Assets/Scripts/Core/Rules/RuleItem.cs unity/Assets/Scripts/Core/Rules/DataLoader.cs unity/Tests.Standalone/DataLoaderTests.cs
git commit -m "feat(core): RuleItem.HandSprite/BulletSprite fields"
```

---

## Task 4: `UnitSpriteFrames` — pure frame-index math (Core-testable, no UnityEngine dependency)

Same pattern as `IsoProjection.cs`: lives under `unity/Assets/Scripts/Unity/Rendering/` (so `UnitRenderer` can call it directly with no cross-namespace friction) but has zero `UnityEngine` references, so it's compiled directly into `Tests.Standalone` and fully unit-tested without an Editor.

**Files:**
- Create: `unity/Assets/Scripts/Unity/Rendering/UnitSpriteFrames.cs`
- Modify: `unity/Tests.Standalone/OpenXcom.Core.Tests.csproj` (one new `<Compile Include>` line)
- Create: `unity/Tests.Standalone/Unity/UnitSpriteFramesTests.cs`

**Interfaces:**
- Produces: `UnitSpriteFrames.BodyPartFrame/TorsoFrame/DeathFrame/HeldRightArmFrame/HeldLeftArmFrame/HeldItemFrame` (all pure `int`-returning static methods) plus the `LegsStandBase`/`LegsWalkBase`/etc. constants and `AimOffsetX`/`AimOffsetY` arrays — consumed by Task 5's `UnitRenderer`.

Ground truth, all from `src/Battlescape/UnitSprite.cpp`'s `drawRoutine0` (used by both soldiers and Sectoids, the `_drawingRoutine <= 10` branch, lines 286-560) unless noted:
- Standing: `partBase + direction` (lines 438-441). `legsStand=16, larmStand=0, rarmStand=8` (line 346), `maleTorso=32` (line 295).
- Walking: `partBase + 24*direction + walkPhase` (lines 418-420). `legsWalk=56, larmWalk=40, rarmWalk=48` (lines 347-349). `walkPhase` is always 0-7 (`src/Savegame/BattleUnit.cpp:1213-1215`, `getWalkingPhase()` returns `_walkPhase % 8`).
- Torso never changes frame for walk vs stand (always `maleTorso + direction`) — it only gets a ±1px Y offset while walking (`torsoHandsWeaponY`, line 350's `YoffWalk[8]`). **[SIMPLIFIED]**: this sub-pixel bob is intentionally not ported — cosmetic only, not worth a per-frame Y-offset plumbing path for a ±1px wobble.
- Death: `die=264` (line 294, "ufo:eu death frame"), frame = `die + fallingPhase`, `fallingPhase` 0..2 (`src/Mod/Armor.cpp:44`, default `_deathFrames=3`; `src/Savegame/BattleUnit.cpp:2084-2087` `getFallingPhase()`). No direction dependence (line 377: `selectUnit(coll, die, _unit->getFallingPhase())` — the "dir" parameter slot is reused to mean fallingPhase for this one call).
- Held item: frame = `handSprite + direction` (lines 96-99, `selectItem`).
- Two-handed (Rifle) arm/item handling (lines 446-500): not aiming → `leftArm=larm2H(240)+dir, rightArm=rarm2H(248)+dir`; aiming → `rightArm=rarmShoot(256)+dir` instead, AND the item's own direction flips to `(dir+2)%8` (line ~450), AND the item gets a pixel offset `offX[dir]/offY[dir]` (lines 353-354) — **only** in this aiming+two-handed case.
- One-handed (Plasma Pistol): `rightArm=rarm1H(232)+dir` always (line 498), no aiming-pose change, no item offset, item direction is always plain `dir` (the `+2` flip is two-handed-only).
- Neither held-item arm pose (`rarm1H`/`rarm2H`/`rarmShoot`/`larm2H`) cycles with `walkPhase` — they're all direction-only, matching the real engine (the arm holding a weapon doesn't visibly "walk," it just bobs ±1px, also cut per the torso note above).

- [ ] **Step 1: Write the failing tests**

Create `unity/Tests.Standalone/Unity/UnitSpriteFramesTests.cs`:

```csharp
using OpenXcom.Unity.Rendering;
using Xunit;

namespace OpenXcom.Core.Tests.Unity
{
    public class UnitSpriteFramesTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(4)]
        [InlineData(7)]
        public void BodyPartFrame_Standing_IsBasePlusDirection(int direction)
        {
            int frame = UnitSpriteFrames.BodyPartFrame(
                UnitSpriteFrames.LegsStandBase, UnitSpriteFrames.LegsWalkBase, direction, walkPhase: -1);
            Assert.Equal(UnitSpriteFrames.LegsStandBase + direction, frame);
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(3, 5)]
        [InlineData(7, 7)]
        public void BodyPartFrame_Walking_IsBasePlus24TimesDirectionPlusWalkPhase(int direction, int walkPhase)
        {
            int frame = UnitSpriteFrames.BodyPartFrame(
                UnitSpriteFrames.RightArmStandBase, UnitSpriteFrames.RightArmWalkBase, direction, walkPhase);
            Assert.Equal(UnitSpriteFrames.RightArmWalkBase + 24 * direction + walkPhase, frame);
        }

        [Fact]
        public void BodyPartFrame_WalkPhaseIsTakenMod8()
        {
            // getWalkingPhase() always returns _walkPhase % 8, so a caller
            // passing an out-of-range value (defensive) still lands correctly.
            int frame = UnitSpriteFrames.BodyPartFrame(0, 100, direction: 0, walkPhase: 9);
            Assert.Equal(100 + 1, frame); // 9 % 8 == 1
        }

        [Fact]
        public void TorsoFrame_IsAlwaysBasePlusDirection_RegardlessOfCallerIntent()
        {
            Assert.Equal(UnitSpriteFrames.TorsoBase + 5, UnitSpriteFrames.TorsoFrame(5));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void DeathFrame_IsDieBasePlusPhase_NoDirectionParameter(int phase)
        {
            Assert.Equal(UnitSpriteFrames.DieBase + phase, UnitSpriteFrames.DeathFrame(phase));
        }

        [Fact]
        public void HeldRightArmFrame_OneHanded_AlwaysUsesTheStaticOneHandedPose()
        {
            Assert.Equal(UnitSpriteFrames.RightArmOneHanded + 3,
                UnitSpriteFrames.HeldRightArmFrame(twoHanded: false, isAiming: false, direction: 3));
            // one-handed weapons never get a special aiming arm pose (UnitSprite.cpp:498)
            Assert.Equal(UnitSpriteFrames.RightArmOneHanded + 3,
                UnitSpriteFrames.HeldRightArmFrame(twoHanded: false, isAiming: true, direction: 3));
        }

        [Fact]
        public void HeldRightArmFrame_TwoHanded_SwitchesToTheAimPoseWhileAiming()
        {
            Assert.Equal(UnitSpriteFrames.RightArmTwoHandedCarry + 2,
                UnitSpriteFrames.HeldRightArmFrame(twoHanded: true, isAiming: false, direction: 2));
            Assert.Equal(UnitSpriteFrames.RightArmTwoHandedAim + 2,
                UnitSpriteFrames.HeldRightArmFrame(twoHanded: true, isAiming: true, direction: 2));
        }

        [Fact]
        public void HeldLeftArmFrame_IsAlwaysTheStaticTwoHandedCarryPose()
        {
            Assert.Equal(UnitSpriteFrames.LeftArmTwoHanded + 6, UnitSpriteFrames.HeldLeftArmFrame(6));
        }

        [Fact]
        public void HeldItemFrame_NotAiming_IsHandSpritePlusPlainDirection()
        {
            Assert.Equal(104 + 5, UnitSpriteFrames.HeldItemFrame(handSprite: 104, twoHanded: false, isAiming: false, direction: 5));
            Assert.Equal(0 + 5, UnitSpriteFrames.HeldItemFrame(handSprite: 0, twoHanded: true, isAiming: false, direction: 5));
        }

        [Fact]
        public void HeldItemFrame_AimingTwoHanded_FlipsDirectionByAQuarterTurn()
        {
            // (direction + 2) % 8 - UnitSprite.cpp's own aiming-pose quirk.
            Assert.Equal(0 + ((5 + 2) % 8), UnitSpriteFrames.HeldItemFrame(handSprite: 0, twoHanded: true, isAiming: true, direction: 5));
        }

        [Fact]
        public void HeldItemFrame_AimingOneHanded_DoesNotFlipDirection()
        {
            // the +2 flip is two-handed-only (UnitSprite.cpp:498 has no such branch for rarm1H).
            Assert.Equal(104 + 5, UnitSpriteFrames.HeldItemFrame(handSprite: 104, twoHanded: false, isAiming: true, direction: 5));
        }

        [Fact]
        public void AimOffsets_HaveEightEntriesMatchingUnitSpriteCppLiterals()
        {
            Assert.Equal(new[] { 8, 10, 7, 4, -9, -11, -7, -3 }, UnitSpriteFrames.AimOffsetX);
            Assert.Equal(new[] { -6, -3, 0, 2, 0, -4, -7, -9 }, UnitSpriteFrames.AimOffsetY);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet test unity/Tests.Standalone/OpenXcom.Core.Tests.csproj --filter "FullyQualifiedName~UnitSpriteFramesTests"`
Expected: compile error — `UnitSpriteFrames` doesn't exist yet, and the `.csproj` doesn't compile the new file yet either.

- [ ] **Step 3: Implement**

Create `unity/Assets/Scripts/Unity/Rendering/UnitSpriteFrames.cs`:

```csharp
namespace OpenXcom.Unity.Rendering
{
    /// <summary>
    /// Pure frame-index math for drawRoutine0's soldier/Sectoid body-part
    /// layout (src/Battlescape/UnitSprite.cpp:286-560, used by both XCOM
    /// soldiers and Sectoids - the "_drawingRoutine &lt;= 10" branch). No
    /// UnityEngine dependency, so it is unit-testable without the Editor
    /// (same pattern as IsoProjection.cs); UnitRenderer/BattleController call
    /// into this for the actual frame indices, then resolve/assign Sprites.
    /// </summary>
    public static class UnitSpriteFrames
    {
        // Body-part base frame indices within units-XCOM_0.png/units-SECTOID.png
        // (UnitSprite.cpp:290,295,346-349).
        public const int LegsStandBase = 16;
        public const int LegsWalkBase = 56;
        public const int LeftArmStandBase = 0;
        public const int LeftArmWalkBase = 40;
        public const int RightArmStandBase = 8;
        public const int RightArmWalkBase = 48;
        public const int TorsoBase = 32;

        // Held-item arm poses, within the same units-*.png atlas (UnitSprite.cpp:297-300,346).
        public const int RightArmOneHanded = 232;       // rarm1H
        public const int LeftArmTwoHanded = 240;        // larm2H
        public const int RightArmTwoHandedCarry = 248;  // rarm2H (not aiming)
        public const int RightArmTwoHandedAim = 256;    // rarmShoot (aiming)

        // Death sequence (UnitSprite.cpp:294,377; src/Mod/Armor.cpp:44 default deathFrames=3).
        public const int DieBase = 264;
        public const int DeathFrameCount = 3;

        /// <summary>Standing (walkPhase &lt; 0) or walking (walkPhase 0-7,
        /// taken mod 8) frame index for legs/left-arm/right-arm, matching
        /// UnitSprite.cpp:418-420 (walk: partBase + 24*direction + walkPhase)
        /// vs :438-441 (stand: partBase + direction). walkPhase mod 8 mirrors
        /// BattleUnit::getWalkingPhase (src/Savegame/BattleUnit.cpp:1213-1215).</summary>
        public static int BodyPartFrame(int partStandBase, int partWalkBase, int direction, int walkPhase) =>
            walkPhase < 0
                ? partStandBase + direction
                : partWalkBase + 24 * direction + (walkPhase % 8);

        /// <summary>Torso frame never changes for walk vs stand (UnitSprite.cpp
        /// only adjusts a +-1px Y offset while walking - torsoHandsWeaponY,
        /// intentionally not ported, see this plan's Task 4 notes); it is
        /// always partBase + direction.</summary>
        public static int TorsoFrame(int direction) => TorsoBase + direction;

        /// <summary>Death frame (UnitSprite.cpp:377, selectUnit(coll, die,
        /// fallingPhase) - no direction dependence). phase is 0..DeathFrameCount-1.</summary>
        public static int DeathFrame(int phase) => DieBase + phase;

        /// <summary>Held-item right-arm frame: one-handed weapons always show
        /// the static one-handed carry pose (UnitSprite.cpp:498, no aiming
        /// variant exists for one-handed items); two-handed weapons show the
        /// two-handed carry pose unless isAiming, in which case they show the
        /// two-handed aim pose (UnitSprite.cpp:483-490). Neither pose cycles
        /// with walkPhase - the real engine's item-holding arm frame is
        /// direction-only regardless of walking/standing.</summary>
        public static int HeldRightArmFrame(bool twoHanded, bool isAiming, int direction) =>
            !twoHanded ? RightArmOneHanded + direction
            : isAiming ? RightArmTwoHandedAim + direction
            : RightArmTwoHandedCarry + direction;

        /// <summary>Held-item left-arm frame: only meaningful for two-handed
        /// weapons (UnitSprite.cpp:483, always larm2H+direction, static - like
        /// the right arm, no walk cycle). One-handed weapons leave the left
        /// arm on its normal stand/walk cycle - callers should use
        /// BodyPartFrame(LeftArmStandBase, LeftArmWalkBase, ...) instead of
        /// this method in that case.</summary>
        public static int HeldLeftArmFrame(int direction) => LeftArmTwoHanded + direction;

        /// <summary>Held-item sprite frame within the hand-sprite atlas
        /// (handSprite + direction, UnitSprite.cpp:96-99 selectItem). For a
        /// two-handed weapon while aiming, the item's own direction flips by
        /// a quarter-turn relative to the body (UnitSprite.cpp:~450,
        /// `dir = (unitDir+2)%8`) - a real, cited quirk of the original data,
        /// not a rewrite invention.</summary>
        public static int HeldItemFrame(int handSprite, bool twoHanded, bool isAiming, int direction)
        {
            int itemDir = (twoHanded && isAiming) ? (direction + 2) % 8 : direction;
            return handSprite + itemDir;
        }

        /// <summary>Item pixel-offset while aiming a two-handed weapon only
        /// (UnitSprite.cpp:353-354,450-451), indexed by direction. In original
        /// SDL pixel units - divide by TileRenderer.PixelsPerUnit before
        /// applying as a Unity local-position offset.</summary>
        public static readonly int[] AimOffsetX = { 8, 10, 7, 4, -9, -11, -7, -3 };
        public static readonly int[] AimOffsetY = { -6, -3, 0, 2, 0, -4, -7, -9 };
    }
}
```

In `unity/Tests.Standalone/OpenXcom.Core.Tests.csproj`, add one line right after the `PathPreview.cs` include:

```xml
    <!-- Pure-math unit sprite frame-index helper: no UnityEngine dependency, testable here. -->
    <Compile Include="../Assets/Scripts/Unity/Rendering/UnitSpriteFrames.cs" />
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet test unity/Tests.Standalone/OpenXcom.Core.Tests.csproj`
Expected: full suite passes, all `UnitSpriteFramesTests` green.

- [ ] **Step 5: Commit**

```bash
git add unity/Assets/Scripts/Unity/Rendering/UnitSpriteFrames.cs unity/Tests.Standalone/OpenXcom.Core.Tests.csproj unity/Tests.Standalone/Unity/UnitSpriteFramesTests.cs
git commit -m "feat(unity): UnitSpriteFrames - pure frame-index math for drawRoutine0's layout"
```

---

## Task 5: `UnitRenderer` — frame-driven body + held-item rendering

Replaces the one-shot static `Setup(...)` with atlas-driven `SetFrame`/`SetDeathFrame`, and adds a 5th (held-item) `SpriteRenderer` layer. This is a MonoBehaviour file — not compiled by `dotnet test` — so verification is a live-Editor compile check, not a `dotnet test` run; the frame-index arithmetic itself was already proven correct in Task 4.

**Files:**
- Modify: `unity/Assets/Scripts/Unity/Rendering/UnitRenderer.cs` (full rewrite of `Setup`, new `SetFrame`/`SetDeathFrame`)
- Modify: `unity/Assets/Scripts/Unity/Rendering/IsoProjection.cs` (`UnitPartRank` gains `Item`, `UnitSortingOrder`'s multiplier changes from `*4` to `*5`)
- Modify: `unity/Tests.Standalone/Unity/IsoProjectionTests.cs` (update any `UnitSortingOrder` assertions for the new multiplier — see Step 1)

**Interfaces:**
- Consumes: `UnitSpriteFrames.*` (Task 4). `RuleItem` (Task 3's `HandSprite`/`BulletSprite`, plus existing `TwoHanded`).
- Produces: `UnitRenderer.Setup(int x, int y, int z, int mapWidth, int mapLength, (Texture2D, List<Rect>) bodyAtlas, (Texture2D, List<Rect>)? itemAtlas, RuleItem heldWeapon)`, `UnitRenderer.SetFrame(int direction, int walkPhase, bool isAiming)`, `UnitRenderer.SetDeathFrame(int phase)` — all consumed by Task 6 (`BattleController`) and Task 8 (`BattlescapeBootstrap`).

Draw-order note (**[SIMPLIFIED]**): the real engine's item-vs-body blit order varies by facing direction (`UnitSprite.cpp:607-640` has a different `case 0..7` ordering per direction). This plan always draws the held item frontmost (highest `sortingOrder`) regardless of facing — correct for the direction-4 (south) pose this project already special-cased at spawn, an acceptable simplification for the other 7 given it's a minor compositing nuance for two starter weapons, not a gameplay-readability issue.

- [ ] **Step 1: Check/update the existing `IsoProjectionTests.cs` `UnitSortingOrder` coverage**

Read `unity/Tests.Standalone/Unity/IsoProjectionTests.cs` in full first. If it has any test asserting a specific `UnitSortingOrder` numeric value (e.g. checking the `*4` sub-order multiplier or the `unitBand` computation), update the expected value to use `*5` instead of `*4` to match Step 3's change below — mirror whatever arithmetic that existing test already does, just with the multiplier updated. If no such test exists, add one:

```csharp
        [Fact]
        public void UnitSortingOrder_FiveDistinctPartRanksNeverCollideWithinOneUnit()
        {
            var ranks = new[]
            {
                IsoProjection.UnitPartRank.Legs, IsoProjection.UnitPartRank.RightArm,
                IsoProjection.UnitPartRank.Torso, IsoProjection.UnitPartRank.LeftArm,
                IsoProjection.UnitPartRank.Item,
            };
            var orders = new System.Collections.Generic.HashSet<int>();
            foreach (var rank in ranks)
                orders.Add(IsoProjection.UnitSortingOrder(3, 3, 0, mapWidth: 10, mapLength: 10, rank));

            Assert.Equal(5, orders.Count); // all 5 parts get distinct sort orders
        }
```

This new/updated test will fail to compile until Step 3 adds `UnitPartRank.Item` — that's expected for this step.

- [ ] **Step 2: Run test to verify it fails**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet test unity/Tests.Standalone/OpenXcom.Core.Tests.csproj --filter "FullyQualifiedName~IsoProjectionTests"`
Expected: compile error (`UnitPartRank.Item` doesn't exist yet) or an assertion failure if you updated an existing numeric assertion instead.

- [ ] **Step 3: Implement `IsoProjection.cs` changes**

In `unity/Assets/Scripts/Unity/Rendering/IsoProjection.cs`, change the `UnitPartRank` enum:

```csharp
        public enum UnitPartRank
        {
            Legs = 0,
            RightArm = 1,
            Torso = 2,
            LeftArm = 3,
            Item = 4,
        }
```

In `UnitSortingOrder`, change the sub-order multiplier from `4` to `5` (and update the doc comment's "tileIndex*4" references to "tileIndex*5" to match):

```csharp
        public static int UnitSortingOrder(int x, int y, int z, int mapWidth, int mapLength, UnitPartRank part)
        {
            long tileIndex = ((long)z * mapLength + y) * mapWidth + x;
            long unitBand = (long)mapWidth * mapLength * 5;
            return (int)(unitBand + tileIndex * 5 + (int)part);
        }
```

(Leave `RaycastDepth`/`UnitRaycastDepth` untouched — they already reference `UnitPartRank.Object`/`.Torso`, unaffected by adding a 5th rank.)

- [ ] **Step 4: Run test to verify it passes**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet test unity/Tests.Standalone/OpenXcom.Core.Tests.csproj --filter "FullyQualifiedName~IsoProjectionTests"`
Expected: PASS.

- [ ] **Step 5: Rewrite `UnitRenderer.cs`**

Replace the full contents of `unity/Assets/Scripts/Unity/Rendering/UnitRenderer.cs`:

```csharp
using System.Collections.Generic;
using OpenXcom.Core.Rules;
using UnityEngine;

namespace OpenXcom.Unity.Rendering
{
    /// <summary>
    /// Draws one unit as 5 stacked SpriteRenderer layers (legs, right arm,
    /// torso, left arm, held item), reproducing UnitSprite.cpp drawRoutine0's
    /// per-direction/walk-phase/aiming frame selection (see
    /// UnitSpriteFrames.cs for the exact port citations). Positioned via the
    /// same IsoProjection math TileRenderer uses. Pure display: resolves
    /// frame indices to Sprites and assigns them, makes no gameplay
    /// decisions - BattleController decides direction/walkPhase/isAiming
    /// from BattleState's events and calls SetFrame/SetDeathFrame here.
    /// </summary>
    public sealed class UnitRenderer : MonoBehaviour
    {
        private SpriteRenderer _legs;
        private SpriteRenderer _rightArm;
        private SpriteRenderer _torso;
        private SpriteRenderer _leftArm;
        private SpriteRenderer _item;
        private BoxCollider _collider;

        private (Texture2D texture, List<Rect> frameRects) _bodyAtlas;
        private (Texture2D texture, List<Rect> frameRects)? _itemAtlas;
        private RuleItem _heldWeapon;

        private void Awake()
        {
            _legs = CreateChild("Legs");
            _rightArm = CreateChild("RightArm");
            _torso = CreateChild("Torso");
            _leftArm = CreateChild("LeftArm");
            _item = CreateChild("Item");

            _collider = gameObject.AddComponent<BoxCollider>();
            _collider.size = new Vector3(0.6f, 1f, 0.1f);
            // Same bottom-anchor-vs-centered-collider mismatch as TileRenderer:
            // the unit sprite (pivot 0.5,0) only extends upward from this
            // transform's local origin, so the collider must be shifted up by
            // half its height to actually overlap the visible body, not just the
            // ground beneath the unit's feet.
            _collider.center = new Vector3(0f, 0.5f, 0f);
        }

        private SpriteRenderer CreateChild(string childName)
        {
            var go = new GameObject(childName);
            go.transform.SetParent(transform, worldPositionStays: false);
            return go.AddComponent<SpriteRenderer>();
        }

        /// <summary>Positions/sorts this unit and stores the atlases/weapon
        /// used by every later SetFrame call. Does not itself pick a frame -
        /// call SetFrame right after Setup to render the initial pose.</summary>
        public void Setup(int x, int y, int z, int mapWidth, int mapLength,
            (Texture2D texture, List<Rect> frameRects) bodyAtlas,
            (Texture2D texture, List<Rect> frameRects)? itemAtlas,
            RuleItem heldWeapon)
        {
            _bodyAtlas = bodyAtlas;
            _itemAtlas = itemAtlas;
            _heldWeapon = heldWeapon;

            var (worldX, worldY) = IsoProjection.WorldPosition(x, y, z, TileRenderer.PixelsPerUnit);
            float depth = IsoProjection.UnitRaycastDepth(x, y, z, mapWidth, mapLength);
            transform.localPosition = new Vector3(worldX, worldY, depth);

            _legs.sortingOrder = IsoProjection.UnitSortingOrder(x, y, z, mapWidth, mapLength, IsoProjection.UnitPartRank.Legs);
            _rightArm.sortingOrder = IsoProjection.UnitSortingOrder(x, y, z, mapWidth, mapLength, IsoProjection.UnitPartRank.RightArm);
            _torso.sortingOrder = IsoProjection.UnitSortingOrder(x, y, z, mapWidth, mapLength, IsoProjection.UnitPartRank.Torso);
            _leftArm.sortingOrder = IsoProjection.UnitSortingOrder(x, y, z, mapWidth, mapLength, IsoProjection.UnitPartRank.LeftArm);
            _item.sortingOrder = IsoProjection.UnitSortingOrder(x, y, z, mapWidth, mapLength, IsoProjection.UnitPartRank.Item);
        }

        /// <summary>Renders the standing (walkPhase &lt; 0) or walking
        /// (walkPhase 0-7) pose facing direction (0-7). isAiming only affects
        /// a two-handed held weapon's arm/item pose (UnitSpriteFrames docs);
        /// pass false outside of the brief firing-pose window.</summary>
        public void SetFrame(int direction, int walkPhase, bool isAiming)
        {
            _legs.enabled = true;
            _rightArm.enabled = true;
            _torso.enabled = true;
            _leftArm.enabled = true;

            _legs.sprite = FrameSprite(_bodyAtlas,
                UnitSpriteFrames.BodyPartFrame(UnitSpriteFrames.LegsStandBase, UnitSpriteFrames.LegsWalkBase, direction, walkPhase));
            _torso.sprite = FrameSprite(_bodyAtlas, UnitSpriteFrames.TorsoFrame(direction));

            if (_heldWeapon != null)
            {
                bool twoHanded = _heldWeapon.TwoHanded;
                _rightArm.sprite = FrameSprite(_bodyAtlas,
                    UnitSpriteFrames.HeldRightArmFrame(twoHanded, isAiming, direction));
                _leftArm.sprite = twoHanded
                    ? FrameSprite(_bodyAtlas, UnitSpriteFrames.HeldLeftArmFrame(direction))
                    : FrameSprite(_bodyAtlas,
                        UnitSpriteFrames.BodyPartFrame(UnitSpriteFrames.LeftArmStandBase, UnitSpriteFrames.LeftArmWalkBase, direction, walkPhase));

                if (_itemAtlas.HasValue)
                {
                    _item.enabled = true;
                    _item.sprite = FrameSprite(_itemAtlas.Value,
                        UnitSpriteFrames.HeldItemFrame(_heldWeapon.HandSprite, twoHanded, isAiming, direction));
                    bool offsetItem = twoHanded && isAiming;
                    float offX = offsetItem ? UnitSpriteFrames.AimOffsetX[direction] / TileRenderer.PixelsPerUnit : 0f;
                    float offY = offsetItem ? -UnitSpriteFrames.AimOffsetY[direction] / TileRenderer.PixelsPerUnit : 0f;
                    _item.transform.localPosition = new Vector3(offX, offY, 0f);
                }
            }
            else
            {
                _rightArm.sprite = FrameSprite(_bodyAtlas,
                    UnitSpriteFrames.BodyPartFrame(UnitSpriteFrames.RightArmStandBase, UnitSpriteFrames.RightArmWalkBase, direction, walkPhase));
                _leftArm.sprite = FrameSprite(_bodyAtlas,
                    UnitSpriteFrames.BodyPartFrame(UnitSpriteFrames.LeftArmStandBase, UnitSpriteFrames.LeftArmWalkBase, direction, walkPhase));
                _item.enabled = false;
            }
        }

        /// <summary>Collapsing/death pose - replaces the entire 4-part
        /// composite with one frame (UnitSprite.cpp:374-378: BODYPART_COLLAPSING
        /// is drawn as a single sprite, not the usual per-part composite), so
        /// this disables every other layer and repurposes the legs renderer
        /// as the sole visible sprite. phase is 0..UnitSpriteFrames.DeathFrameCount-1.</summary>
        public void SetDeathFrame(int phase)
        {
            _rightArm.enabled = false;
            _torso.enabled = false;
            _leftArm.enabled = false;
            _item.enabled = false;
            _legs.enabled = true;
            _legs.sprite = FrameSprite(_bodyAtlas, UnitSpriteFrames.DeathFrame(phase));
        }

        private static Sprite FrameSprite((Texture2D texture, List<Rect> frameRects) atlas, int frameIndex) =>
            Sprite.Create(atlas.texture, atlas.frameRects[frameIndex], new Vector2(0.5f, 0f), TileRenderer.PixelsPerUnit);
    }
}
```

Note on the item offset's Y sign: `UnitSpriteFrames.AimOffsetY` is in SDL pixel space (positive = further down the screen); Unity world Y is the negated convention already established by `IsoProjection.WorldPosition`'s own doc comment, hence the `-` in `-UnitSpriteFrames.AimOffsetY[direction]` above.

- [ ] **Step 6: Verify compiles**

Since `UnitRenderer.cs` is not covered by `dotnet test`, verify via a connected Unity Editor: call `mcp__coplay-mcp__list_unity_project_roots`; if it returns this project, call `mcp__coplay-mcp__check_compile_errors` and confirm no errors (note: `BattlescapeBootstrap.cs` will now fail to compile too, since it still calls the *old* `UnitRenderer.Setup` 8-arg signature — that's expected and gets fixed in Task 8; for this task, confirm `UnitRenderer.cs`/`IsoProjection.cs` themselves introduce no *new* errors beyond that already-expected downstream breakage, i.e. the errors you see should be scoped to `BattlescapeBootstrap.cs`'s stale call site, not to anything inside `UnitRenderer.cs` itself). If no Editor is connected, state that explicitly in the task report instead of claiming this was verified.

- [ ] **Step 7: Commit**

```bash
git add unity/Assets/Scripts/Unity/Rendering/UnitRenderer.cs unity/Assets/Scripts/Unity/Rendering/IsoProjection.cs unity/Tests.Standalone/Unity/IsoProjectionTests.cs
git commit -m "feat(unity): UnitRenderer - frame-driven body + held-item rendering"
```

---

## Task 6: `BattleController` — walk-phase stepping, firing-pose hold, death sequence

Also a MonoBehaviour file, not covered by `dotnet test` — verify live per Task 5's Step 6 pattern.

**Files:**
- Modify: `unity/Assets/Scripts/Unity/BattleController.cs`

**Interfaces:**
- Consumes: `UnitRenderer.SetFrame`/`.SetDeathFrame` (Task 5), `UnitMovedEvent.From` (Task 1), `UnitSpriteFrames.DeathFrameCount` (Task 4).
- Produces: `BattleController.StartFiringPose(Transform, int direction)` (private, but Task 7 needs the *pattern*, not this method itself — Task 7 adds its own `ProjectileFiredEvent` handling in `DrainAndAnimate` directly).

Design note on why walk-phase and fire/death use two different mechanisms (this is a deliberate scope decision, not an oversight — document it verbatim in the class's doc comment so a reviewer sees the reasoning): walk-phase must stay locked to actual tile arrival (a gameplay-visual sync requirement — the frame must change exactly when the unit visually reaches each tile, not on a fixed timer that could drift out of sync with `tilesPerSecond`), so it's driven by the existing per-tile `Queue.Dequeue()` point in `AdvanceAnimations`. Firing-pose-hold and the death sequence are both genuinely time-boxed ("show this pose/sequence for N seconds then revert/finish") with no positional trigger, so they share one small timed-sequence mechanism.

- [ ] **Step 1: Modify `UnitAnimation` and wire walk-phase stepping**

In `unity/Assets/Scripts/Unity/BattleController.cs`, replace the `UnitAnimation` class:

```csharp
        private sealed class UnitAnimation
        {
            public Transform Transform;
            public UnitRenderer Renderer;
            public Queue<Position> Queue;
            public Vector3 Target;
            public Position Previous;
            public int Direction;
            public int WalkPhase;
        }
```

In `DrainAndAnimate`, replace the `UnitMovedEvent` branch:

```csharp
                if (evt is UnitMovedEvent moved && _unitTransforms.TryGetValue(moved.Unit, out var t))
                {
                    t.TryGetComponent<UnitRenderer>(out var renderer);
                    _activeAnimations.Add(new UnitAnimation
                    {
                        Transform = t,
                        Renderer = renderer,
                        Queue = new Queue<Position>(moved.Path),
                        Target = t.localPosition,
                        Previous = moved.From,
                        Direction = moved.Unit.Direction,
                        WalkPhase = -1, // first dequeue below increments to 0
                    });
                }
```

In `AdvanceAnimations`, replace the whole method body's dequeue/completion branches:

```csharp
        private void AdvanceAnimations()
        {
            for (int i = _activeAnimations.Count - 1; i >= 0; i--)
            {
                var anim = _activeAnimations[i];

                if (anim.Queue.Count == 0 && Vector3.Distance(anim.Transform.localPosition, anim.Target) < 0.01f)
                {
                    anim.Renderer?.SetFrame(anim.Direction, walkPhase: -1, isAiming: false);
                    _activeAnimations.RemoveAt(i);
                    continue;
                }

                if (Vector3.Distance(anim.Transform.localPosition, anim.Target) < 0.01f)
                {
                    var next = anim.Queue.Dequeue();
                    var (worldX, worldY) = IsoProjection.WorldPosition(next.X, next.Y, next.Z, TileRenderer.PixelsPerUnit);
                    // Z must track UnitRaycastDepth, not a flat 0 - every tile's
                    // MeshCollider/BoxCollider sits at a small negative
                    // (closer-to-camera) Z via IsoProjection.RaycastDepth, and a
                    // moving unit needs to stay closer than the tile beneath it
                    // (its own UnitRaycastDepth) or the raycast that resolves
                    // clicks/hover starts hitting the tile instead of the unit
                    // standing on it once it settles at Z=0. This made any unit
                    // that had ever moved - including every Hostile unit after
                    // its first AI turn, regardless of what the player did -
                    // permanently unable to be right-click-targeted.
                    float depth = IsoProjection.UnitRaycastDepth(next.X, next.Y, next.Z, _state.Grid.Width, _state.Grid.Length);
                    anim.Target = new Vector3(worldX, worldY, depth);

                    anim.Direction = Directions.IndexOf(next - anim.Previous);
                    anim.WalkPhase = (anim.WalkPhase + 1) % 8;
                    anim.Previous = next;
                    anim.Renderer?.SetFrame(anim.Direction, anim.WalkPhase, isAiming: false);
                }

                anim.Transform.localPosition = Vector3.MoveTowards(
                    anim.Transform.localPosition, anim.Target, tilesPerSecond * Time.deltaTime);
            }
        }
```

- [ ] **Step 2: Add the shared timed-sequence mechanism, firing pose, and death sequence**

Add these constants near the top of the class (with `tilesPerSecond`):

```csharp
        private const float FiringPoseSeconds = 0.3f;
        private const float DeathSequenceSeconds = 0.6f;
```

Add this nested class right after `UnitAnimation`:

```csharp
        /// <summary>Shared timer for the two genuinely duration-based
        /// animations (firing-pose hold, death sequence) - unlike walk-phase
        /// stepping (position-driven, see the class doc comment), both of
        /// these are "hold/step for N seconds then settle," so they share one
        /// mechanism instead of two near-identical ad hoc timers.</summary>
        private sealed class TimedSequence
        {
            public UnitRenderer Renderer;
            public GameObject GameObjectToDeactivate; // null for firing-pose (nothing to deactivate)
            public BattleUnit Unit; // set for death sequences, to remove from _unitTransforms on completion
            public float Elapsed;
            public float Duration;
            public int FrameCount;
            public bool IsDeath;
            public int Direction; // firing-pose revert direction
        }

        private readonly List<TimedSequence> _timedSequences = new();

        private void StartFiringPose(Transform shooterTransform, int direction)
        {
            if (!shooterTransform.TryGetComponent<UnitRenderer>(out var renderer))
                return;

            renderer.SetFrame(direction, walkPhase: -1, isAiming: true);
            _timedSequences.Add(new TimedSequence
            {
                Renderer = renderer, Elapsed = 0f, Duration = FiringPoseSeconds, FrameCount = 1, IsDeath = false, Direction = direction,
            });
        }

        private void StartDeathSequence(BattleUnit unit, Transform deadTransform)
        {
            if (!deadTransform.TryGetComponent<UnitRenderer>(out var renderer))
            {
                deadTransform.gameObject.SetActive(false);
                _unitTransforms.Remove(unit);
                if (_selected == unit)
                    _selected = null;
                return;
            }

            renderer.SetDeathFrame(0);
            _timedSequences.Add(new TimedSequence
            {
                Renderer = renderer, GameObjectToDeactivate = deadTransform.gameObject, Unit = unit,
                Elapsed = 0f, Duration = DeathSequenceSeconds, FrameCount = OpenXcom.Unity.Rendering.UnitSpriteFrames.DeathFrameCount, IsDeath = true,
            });
        }

        private void AdvanceTimedSequences()
        {
            for (int i = _timedSequences.Count - 1; i >= 0; i--)
            {
                var seq = _timedSequences[i];
                seq.Elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(seq.Elapsed / seq.Duration);

                if (seq.IsDeath)
                {
                    int phase = Mathf.Min(seq.FrameCount - 1, (int)(t * seq.FrameCount));
                    seq.Renderer.SetDeathFrame(phase);
                }

                if (t < 1f)
                    continue;

                if (seq.IsDeath)
                {
                    seq.GameObjectToDeactivate.SetActive(false);
                    _unitTransforms.Remove(seq.Unit);
                    if (_selected == seq.Unit)
                        _selected = null;
                }
                else
                {
                    seq.Renderer.SetFrame(seq.Direction, walkPhase: -1, isAiming: false);
                }
                _timedSequences.RemoveAt(i);
            }
        }
```

In `Update()`, add `AdvanceTimedSequences();` right after `UpdateHover();` (so it ticks every frame regardless of the walk-animation input gate below it):

```csharp
        private void Update()
        {
            UpdateHover();
            AdvanceTimedSequences();

            if (_activeAnimations.Count > 0)
            {
                AdvanceAnimations();
                return; // don't accept new input while any unit is mid-animation
            }
            ...
```

In `DrainAndAnimate`, replace the `UnitDiedEvent` branch:

```csharp
                else if (evt is UnitDiedEvent died && _unitTransforms.TryGetValue(died.Unit, out var deadTransform))
                {
                    StartDeathSequence(died.Unit, deadTransform);
                }
```

(Task 7 will further modify the `ProjectileFiredEvent` branch to also call `StartFiringPose` — leave that branch's existing `Debug.Log`-only body untouched in this task; Task 7 adds to it, it doesn't belong to this task's scope.)

- [ ] **Step 3: Verify compiles**

Same live-Editor verification as Task 5 Step 6: `check_compile_errors` via Coplay MCP if an Editor is connected; state explicitly if not.

- [ ] **Step 4: Commit**

```bash
git add unity/Assets/Scripts/Unity/BattleController.cs
git commit -m "feat(unity): BattleController - walk-phase stepping, firing-pose hold, death sequence"
```

---

## Task 7: `ProjectileView` — bullet-trail visual along Phase 8's traced path

**Files:**
- Modify: `unity/Assets/Scripts/Core/Battle/BattleEvent.cs` (`ProjectileFiredEvent` gains `Weapon`)
- Modify: `unity/Assets/Scripts/Core/Battle/BattleState.cs` (`TryFire` passes `weapon.Rules`)
- Modify: `unity/Tests.Standalone/BattleStateFireTests.cs` (one new assertion on an existing test, or a small new test)
- Modify: `unity/Assets/Scripts/Unity/Rendering/IsoProjection.cs` (new `VoxelWorldPosition` overload)
- Modify: `unity/Tests.Standalone/Unity/IsoProjectionTests.cs` (new test for `VoxelWorldPosition`)
- Create: `unity/Assets/Scripts/Unity/Rendering/ProjectileView.cs`
- Modify: `unity/Assets/Scripts/Unity/BattleController.cs` (forward the event)

**Interfaces:**
- Consumes: `ProjectileFiredEvent.Trajectory` (Phase 8, `IReadOnlyList<Position>` in voxel space, currently `[originVoxel, finalTracedVoxel]`), `RuleItem.BulletSprite` (Task 3).
- Produces: `ProjectileView.Setup((Texture2D, List<Rect>) bulletAtlas)`, `ProjectileView.Play(Position originVoxel, Position hitVoxel, int bulletSpriteBase)` — consumed by Task 8.

**[SIMPLIFIED]**: the real engine draws a 35-frame fading streak sampled from a dense per-voxel-step trajectory (`src/Battlescape/Projectile.cpp:557-560`'s `getParticle(i) = _bulletSprite + i`, `Map.h:67`'s `BULLET_SPRITES = 35`, `Map.cpp:1121-1165`'s draw loop). Phase 8's `Trajectory` only records `[origin, hit]` (no intermediate voxel steps are retained), so faithfully reproducing the 35-frame trailing-streak sampling isn't possible without changing Phase 8's data shape — out of scope here. Instead, this task moves **one** dot sprite (the bullet's own base frame, `RuleItem.BulletSprite + 0`) smoothly from origin to hit voxel over a fixed short duration. State this plainly; it is not a rewrite bug, it is a recorded scope cut.

- [ ] **Step 1: Write the failing tests**

In `unity/Tests.Standalone/BattleStateFireTests.cs`, add this test near the other `TryFire_...` tests:

```csharp
        [Fact]
        public void TryFire_ProjectileFiredEventCarriesTheWeaponUsed()
        {
            var (grid, state) = MakeOpenBattle(width: 25);
            state.LoftData = FullTileLoftData();
            var attacker = MakeGuaranteedHitAttacker(new Position(0, 0, 0));
            var defender = MakeFullTileDefender(new Position(8, 0, 0), health: 100);
            grid.At(0, 0, 0).Occupant = attacker;
            grid.At(8, 0, 0).Occupant = defender;
            for (int x = 0; x <= 8; x++) grid.At(x, 0, 0).Floor = new MapDataTile { StopLOS = false };
            state.Units.Add(attacker);
            state.Units.Add(defender);

            state.TryFire(attacker, attacker.RightHand, BattleActionType.AimedShot, defender);

            var fired = state.DequeueEvents().OfType<ProjectileFiredEvent>().Single();
            Assert.Same(attacker.RightHand.Rules, fired.Weapon);
        }
```

(`using System.Linq;` is already present in this file per Phase 8.)

In `unity/Tests.Standalone/Unity/IsoProjectionTests.cs`, add:

```csharp
        [Fact]
        public void VoxelWorldPosition_OneTileOriginVoxel_MatchesWorldPositionAtTheEquivalentTile()
        {
            // Voxel (16,16,24) is exactly tile (1,1,1)'s origin corner (16 voxel
            // units/tile in X/Y, 24 in Z) - so VoxelWorldPosition at that voxel
            // must equal WorldPosition at tile (1,1,1).
            var (tileX, tileY) = IsoProjection.WorldPosition(1, 1, 1, pixelsPerUnit: 32f);
            var (voxelX, voxelY) = IsoProjection.VoxelWorldPosition(16f, 16f, 24f, pixelsPerUnit: 32f);
            Assert.Equal(tileX, voxelX, 3);
            Assert.Equal(tileY, voxelY, 3);
        }

        [Fact]
        public void VoxelWorldPosition_HalfTileVoxel_IsHalfwayBetweenTileOrigins()
        {
            var (x0, y0) = IsoProjection.VoxelWorldPosition(0f, 0f, 0f, pixelsPerUnit: 32f);
            var (x1, y1) = IsoProjection.WorldPosition(1, 0, 0, pixelsPerUnit: 32f);
            var (xHalf, yHalf) = IsoProjection.VoxelWorldPosition(8f, 0f, 0f, pixelsPerUnit: 32f);
            Assert.Equal((x0 + x1) / 2f, xHalf, 3);
            Assert.Equal((y0 + y1) / 2f, yHalf, 3);
        }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet test unity/Tests.Standalone/OpenXcom.Core.Tests.csproj --filter "FullyQualifiedName~TryFire_ProjectileFiredEventCarriesTheWeaponUsed|FullyQualifiedName~VoxelWorldPosition"`
Expected: compile errors (`ProjectileFiredEvent.Weapon` and `IsoProjection.VoxelWorldPosition` don't exist yet).

- [ ] **Step 3: Implement**

In `unity/Assets/Scripts/Core/Battle/BattleEvent.cs`, replace `ProjectileFiredEvent`:

```csharp
    public sealed class ProjectileFiredEvent : BattleEvent
    {
        public BattleUnit Attacker { get; }
        public BattleUnit Defender { get; }
        public bool Hit { get; }
        public IReadOnlyList<Position> Trajectory { get; }
        public RuleItem Weapon { get; }

        public ProjectileFiredEvent(BattleUnit attacker, BattleUnit defender, bool hit, IReadOnlyList<Position> trajectory, RuleItem weapon)
        {
            Attacker = attacker;
            Defender = defender;
            Hit = hit;
            Trajectory = trajectory;
            Weapon = weapon;
        }
    }
```

(`RuleItem` is in `OpenXcom.Core.Rules`, already `using`d at the top of `BattleEvent.cs`.)

In `unity/Assets/Scripts/Core/Battle/BattleState.cs`, in `TryFire`, change:

```csharp
            Enqueue(new ProjectileFiredEvent(attacker, defender, shot.Hit, trajectory));
```

to:

```csharp
            Enqueue(new ProjectileFiredEvent(attacker, defender, shot.Hit, trajectory, weapon.Rules));
```

In `unity/Assets/Scripts/Unity/Rendering/IsoProjection.cs`, add this method right after `WorldPosition`:

```csharp
        /// <summary>
        /// Voxel-precision equivalent of WorldPosition, for animating along a
        /// traced voxel path (Phase 8's ProjectileFiredEvent.Trajectory)
        /// rather than snapping to tile centers. Voxel scale is 16 units/tile
        /// in X/Y, 24 in Z (Phase 8 design spec §3) - divides down to
        /// fractional tile coordinates, then applies the same MapToScreen
        /// formula at float precision (MapToScreen itself stays int/tile-only,
        /// since every other caller - tiles, units, cursor, path arrows -
        /// only ever needs tile-precision placement).
        /// </summary>
        public static (float WorldX, float WorldY) VoxelWorldPosition(float voxelX, float voxelY, float voxelZ, float pixelsPerUnit)
        {
            float tileX = voxelX / 16f;
            float tileY = voxelY / 16f;
            float tileZ = voxelZ / 24f;
            float screenX = (tileX - tileY) * (SpriteWidth / 2f);
            float screenY = (tileX + tileY) * (SpriteWidth / 4f) - tileZ * ((SpriteHeight + SpriteWidth / 4f) / 2f);
            return (screenX / pixelsPerUnit, -screenY / pixelsPerUnit);
        }
```

Create `unity/Assets/Scripts/Unity/Rendering/ProjectileView.cs`:

```csharp
using System.Collections.Generic;
using OpenXcom.Core.Common;
using UnityEngine;

namespace OpenXcom.Unity.Rendering
{
    /// <summary>
    /// Animates a bullet-trail dot sprite along a fired shot's traced voxel
    /// path (Phase 8's TileEngine.CalculateLine output, carried by
    /// ProjectileFiredEvent.Trajectory). [SIMPLIFIED] the real engine draws a
    /// 35-frame fading streak sampled from a dense per-voxel-step trajectory
    /// (src/Battlescape/Projectile.cpp:557-560, Map.cpp:1121-1165) - Trajectory
    /// here is only [origin, hitVoxel] (Phase 8 doesn't record intermediate
    /// steps), so this instead moves ONE dot sprite (the bullet's own base
    /// frame, offset+0) smoothly from origin to hit voxel over a fixed short
    /// duration, rather than reproducing the 35-frame trailing-streak sampling.
    /// </summary>
    public sealed class ProjectileView : MonoBehaviour
    {
        private const float FlightSeconds = 0.25f;

        private (Texture2D texture, List<Rect> frameRects) _atlas;
        private SpriteRenderer _dot;
        private float _elapsed = -1f;
        private Vector3 _from;
        private Vector3 _to;

        private void Awake()
        {
            var go = new GameObject("Bullet");
            go.transform.SetParent(transform, worldPositionStays: false);
            _dot = go.AddComponent<SpriteRenderer>();
            _dot.sortingOrder = short.MaxValue; // always drawn above tiles/units - a bullet is never occluded mid-flight
            _dot.enabled = false;
        }

        public void Setup((Texture2D texture, List<Rect> frameRects) atlas)
        {
            _atlas = atlas;
        }

        /// <summary>Starts a new bullet-trail animation from originVoxel to
        /// hitVoxel, using bulletSpriteBase (RuleItem.BulletSprite) as the
        /// dot's frame within the bulletsprites atlas.</summary>
        public void Play(Position originVoxel, Position hitVoxel, int bulletSpriteBase)
        {
            var (fx, fy) = IsoProjection.VoxelWorldPosition(originVoxel.X, originVoxel.Y, originVoxel.Z, TileRenderer.PixelsPerUnit);
            var (tx, ty) = IsoProjection.VoxelWorldPosition(hitVoxel.X, hitVoxel.Y, hitVoxel.Z, TileRenderer.PixelsPerUnit);
            _from = new Vector3(fx, fy, -0.01f);
            _to = new Vector3(tx, ty, -0.01f);
            _dot.sprite = Sprite.Create(_atlas.texture, _atlas.frameRects[bulletSpriteBase], new Vector2(0.5f, 0.5f), TileRenderer.PixelsPerUnit);
            _dot.enabled = true;
            _elapsed = 0f;
            _dot.transform.position = _from;
        }

        private void Update()
        {
            if (_elapsed < 0f)
                return;

            _elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(_elapsed / FlightSeconds);
            _dot.transform.position = Vector3.Lerp(_from, _to, t);

            if (t >= 1f)
            {
                _elapsed = -1f;
                _dot.enabled = false;
            }
        }
    }
}
```

In `unity/Assets/Scripts/Unity/BattleController.cs`, add a serialized field near `tilesPerSecond`:

```csharp
        [SerializeField] private ProjectileView projectileView;
```

In `DrainAndAnimate`, extend the `ProjectileFiredEvent` branch:

```csharp
                else if (evt is ProjectileFiredEvent fired)
                {
                    Debug.Log(fired.Hit
                        ? $"{fired.Attacker.Name} hits {fired.Defender.Name}"
                        : $"{fired.Attacker.Name} misses {fired.Defender.Name}");

                    if (projectileView != null && fired.Trajectory.Count >= 2)
                        projectileView.Play(fired.Trajectory[0], fired.Trajectory[^1], fired.Weapon.BulletSprite);

                    if (_unitTransforms.TryGetValue(fired.Attacker, out var shooterTransform))
                        StartFiringPose(shooterTransform, fired.Attacker.Direction);
                }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet test unity/Tests.Standalone/OpenXcom.Core.Tests.csproj`
Expected: full suite passes (this also exercises every pre-existing `ProjectileFiredEvent`-touching test from Phase 8, confirming the new trailing `Weapon` constructor argument didn't silently break anything — since no test directly constructs `ProjectileFiredEvent` itself, only `BattleState.TryFire`'s own call site needed updating).

- [ ] **Step 5: Verify Unity-side compiles**

Live-Editor `check_compile_errors` for `ProjectileView.cs`/`BattleController.cs` per the same pattern as Tasks 5/6 (expect `BattlescapeBootstrap.cs` errors to persist until Task 8 — same caveat as Task 5 Step 6).

- [ ] **Step 6: Commit**

```bash
git add unity/Assets/Scripts/Core/Battle/BattleEvent.cs unity/Assets/Scripts/Core/Battle/BattleState.cs unity/Tests.Standalone/BattleStateFireTests.cs unity/Assets/Scripts/Unity/Rendering/IsoProjection.cs unity/Tests.Standalone/Unity/IsoProjectionTests.cs unity/Assets/Scripts/Unity/Rendering/ProjectileView.cs unity/Assets/Scripts/Unity/BattleController.cs
git commit -m "feat(unity): ProjectileView - bullet-trail visual along the Phase 8 traced path"
```

---

## Task 8: `BattlescapeBootstrap` — end-to-end wiring

Final task: wires every prior task's new capability into the actual scene bootstrap. This is the task verified by actually playing the game (design spec §9), not just a compile check.

**Files:**
- Modify: `unity/Assets/Scripts/Unity/BattlescapeBootstrap.cs`

**Interfaces:**
- Consumes: everything from Tasks 2 (`handob`/`bulletsprites` atlases), 5 (`UnitRenderer.Setup`/`.SetFrame` new signatures), 7 (`ProjectileView.Setup`).

- [ ] **Step 1: Rewrite `BattlescapeBootstrap.cs`**

Replace the full contents of `unity/Assets/Scripts/Unity/BattlescapeBootstrap.cs`:

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
    [RequireComponent(typeof(TileCursorView))]
    [RequireComponent(typeof(PathPreviewView))]
    [RequireComponent(typeof(ProjectileView))]
    public sealed class BattlescapeBootstrap : MonoBehaviour
    {
        private static readonly Position SoldierAPos = new(1, 1, 0);
        private static readonly Position SoldierBPos = new(2, 1, 0);
        private static readonly Position SectoidAPos = new(8, 8, 0);
        private static readonly Position SectoidBPos = new(7, 8, 0);

        /// <summary>Initial spawn facing: direction 4 = south / facing the
        /// camera (UnitSprite.cpp:290-369,620; Pathfinding.h:220 for the
        /// direction convention) - units turn freely once the battle starts
        /// (BattleState.TryMove/TryFire), this is just the spawn pose.</summary>
        private const int SouthDirection = 4;

        private void Start()
        {
            string gameDataDir = Path.Combine(Application.dataPath, "GameData");
            var grid = GetComponent<BattlescapeMapView>().Grid;

            var armorsById = DataLoader.LoadArmors(gameDataDir).ToDictionary(a => a.Id);
            var unitsById = DataLoader.LoadUnits(gameDataDir, armorsById).ToDictionary(u => u.Id);
            var itemsById = DataLoader.LoadItems(gameDataDir).ToDictionary(i => i.Id);

            var xcomAtlas = AtlasLoader.Load(gameDataDir, "units-XCOM_0");
            var sectoidAtlas = AtlasLoader.Load(gameDataDir, "units-SECTOID");
            var handobAtlas = AtlasLoader.Load(gameDataDir, "handob");
            var bulletAtlas = AtlasLoader.Load(gameDataDir, "bulletsprites");

            var state = new BattleState(grid);
            state.LoftData = DataLoader.LoadLoftemps(gameDataDir);
            var unitTransforms = new Dictionary<BattleUnit, Transform>();

            Spawn(state, grid, unitTransforms, unitsById["STR_SOLDIER"], itemsById["STR_RIFLE"],
                Faction.Player, "Soldier A", SoldierAPos, xcomAtlas, handobAtlas);
            Spawn(state, grid, unitTransforms, unitsById["STR_SOLDIER"], itemsById["STR_RIFLE"],
                Faction.Player, "Soldier B", SoldierBPos, xcomAtlas, handobAtlas);
            Spawn(state, grid, unitTransforms, unitsById["STR_SECTOID_SOLDIER"], itemsById["STR_PLASMA_PISTOL"],
                Faction.Hostile, "Sectoid A", SectoidAPos, sectoidAtlas, handobAtlas);
            Spawn(state, grid, unitTransforms, unitsById["STR_SECTOID_SOLDIER"], itemsById["STR_PLASMA_PISTOL"],
                Faction.Hostile, "Sectoid B", SectoidBPos, sectoidAtlas, handobAtlas);

            GetComponent<BattleController>().Bind(state, unitTransforms);

            var cursorAtlas = AtlasLoader.Load(gameDataDir, "cursor");
            GetComponent<TileCursorView>().Setup(GetComponent<BattleController>(), cursorAtlas, grid.Width, grid.Length);

            var pathAtlas = AtlasLoader.Load(gameDataDir, "pathfinding");
            GetComponent<PathPreviewView>().Setup(GetComponent<BattleController>(), state, pathAtlas);

            GetComponent<ProjectileView>().Setup(bulletAtlas);
        }

        private void Spawn(BattleState state, OpenXcom.Core.Battle.TileGrid grid,
            Dictionary<BattleUnit, Transform> unitTransforms,
            RuleUnit ruleUnit, RuleItem weapon, Faction faction, string name, Position position,
            (Texture2D texture, List<Rect> frameRects) bodyAtlas,
            (Texture2D texture, List<Rect> frameRects) itemAtlas)
        {
            var unit = new BattleUnit(ruleUnit, faction, name)
            {
                Position = position,
                Direction = SouthDirection,
                RightHand = new BattleItem(weapon),
            };
            grid.At(position.X, position.Y, position.Z).Occupant = unit;
            state.Units.Add(unit);

            var go = new GameObject(name);
            go.transform.SetParent(transform, worldPositionStays: false);
            var renderer = go.AddComponent<UnitRenderer>();
            renderer.Setup(position.X, position.Y, position.Z, grid.Width, grid.Length, bodyAtlas, itemAtlas, weapon);
            renderer.SetFrame(unit.Direction, walkPhase: -1, isAiming: false);

            unitTransforms[unit] = go.transform;
        }
    }
}
```

Note what's removed vs. the pre-Phase-9 version: the hardcoded `LegsStandBase`/`RightArmStandBase`/`MaleTorsoBase`/`LeftArmStandBase` constants and the private `FrameSprite` helper are gone — that logic now lives in `UnitSpriteFrames`/`UnitRenderer` (Tasks 4-5). `SouthDirection` stays (still needed for the initial spawn pose).

- [ ] **Step 2: Verify live in the Unity Editor**

Per design spec §9 and this plan's Global Constraints:
1. Call `mcp__coplay-mcp__list_unity_project_roots`; if it returns this project, `mcp__coplay-mcp__set_unity_project_root` to it.
2. `mcp__coplay-mcp__check_compile_errors` — must report zero errors (this is the first point where every task's changes compile together).
3. `mcp__coplay-mcp__play_game`.
4. Move a soldier in a few different directions; confirm it turns and visibly steps through a walk cycle (not a static slide).
5. Fire at a visible Sectoid; confirm the shooter's firing pose plays facing the target, and a bullet-trail dot travels from shooter to the actual hit point.
6. Continue firing until a unit dies; confirm a short death-frame sequence plays before the unit disappears, not an instant vanish.
7. Confirm both soldiers show the Rifle in-hand and both Sectoids show the Plasma Pistol in-hand at their spawn (south-facing) pose.
8. Use `mcp__coplay-mcp__get_unity_logs` to confirm no new exceptions appeared during the above.
9. `mcp__coplay-mcp__stop_game`.

If no Editor is connected at this point in execution, state that explicitly rather than claiming this was verified — this task's live-Play verification is its primary test, since none of its own lines are covered by `dotnet test`.

- [ ] **Step 3: Commit**

```bash
git add unity/Assets/Scripts/Unity/BattlescapeBootstrap.cs
git commit -m "feat(unity): BattlescapeBootstrap - wire animation, facing, and weapon visuals end-to-end"
```

---

## Self-Review

**Spec coverage** (against `docs/superpowers/specs/2026-07-15-phase9-animations-weapons-design.md`):
- §2 Facing → Task 1.
- §3 Walk-cycle & standing animation → Tasks 4-6.
- §4 Death animation → Tasks 4-6 (`DeathFrame`/`SetDeathFrame`/`StartDeathSequence`), explicit single-sequence-regardless-of-cause simplification carried over verbatim from the spec.
- §5 Weapon visuals (hand sprite, compositing, firing pose) → Tasks 2, 3, 5, 6.
- §6 Projectile visual → Tasks 2, 3, 7.
- §7 `Xcom.Convert` additions → Task 2.
- §8 (Phase 9 does NOT include list) → respected throughout: no inventory/drop visuals, no weapons beyond Rifle/Pistol, no autofire multi-shot sequencing beyond one trail per shot, no melee animation, no reaction-fire triggers, no death-pose variation by damage type.
- §9 Verification → Task 8 Step 2 reproduces this checklist exactly.

**Placeholder scan:** none — every task has complete code, every simplification is labeled **[SIMPLIFIED]** with what's cut and why (torso/arm walk-bob in Task 4, item-vs-body draw order in Task 5, 35-frame trailing streak in Task 7).

**Type/signature consistency:** `UnitRenderer.Setup`'s new 6-arg signature (Task 5) is used identically in Task 8's `Spawn`. `UnitRenderer.SetFrame(int, int, bool)` is called with the same 3 named args (`direction`, `walkPhase`, `isAiming`) everywhere it appears (Tasks 5, 6, 8). `ProjectileFiredEvent`'s new 5-arg constructor (Task 7) has exactly one production call site (`BattleState.TryFire`), updated in the same task. `UnitMovedEvent`'s new 3-arg constructor (Task 1) has its one pre-existing test call site fixed in the same task. `UnitSpriteFrames`'s method names/signatures as defined in Task 4 are used unchanged by Task 5's `UnitRenderer`.

**Ambiguity check:** the walk-phase-vs-timed-sequence mechanism split (Task 6) and the draw-order/trailing-streak simplifications (Tasks 5, 7) are each resolved explicitly with a stated reason, not left for an implementer to guess at.
