using OpenXcom.Core.Battle;
using OpenXcom.Core.Common;
using OpenXcom.Core.Rules;
using Xunit;

namespace OpenXcom.Core.Tests
{
    public class CombatMathTests
    {
        private static BattleUnit MakeShooter(int firing = 50, RuleItem weapon = null)
        {
            var stats = UnitStats.Rookie;
            stats.Firing = firing;
            var unit = new BattleUnit(new RuleUnit("SHOOTER", stats, RuleArmor.None), Faction.Player)
            {
                Position = new Position(0, 0),
            };
            unit.RightHand = new BattleItem(weapon ?? RuleItem.Rifle);
            return unit;
        }

        [Fact]
        public void FiringAccuracy_SnapWithRifle_IsStatTimesWeaponPercent()
        {
            // firing 50 * rifle snap 60% = 30
            var shooter = MakeShooter(firing: 50, weapon: RuleItem.Rifle);
            int acc = shooter.GetFiringAccuracy(BattleActionType.Snapshot, shooter.RightHand);
            Assert.Equal(30, acc);
        }

        [Fact]
        public void FiringAccuracy_Aimed_UsesAimedPercent()
        {
            // firing 80 * rifle aimed 110% = 88
            var shooter = MakeShooter(firing: 80, weapon: RuleItem.Rifle);
            int acc = shooter.GetFiringAccuracy(BattleActionType.AimedShot, shooter.RightHand);
            Assert.Equal(88, acc);
        }

        [Fact]
        public void FiringAccuracy_Kneeling_AddsFifteenPercentBonus()
        {
            var shooter = MakeShooter(firing: 50, weapon: RuleItem.Rifle);
            shooter.Kneeled = true;
            // (50 * 60/100) * 115/100 = 30 * 115/100 = 34
            int acc = shooter.GetFiringAccuracy(BattleActionType.Snapshot, shooter.RightHand);
            Assert.Equal(34, acc);
        }

        [Fact]
        public void FiringAccuracy_TwoHandedWithBothHandsFull_AppliesOneHandPenalty()
        {
            var shooter = MakeShooter(firing: 50, weapon: RuleItem.Rifle); // two-handed
            shooter.LeftHand = new BattleItem(RuleItem.Pistol);            // other hand occupied
            // 30 * 80/100 = 24
            int acc = shooter.GetFiringAccuracy(BattleActionType.Snapshot, shooter.RightHand);
            Assert.Equal(24, acc);
        }

        [Fact]
        public void FiringAccuracy_WoundedShooter_ScalesByHealthRatio()
        {
            var shooter = MakeShooter(firing: 50, weapon: RuleItem.Rifle);
            shooter.Health = shooter.Stats.Health / 2; // 50% health
            // 30 * 50/100 = 15
            int acc = shooter.GetFiringAccuracy(BattleActionType.Snapshot, shooter.RightHand);
            Assert.Equal(15, acc);
        }

        [Fact]
        public void HitChance_IsClampedToHundred()
        {
            var shooter = MakeShooter(firing: 120, weapon: RuleItem.Rifle);
            int chance = Combat.HitChance(shooter, shooter.RightHand,
                BattleActionType.AimedShot, new Position(5, 0));
            Assert.Equal(100, chance); // 120*110/100 = 132 → clamped
        }

        [Fact]
        public void ResolveShot_IsDeterministicForSeed()
        {
            var shooter = MakeShooter(firing: 100, weapon: RuleItem.Rifle);
            var defender = new BattleUnit(RuleUnit.Sectoid, Faction.Hostile)
            {
                Position = new Position(3, 0),
            };
            int startHealth = defender.Health;

            var a = Combat.ResolveShot(new Rng(42), shooter, shooter.RightHand,
                BattleActionType.AimedShot, MakeFreshDefender(out int h1));
            var b = Combat.ResolveShot(new Rng(42), shooter, shooter.RightHand,
                BattleActionType.AimedShot, MakeFreshDefender(out int h2));

            Assert.Equal(a.Hit, b.Hit);
            Assert.Equal(a.RolledDamage, b.RolledDamage);
            Assert.Equal(a.AppliedDamage, b.AppliedDamage);
        }

        [Fact]
        public void ResolveShot_AppliedDamageNeverNegative()
        {
            var shooter = MakeShooter(firing: 100, weapon: RuleItem.Rifle);
            // Run many seeds; applied damage must stay >= 0 and reduce health on hit.
            for (uint seed = 1; seed < 200; seed++)
            {
                var defender = MakeFreshDefender(out int start);
                var res = Combat.ResolveShot(new Rng(seed), shooter, shooter.RightHand,
                    BattleActionType.AimedShot, defender);
                Assert.True(res.AppliedDamage >= 0);
                if (res.Hit)
                    Assert.Equal(start - res.AppliedDamage, defender.Health);
            }
        }

        private static BattleUnit MakeFreshDefender(out int startHealth)
        {
            var d = new BattleUnit(RuleUnit.Sectoid, Faction.Hostile)
            {
                Position = new Position(3, 0),
            };
            startHealth = d.Health;
            return d;
        }

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
        public void ExtendAimVoxel_ScalesAxisAlignedDirectionOutToMaxRange()
        {
            var origin = new Position(0, 0, 0);
            var aim = new Position(10, 0, 0); // pure +X direction, length 10

            var extended = Combat.ExtendAimVoxel(origin, aim, maxRange: 100);

            Assert.Equal(100, extended.X);
            Assert.Equal(0, extended.Y);
            Assert.Equal(0, extended.Z);
        }

        [Fact]
        public void ExtendAimVoxel_PreservesDirectionForADiagonalAim()
        {
            var origin = new Position(0, 0, 0);
            var aim = new Position(3, 4, 0); // length 5 (3-4-5 right triangle)

            var extended = Combat.ExtendAimVoxel(origin, aim, maxRange: 50);

            // unit direction (3/5, 4/5, 0) * 50 = (30, 40, 0)
            Assert.Equal(30, extended.X);
            Assert.Equal(40, extended.Y);
            Assert.Equal(0, extended.Z);
        }

        [Fact]
        public void ExtendAimVoxel_ZeroLengthDirectionReturnsAimVoxelUnchanged()
        {
            var origin = new Position(5, 5, 5);
            var aim = new Position(5, 5, 5); // same point as origin - no direction to extend along

            var extended = Combat.ExtendAimVoxel(origin, aim);

            Assert.Equal(aim, extended);
        }

        [Fact]
        public void ApplyDamage_AppliesArmorAndKillsWhenHealthReachesZero()
        {
            var rng = new Rng(1);
            var attacker = MakeShooter();
            var defender = MakeFreshDefender(out _);
            defender.Health = 1;

            var result = Combat.ApplyDamage(rng, attacker, RuleItem.Rifle, defender);

            Assert.True(result.Hit);
            Assert.True(result.Killed);
            // No health-clamping exists anywhere in BattleUnit/Combat (IsAlive is
            // simply Health > 0, matching original X-COM overkill behavior), so a
            // 1-HP defender hit for more than 1 damage goes negative, not to exactly
            // zero. Assert the real invariant (dead, and Health tracks the applied
            // damage exactly) rather than an exact-zero value that depends on this
            // roll happening to deal exactly 1 damage.
            Assert.True(defender.Health <= 0);
            Assert.Equal(1 - result.AppliedDamage, defender.Health);
        }
    }
}
