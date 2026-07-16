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
            Assert.Equal(16, unit.StandHeight);
            Assert.Equal(12, unit.KneelHeight);
            Assert.Equal(0, unit.FloatHeight);
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
            Assert.Equal(22, unit.StandHeight);
            Assert.Equal(14, unit.KneelHeight);
            Assert.Equal(0, unit.FloatHeight);
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
            Assert.Equal(0, rifle.HandSprite);
            Assert.Equal(70, rifle.BulletSprite); // raw bulletSprite=2 * 35 (RuleItem.cpp:353 loadSpriteOffset multiplier)

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
            Assert.Equal(104, pistol.HandSprite);
            Assert.Equal(280, pistol.BulletSprite); // raw bulletSprite=8 * 35
        }
    }
}
