namespace OpenXcom.Core.Rules
{
    /// <summary>
    /// Immutable weapon/item template. Accuracy fields are the per-action weapon
    /// percentages from OXCE (RuleItem::getAccuracySnap/Aimed/Auto/Melee/Throw).
    /// TU costs are a percentage of the firer's MAX time units.
    /// Mirrors OXCE <c>RuleItem</c> (src/Mod/RuleItem.h), slice-1 subset.
    /// </summary>
    public sealed class RuleItem
    {
        public string Id { get; }
        public bool TwoHanded { get; init; }

        // Damage
        public int Power { get; init; }
        public DamageType DamageType { get; init; } = DamageType.Standard;

        // Accuracy per action (weapon %). 0 disables that action.
        public int AccuracySnap { get; init; }
        public int AccuracyAimed { get; init; }
        public int AccuracyAuto { get; init; }
        public int AccuracyMelee { get; init; }
        public int AccuracyThrow { get; init; }

        // Auto-shot: number of projectiles fired.
        public int AutoShots { get; init; } = 3;

        // TU cost as a percent of firer's max TUs.
        public int TuSnap { get; init; } = 25;
        public int TuAimed { get; init; } = 55;
        public int TuAuto { get; init; } = 35;
        public int TuMelee { get; init; } = 20;
        public int TuThrow { get; init; } = 25;

        // Accuracy distance drop-off (percent per tile) beyond/inside the limits.
        public int DropOff { get; init; } = 0;      // 0 = no drop-off
        public int UpperLimit { get; init; } = 200; // effectively no upper limit
        public int LowerLimit { get; init; } = 0;

        public RuleItem(string id)
        {
            Id = id;
        }

        /// <summary>Weapon accuracy % for the given action, or 0 if unsupported.</summary>
        public int AccuracyFor(BattleActionType action) => action switch
        {
            BattleActionType.Snapshot => AccuracySnap,
            BattleActionType.AimedShot => AccuracyAimed,
            BattleActionType.AutoShot => AccuracyAuto,
            BattleActionType.Melee => AccuracyMelee,
            BattleActionType.Throw => AccuracyThrow,
            _ => 0,
        };

        /// <summary>TU cost percent (of max TUs) for the given action.</summary>
        public int TuPercentFor(BattleActionType action) => action switch
        {
            BattleActionType.Snapshot => TuSnap,
            BattleActionType.AimedShot => TuAimed,
            BattleActionType.AutoShot => TuAuto,
            BattleActionType.Melee => TuMelee,
            BattleActionType.Throw => TuThrow,
            _ => 0,
        };

        // --- Sample content (stand-ins until data authoring lands) ---

        public static RuleItem Rifle => new("STR_RIFLE")
        {
            TwoHanded = true,
            Power = 30,
            DamageType = DamageType.Standard,
            AccuracySnap = 60,
            AccuracyAimed = 110,
            AccuracyAuto = 35,
            AutoShots = 3,
        };

        public static RuleItem Pistol => new("STR_PISTOL")
        {
            TwoHanded = false,
            Power = 26,
            DamageType = DamageType.Standard,
            AccuracySnap = 60,
            AccuracyAimed = 78,
            AccuracyAuto = 0,
        };
    }
}
