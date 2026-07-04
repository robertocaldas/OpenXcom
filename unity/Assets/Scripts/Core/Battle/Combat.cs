using System;
using OpenXcom.Core.Common;
using OpenXcom.Core.Rules;

namespace OpenXcom.Core.Battle
{
    /// <summary>Result of resolving one shot, for UI/logging and tests.</summary>
    public readonly struct ShotResult
    {
        public readonly bool Hit;
        public readonly int RolledDamage;   // before armor
        public readonly int AppliedDamage;  // after armor, actually dealt
        public readonly bool Killed;

        public ShotResult(bool hit, int rolled, int applied, bool killed)
        {
            Hit = hit;
            RolledDamage = rolled;
            AppliedDamage = applied;
            Killed = killed;
        }

        public static ShotResult Miss => new(false, 0, 0, false);
    }

    /// <summary>
    /// Stateless combat math: hit chance, hit roll, damage roll, damage application.
    /// This is the most valuable code to keep faithful and heavily unit-tested,
    /// because it decides whether the game "feels" like X-COM.
    /// </summary>
    public static class Combat
    {
        /// <summary>
        /// Final to-hit percentage including distance drop-off.
        /// Base accuracy: BattleUnit.GetFiringAccuracy.
        /// Drop-off: TileEngine.cpp:2621-2635.
        /// </summary>
        public static int HitChance(BattleUnit attacker, BattleItem weapon,
            BattleActionType action, Position target)
        {
            int acc = attacker.GetFiringAccuracy(action, weapon);
            var r = weapon.Rules;

            if (r.DropOff > 0)
            {
                double distance = attacker.Position.Distance(target);
                if (distance > r.UpperLimit)
                    acc -= (int)((distance - r.UpperLimit) * r.DropOff);
                else if (distance < r.LowerLimit)
                    acc -= (int)((r.LowerLimit - distance) * r.DropOff);
            }

            return Math.Clamp(acc, 0, 100);
        }

        /// <summary>
        /// Roll damage for a hit and apply it to the defender through armor.
        /// Standard type rolls uniform 0..200% of power (TileEngine.cpp:3233).
        /// </summary>
        public static int RollDamage(Rng rng, RuleItem weapon)
        {
            // Only Standard is implemented in slice 1; others reuse the same curve.
            return rng.Generate(0, 200) * weapon.Power / 100;
        }

        /// <summary>Which armor side is hit, from attacker→defender direction. Slice 1: front/rear only.</summary>
        public static UnitSide HitSide(Position attacker, Position defender)
        {
            // Very coarse: if the attacker is roughly behind the defender's facing we'd
            // use rear; slice 1 has no per-unit facing-vs-shot math yet, so treat all
            // shots as hitting the front. Refined in the LOS/facing slice.
            return UnitSide.Front;
        }

        /// <summary>
        /// Resolve a single projectile against a target unit: roll to hit, and on a
        /// hit roll and apply damage. Deterministic for a given <paramref name="rng"/>.
        /// </summary>
        public static ShotResult ResolveShot(Rng rng, BattleUnit attacker, BattleItem weapon,
            BattleActionType action, BattleUnit defender)
        {
            int chance = HitChance(attacker, weapon, action, defender.Position);
            if (!rng.Percent(chance))
                return ShotResult.Miss;

            int rolled = RollDamage(rng, weapon.Rules);
            var side = HitSide(attacker.Position, defender.Position);
            int armor = defender.Armor.ValueFor(side);
            int applied = Math.Max(0, rolled - armor);

            defender.Health -= applied;
            bool killed = !defender.IsAlive;
            return new ShotResult(true, rolled, applied, killed);
        }
    }
}
