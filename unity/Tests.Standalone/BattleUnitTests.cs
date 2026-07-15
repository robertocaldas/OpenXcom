using OpenXcom.Core.Battle;
using OpenXcom.Core.Rules;
using Xunit;

namespace OpenXcom.Core.Tests
{
    public class BattleUnitTests
    {
        [Fact]
        public void Height_ReflectsKneeledState()
        {
            var unit = new BattleUnit(new RuleUnit("STR_TEST", UnitStats.Rookie, RuleArmor.None,
                standHeight: 22, kneelHeight: 14), Faction.Player);

            Assert.Equal(22, unit.Height);
            unit.Kneeled = true;
            Assert.Equal(14, unit.Height);
        }
    }
}
