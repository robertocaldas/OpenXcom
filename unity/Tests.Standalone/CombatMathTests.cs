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
    }
}
