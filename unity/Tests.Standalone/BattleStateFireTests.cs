using System.Linq;
using OpenXcom.Core.Battle;
using OpenXcom.Core.Common;
using OpenXcom.Core.Rules;
using Xunit;

namespace OpenXcom.Core.Tests
{
    public class BattleStateFireTests
    {
        // Firing=100 against Rifle's AccuracyAimed=110 clamps to 100 ->
        // Rng.Percent(100) is always true (Rng.cs: chance>=100 always
        // hits), so this attacker's shots are deterministically guaranteed
        // to hit regardless of seed.
        private static BattleUnit MakeGuaranteedHitAttacker(Position pos)
        {
            var stats = new UnitStats { TimeUnits = 50, Health = 100, Firing = 100 };
            var unit = new BattleUnit(new RuleUnit("ATTACKER", stats, RuleArmor.None), Faction.Player)
            {
                Position = pos,
            };
            unit.RightHand = new BattleItem(RuleItem.Rifle);
            return unit;
        }

        // Firing=0 keeps this attacker's HitChance at 0, but that alone no
        // longer guarantees a miss - TryFire resolves hits via a real voxel
        // trace (TileEngine.CalculateLine), not a Rng.Percent(chance) roll.
        // The actual guaranteed-miss mechanism here is MakeOpenBattle()
        // never setting LoftData: TileEngine.VoxelCheck treats every voxel
        // as passable against an empty LoftData array, so the trace never
        // finds a hit regardless of accuracy.
        private static BattleUnit MakeGuaranteedMissAttacker(Position pos)
        {
            var stats = new UnitStats { TimeUnits = 50, Health = 100, Firing = 0 };
            var unit = new BattleUnit(new RuleUnit("ATTACKER", stats, RuleArmor.None), Faction.Player)
            {
                Position = pos,
            };
            unit.RightHand = new BattleItem(RuleItem.Rifle);
            return unit;
        }

        private static BattleUnit MakeDefender(Position pos, int health)
        {
            var stats = new UnitStats { Health = health };
            return new BattleUnit(new RuleUnit("DEFENDER", stats, RuleArmor.None), Faction.Hostile)
            {
                Position = pos,
            };
        }

        // Post-Phase-8 TryFire resolves hit/miss via a real voxel trace
        // (TileEngine.CalculateLine), not a direct percent roll - a
        // "guaranteed hit" now also needs real geometry for the traced aim
        // point to land in, not just 100 accuracy. FullTileArmor/
        // FullTileLoftData (below) give the defender a fully-solid loft
        // template across its whole tile column; standHeight/kneelHeight 23
        // (not some far-off value) keeps the aim point's Z itself inside the
        // map's single tile-level (voxel Z 0-23) while still covering
        // essentially that entire band, so any accuracy-driven deviation
        // still lands inside the defender deterministically.
        private static BattleUnit MakeFullTileDefender(Position pos, int health)
        {
            var stats = new UnitStats { Health = health };
            return new BattleUnit(new RuleUnit("DEFENDER", stats, FullTileArmor(), standHeight: 23, kneelHeight: 23), Faction.Hostile)
            {
                Position = pos,
            };
        }

        private static (TileGrid grid, BattleState state) MakeOpenBattle(int width = 25)
        {
            var grid = new TileGrid(width, 1, 1);
            return (grid, new BattleState(grid));
        }

        [Fact]
        public void TryFire_TargetBeyondMaxViewDistance_ReturnsNoLineOfSightAndSpendsNoTu()
        {
            var (grid, state) = MakeOpenBattle(width: 30);
            var attacker = MakeGuaranteedHitAttacker(new Position(0, 0, 0));
            var defender = MakeDefender(new Position(25, 0, 0), health: 30); // distance 25 > MaxViewDistance (20)
            int startingTu = attacker.TimeUnits;

            var result = state.TryFire(attacker, attacker.RightHand, BattleActionType.AimedShot, defender);

            Assert.Equal(FireOutcome.NoLineOfSight, result.Outcome);
            Assert.Equal(startingTu, attacker.TimeUnits);
            Assert.Empty(state.DequeueEvents());
        }

        [Fact]
        public void TryFire_InsufficientTu_ReturnsInsufficientTuAndSpendsNoTu()
        {
            var (grid, state) = MakeOpenBattle();
            var attacker = MakeGuaranteedHitAttacker(new Position(0, 0, 0));
            attacker.TimeUnits = 5; // Rifle aimed shot costs 55% of 50 max TU = 27; 5 is not enough
            var defender = MakeDefender(new Position(3, 0, 0), health: 30);

            var result = state.TryFire(attacker, attacker.RightHand, BattleActionType.AimedShot, defender);

            Assert.Equal(FireOutcome.InsufficientTu, result.Outcome);
            Assert.Equal(5, attacker.TimeUnits);
            Assert.Empty(state.DequeueEvents());
        }

        [Fact]
        public void TryFire_GuaranteedMiss_SpendsTuAndEnqueuesOnlyAMissEvent()
        {
            var (grid, state) = MakeOpenBattle();
            var attacker = MakeGuaranteedMissAttacker(new Position(0, 0, 0));
            var defender = MakeDefender(new Position(3, 0, 0), health: 30);
            int expectedTuCost = attacker.FireTuCost(BattleActionType.AimedShot, attacker.RightHand);

            var result = state.TryFire(attacker, attacker.RightHand, BattleActionType.AimedShot, defender);

            Assert.Equal(FireOutcome.Fired, result.Outcome);
            Assert.False(result.Shot.Hit);
            Assert.Equal(50 - expectedTuCost, attacker.TimeUnits); // TU spent even on a miss

            var events = state.DequeueEvents();
            Assert.Single(events);
            var fired = Assert.IsType<ProjectileFiredEvent>(events[0]);
            Assert.False(fired.Hit);
            Assert.Same(attacker, fired.Attacker);
            Assert.Same(defender, fired.Defender);
        }

        [Fact]
        public void TryFire_GuaranteedHitButHugeDefenderHealth_NeverKillsRegardlessOfDamageRoll()
        {
            // Max possible damage from Rifle (Power=30) is Generate(0,200)*30/100,
            // capped at 200*30/100 = 60. A defender with 10000 health can
            // never die to this hit no matter what the RNG rolls - this test
            // is deterministic without needing to know/guess a specific seed.
            // Uses MakeFullTileDefender/LoftData (not the plain
            // MakeDefender/RuleArmor.None pairing the other non-firing tests
            // above use) because TryFire's hit itself is no longer a direct
            // percent roll - see MakeFullTileDefender's comment.
            var (grid, state) = MakeOpenBattle();
            state.LoftData = FullTileLoftData();
            var attacker = MakeGuaranteedHitAttacker(new Position(0, 0, 0));
            var defender = MakeFullTileDefender(new Position(3, 0, 0), health: 10000);
            grid.At(3, 0, 0).Occupant = defender;

            var result = state.TryFire(attacker, attacker.RightHand, BattleActionType.AimedShot, defender);

            Assert.Equal(FireOutcome.Fired, result.Outcome);
            Assert.True(result.Shot.Hit);
            Assert.False(result.Shot.Killed);
            Assert.Same(defender, grid.At(3, 0, 0).Occupant); // occupancy untouched

            var events = state.DequeueEvents();
            Assert.Equal(2, events.Count); // ProjectileFired + UnitHit, no UnitDied
            Assert.IsType<ProjectileFiredEvent>(events[0]);
            var hitEvent = Assert.IsType<UnitHitEvent>(events[1]);
            Assert.Same(defender, hitEvent.Unit);
            Assert.Equal(result.Shot.AppliedDamage, hitEvent.Damage); // event carries the actual rolled/applied damage, not a copy that could drift
        }

        [Fact]
        public void TryFire_GuaranteedHitOnOneHealthDefender_AtLeastOneSeedKillsAndClearsOccupancy()
        {
            // RollDamage's minimum possible roll is 0 (Generate(0,200) can
            // return 0), so a single fixed seed can't be *proven* lethal by
            // construction alone - this loops many independent seeds (fresh
            // state each time, matching CombatMathTests.cs's existing
            // AppliedDamageNeverNegative pattern in this codebase) and
            // requires that AT LEAST ONE actually kills, so the test can't
            // pass vacuously if the death-handling branch is never reached.
            bool anyKillObserved = false;

            for (uint seed = 1; seed <= 100; seed++)
            {
                var freshGrid = new TileGrid(25, 1, 1);
                var freshState = new BattleState(freshGrid, new Rng(seed)) { LoftData = FullTileLoftData() };
                var attacker = MakeGuaranteedHitAttacker(new Position(0, 0, 0));
                var defender = MakeFullTileDefender(new Position(3, 0, 0), health: 1);
                freshGrid.At(3, 0, 0).Occupant = defender;

                var result = freshState.TryFire(attacker, attacker.RightHand, BattleActionType.AimedShot, defender);

                Assert.True(result.Shot.Hit); // accuracy is still 100 regardless of seed
                if (result.Shot.Killed)
                {
                    anyKillObserved = true;
                    Assert.Null(freshGrid.At(3, 0, 0).Occupant);
                    var events = freshState.DequeueEvents();
                    Assert.Equal(3, events.Count); // ProjectileFired + UnitHit + UnitDied
                    Assert.IsType<UnitDiedEvent>(events[2]);
                }
                else
                {
                    Assert.Same(defender, freshGrid.At(3, 0, 0).Occupant); // not killed -> occupancy untouched
                }
            }

            Assert.True(anyKillObserved, "Expected at least one of 100 seeds to produce a lethal hit on a 1-health defender.");
        }

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
            var defender = new BattleUnit(new RuleUnit("DEFENDER", UnitStats.Rookie, FullTileArmor(), standHeight: 23, kneelHeight: 23), Faction.Hostile)
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
            var bystander = new BattleUnit(new RuleUnit("BYSTANDER", UnitStats.Rookie, FullTileArmor(), standHeight: 23, kneelHeight: 23), Faction.Hostile)
            {
                Position = new Position(1, 0, 0),
            };
            var intendedTarget = new BattleUnit(new RuleUnit("FAR_TARGET", UnitStats.Rookie, FullTileArmor(), standHeight: 23, kneelHeight: 23), Faction.Hostile)
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
        public void TryFire_ShotContinuesPastTheAimedAtPointAndHitsWhatsBehindIt()
        {
            // Wide grid so there's real room "behind" the aimed-at tile to place a wall.
            var grid = new TileGrid(50, 5, 1);
            var state = new BattleState(grid) { LoftData = FullTileLoftData() };
            var attacker = new BattleUnit(RuleUnit.Soldier, Faction.Player) { Position = new Position(0, 0, 0) };

            // A far wall well beyond the aimed-at tile's range (tile 3) but within maxRange.
            var farWall = new MapDataTile { Loft = new int[12] };
            for (int i = 0; i < 12; i++) farWall.Loft[i] = 1;
            grid.At(20, 0, 0).Object = farWall;

            // aimedAt exists only to give TryFire a Position/LOS/TU target - deliberately NOT
            // placed in the grid's Occupant slot, so the ray finds nothing solid at its own
            // range and must continue past it to hit anything - proving the extend-line fix,
            // not re-testing the existing near-hit case (already covered by other TryFire tests).
            var aimedAt = new BattleUnit(new RuleUnit("PHANTOM", UnitStats.Rookie, FullTileArmor(), standHeight: 23, kneelHeight: 23), Faction.Hostile)
            {
                Position = new Position(3, 0, 0),
            };
            state.Units.Add(attacker);
            state.Units.Add(aimedAt);
            var weapon = new BattleItem(RuleItem.Rifle);
            attacker.RightHand = weapon;

            var result = state.TryFire(attacker, weapon, BattleActionType.Snapshot, aimedAt);
            var events = state.DequeueEvents();

            var fired = events.OfType<ProjectileFiredEvent>().Single();
            // Reaches at least tile 20's near voxel edge (20*16=320) - would be ~48-64
            // (tile 3's range) without the extend-line fix.
            Assert.True(fired.Trajectory.Last().X >= 320,
                $"expected trajectory to reach past X=320 (tile 20), got X={fired.Trajectory.Last().X}");
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

        [Fact]
        public void IsBattleOver_FalseWithLivingUnitsOnBothSides()
        {
            var (grid, state) = MakeOpenBattle();
            state.Units.Add(MakeGuaranteedHitAttacker(new Position(0, 0, 0)));
            state.Units.Add(MakeDefender(new Position(1, 0, 0), health: 10));

            Assert.False(state.IsBattleOver);
        }

        [Fact]
        public void IsBattleOver_TrueWhenAllPlayerUnitsAreDead()
        {
            var (grid, state) = MakeOpenBattle();
            var player = MakeGuaranteedHitAttacker(new Position(0, 0, 0));
            player.Health = 0; // dead, but still in the Units list (list membership alone must not be used)
            state.Units.Add(player);
            state.Units.Add(MakeDefender(new Position(1, 0, 0), health: 10));

            Assert.True(state.IsBattleOver);
        }

        [Fact]
        public void IsBattleOver_TrueWhenAllHostileUnitsAreDead()
        {
            var (grid, state) = MakeOpenBattle();
            state.Units.Add(MakeGuaranteedHitAttacker(new Position(0, 0, 0)));
            var hostile = MakeDefender(new Position(1, 0, 0), health: 10);
            hostile.Health = 0;
            state.Units.Add(hostile);

            Assert.True(state.IsBattleOver);
        }
    }
}
