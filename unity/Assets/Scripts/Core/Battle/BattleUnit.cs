using System;
using OpenXcom.Core.Common;
using OpenXcom.Core.Rules;

namespace OpenXcom.Core.Battle
{
    /// <summary>
    /// Mutable per-instance combat state for one unit on the battlefield.
    /// References its immutable <see cref="RuleUnit"/> for base stats/armor.
    /// Mirrors OXCE's <c>BattleUnit</c> (src/Savegame/BattleUnit.cpp), slice-1 subset.
    /// </summary>
    public sealed class BattleUnit
    {
        public RuleUnit Rules { get; }
        public Faction Faction { get; }
        public string Name { get; set; }

        public Position Position;
        public int Direction; // 0..7, facing

        // Current pools (reset/refreshed each turn).
        public int TimeUnits;
        public int Energy;
        public int Health;

        // Fatal wounds to head+torso, used by the accuracy penalty. Slice 1 leaves at 0.
        public int FatalWounds;

        public bool Kneeled;

        // Held weapons (slice 1: right/left hand slots only).
        public BattleItem RightHand;
        public BattleItem LeftHand;

        public UnitStats Stats => Rules.Stats;
        public RuleArmor Armor => Rules.Armor;
        public bool IsAlive => Health > 0;

        public BattleUnit(RuleUnit rules, Faction faction, string name = null)
        {
            Rules = rules;
            Faction = faction;
            Name = name ?? rules.Id;
            Health = rules.Stats.Health;
            RefreshForNewTurn();
        }

        /// <summary>Restore TUs and (partial) energy at the start of the unit's turn.</summary>
        public void RefreshForNewTurn()
        {
            TimeUnits = Stats.TimeUnits;
            // OXCE recovers a fraction of stamina each turn; slice 1 fully refreshes.
            Energy = Math.Min(Stats.Stamina, Energy + Stats.Stamina);
        }

        public BattleItem WeaponFor(BattleActionType action)
        {
            // Prefer the right hand if it supports the action, else the left.
            if (RightHand != null && RightHand.Rules.AccuracyFor(action) > 0) return RightHand;
            if (LeftHand != null && LeftHand.Rules.AccuracyFor(action) > 0) return LeftHand;
            return RightHand ?? LeftHand;
        }

        /// <summary>
        /// Final firing accuracy percent for an attack, before distance drop-off.
        /// Port of BattleUnit::getFiringAccuracy (src/Savegame/BattleUnit.cpp:2473).
        ///
        /// Formula = accuracyStat * weaponAccuracy%
        ///           * kneelBonus(1.15) * oneHandPenalty(0.8)
        ///           * woundsPenalty(health%) * critPenalty(-10%/wound)
        /// </summary>
        public int GetFiringAccuracy(BattleActionType action, BattleItem weapon)
        {
            var r = weapon.Rules;
            int weaponAcc = r.AccuracyFor(action);
            bool kneeled = Kneeled;

            int stat = action switch
            {
                BattleActionType.Melee => Stats.Melee,
                BattleActionType.Throw => Stats.Throwing,
                _ => Stats.Firing,
            };

            // Melee/throw don't get the kneel bonus (BattleUnit.cpp:2495,2500).
            if (action == BattleActionType.Melee || action == BattleActionType.Throw)
                kneeled = false;

            // Base: accuracyStat * weaponAccuracy% / 100
            int result = stat * weaponAcc / 100;

            // Kneeling bonus (+15%) for firing actions.
            if (kneeled)
                result = result * 115 / 100;

            // Two-handed weapon fired with the other hand occupied → 20% penalty.
            if (r.TwoHanded && action != BattleActionType.Throw)
            {
                if (RightHand != null && LeftHand != null)
                    result = result * 80 / 100;
            }

            // Health + fatal-wound penalty (getAccuracyModifier).
            result = result * GetAccuracyModifier() / 100;

            return Math.Max(result, 0);
        }

        /// <summary>
        /// Health-and-wounds penalty as a percent. 100 = no penalty.
        /// Port of BattleUnit::getAccuracyModifier (src/Savegame/BattleUnit.cpp:2540).
        /// </summary>
        public int GetAccuracyModifier()
        {
            int healthPercent = Stats.Health > 0 ? 100 * Health / Stats.Health : 0;
            return Math.Max(healthPercent - 10 * FatalWounds, 0);
        }

        /// <summary>
        /// TU cost for an attack action with the given weapon (percent of
        /// max TUs). Floored at 1: a low-TimeUnits unit combined with a low
        /// TU-percent weapon could otherwise truncate to 0 (integer
        /// division), which would let CanSpend(0) always succeed - Phase 5's
        /// AiModule.RunHostileTurn relies on every successful fire spending
        /// TU > 0 to guarantee its per-unit action loop terminates.
        /// </summary>
        public int FireTuCost(BattleActionType action, BattleItem weapon)
        {
            int percent = weapon.Rules.TuPercentFor(action);
            return Math.Max(1, Stats.TimeUnits * percent / 100);
        }

        public bool CanSpend(int tu) => TimeUnits >= tu;

        public void Spend(int tu)
        {
            TimeUnits = Math.Max(0, TimeUnits - tu);
        }
    }
}
