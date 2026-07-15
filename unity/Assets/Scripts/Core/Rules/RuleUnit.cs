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
