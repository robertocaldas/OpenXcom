namespace OpenXcom.Core.Rules
{
    /// <summary>
    /// Immutable template for a unit type (a soldier class or an alien species):
    /// its base stats and default armor. The mutable per-instance state lives in
    /// <c>Battle.BattleUnit</c>.
    /// Mirrors the OXCE <c>Unit</c>/<c>Soldier</c> rules split.
    /// </summary>
    public sealed class RuleUnit
    {
        public string Id { get; }
        public UnitStats Stats { get; }
        public RuleArmor Armor { get; }

        public RuleUnit(string id, UnitStats stats, RuleArmor armor)
        {
            Id = id;
            Stats = stats;
            Armor = armor;
        }

        public static RuleUnit Soldier => new(
            "STR_SOLDIER", UnitStats.Rookie, RuleArmor.None);

        public static RuleUnit Sectoid => new(
            "STR_SECTOID",
            new UnitStats
            {
                TimeUnits = 54, Stamina = 90, Health = 30, Bravery = 80,
                Reactions = 63, Firing = 52, Throwing = 58, Strength = 30, Melee = 40,
            },
            RuleArmor.None);
    }
}
