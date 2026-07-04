namespace OpenXcom.Core.Rules
{
    /// <summary>Kind of attack. Mirrors OXCE BattleActionType (subset for slice 1).</summary>
    public enum BattleActionType
    {
        None,
        Snapshot,
        AimedShot,
        AutoShot,
        Melee,
        Throw,
    }

    /// <summary>Damage type. Slice 1 only implements Standard; the rest are placeholders.</summary>
    public enum DamageType
    {
        Standard,   // 0..200% of power
        Armor,      // e.g. AP
        Incendiary,
        HighExplosive,
        Laser,
        Plasma,
        Stun,
    }

    /// <summary>Which armor value applies to a hit. Mirrors OXCE UnitSide.</summary>
    public enum UnitSide
    {
        Front,
        Left,
        Right,
        Rear,
        Under,
    }

    public enum Faction
    {
        Player,
        Hostile,
        Neutral,
    }
}
