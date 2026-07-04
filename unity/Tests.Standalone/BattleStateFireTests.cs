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

        // Firing=0 -> HitChance is always 0 -> Rng.Percent(0) is always
        // false (Rng.cs: chance<=0 always misses), deterministic regardless
        // of seed.
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
            var (grid, state) = MakeOpenBattle();
            var attacker = MakeGuaranteedHitAttacker(new Position(0, 0, 0));
            var defender = MakeDefender(new Position(3, 0, 0), health: 10000);
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
                var freshState = new BattleState(freshGrid, new Rng(seed));
                var attacker = MakeGuaranteedHitAttacker(new Position(0, 0, 0));
                var defender = MakeDefender(new Position(3, 0, 0), health: 1);
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
