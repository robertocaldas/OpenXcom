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
        public readonly UnitSide Side;      // which armor side was hit (Front placeholder for a miss)

        public ShotResult(bool hit, int rolled, int applied, bool killed, UnitSide side)
        {
            Hit = hit;
            RolledDamage = rolled;
            AppliedDamage = applied;
            Killed = killed;
            Side = side;
        }

        public static ShotResult Miss => new(false, 0, 0, false, UnitSide.Front);
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
        /// Rolls and applies damage from `weapon` to `target`, mutating
        /// target.Health. Does not roll to-hit - the caller has already
        /// determined this shot connects, either via ResolveShot's classic
        /// percent-chance roll or via a voxel trace in
        /// BattleState.TryFire's Phase 8 path.
        /// </summary>
        public static ShotResult ApplyDamage(Rng rng, BattleUnit attacker, RuleItem weapon, BattleUnit target)
        {
            int rolled = RollDamage(rng, weapon);
            var side = HitSide(attacker.Position, target.Position);
            int armor = target.Armor.ValueFor(side);
            int applied = Math.Max(0, rolled - armor);

            target.Health -= applied;
            bool killed = !target.IsAlive;
            return new ShotResult(true, rolled, applied, killed, side);
        }

        /// <summary>
        /// Resolve a single projectile against a target unit: roll to hit, and on a
        /// hit roll and apply damage. Deterministic for a given <paramref name="rng"/>.
        /// Kept as a tested library method for CombatMathTests.cs and any future
        /// caller that wants the classic percent-roll behavior; not currently
        /// called by BattleState.TryFire, which resolves hits via a voxel trace
        /// and calls Combat.ApplyDamage directly instead.
        /// </summary>
        public static ShotResult ResolveShot(Rng rng, BattleUnit attacker, BattleItem weapon,
            BattleActionType action, BattleUnit defender)
        {
            int chance = HitChance(attacker, weapon, action, defender.Position);
            if (!rng.Percent(chance))
                return ShotResult.Miss;

            return ApplyDamage(rng, attacker, weapon.Rules, defender);
        }

        /// <summary>
        /// Aim-point scatter from an accuracy percent (0-100), operating in
        /// voxel coordinates. Port of Projectile::applyAccuracy's "classic"
        /// (non-uniform, Options::oxceUniformShootingSpread == false) branch
        /// (src/Battlescape/Projectile.cpp:332-462). The C++ works in a
        /// 0.0-1.0 accuracy fraction multiplied by 100; this takes the
        /// already-0-100 percent HitChance uses directly, so
        /// "accuracy*100" in the original becomes "accuracyPercent" here -
        /// same formula, adapted scale. Does not model range-based accuracy
        /// dropoff (Combat.HitChance already applies that before this is
        /// called) or the OXCE uniform-spread toggle.
        /// </summary>
        public static Position ApplyDeviation(Rng rng, Position originVoxel, Position targetVoxel, int accuracyPercent)
        {
            int xDist = Math.Abs(originVoxel.X - targetVoxel.X);
            int yDist = Math.Abs(originVoxel.Y - targetVoxel.Y);
            int zDist = Math.Abs(originVoxel.Z - targetVoxel.Z);

            int xyShift = (xDist / 2 <= yDist) ? xDist / 4 + yDist : (xDist + yDist) / 2;
            int zShift = (xyShift <= zDist) ? xyShift / 2 + zDist : xyShift + zDist / 2;

            int deviation = rng.Generate(0, 100) - accuracyPercent;
            deviation += deviation >= 0 ? 50 : 10;
            deviation = Math.Max(1, zShift * deviation / 200);

            int dx = rng.Generate(0, deviation) - deviation / 2;
            int dy = rng.Generate(0, deviation) - deviation / 2;
            int dz = rng.Generate(0, deviation / 2) / 2 - deviation / 8;

            return new Position(targetVoxel.X + dx, targetVoxel.Y + dy, targetVoxel.Z + dz);
        }

        /// <summary>
        /// Extends a deviated aim point out to maxRange voxel units from origin, along
        /// the same direction - so a miss doesn't just fall short of or beside the
        /// target, it continues on to strike whatever lies behind it. Port of
        /// Projectile::applyAccuracy's extendLine block (src/Battlescape/Projectile.cpp:463-478),
        /// using vector normalize-and-scale instead of the C++'s equivalent
        /// azimuth/elevation trig reconstruction - same result, simpler
        /// implementation. maxRange default 16000 matches the C++'s hardcoded
        /// "16*1000 // 1000 tiles" (Projectile.cpp:339), the maxRange used for all
        /// direct-fire (non-BA_THROW/BA_LAUNCH) shots, which always pass
        /// extendLine=true (Projectile.cpp:183,201).
        /// </summary>
        public static Position ExtendAimVoxel(Position origin, Position aimVoxel, int maxRange = 16000)
        {
            double dx = aimVoxel.X - origin.X;
            double dy = aimVoxel.Y - origin.Y;
            double dz = aimVoxel.Z - origin.Z;
            double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (length < 0.0001)
                return aimVoxel; // zero-length direction (origin == aimVoxel); nothing meaningful to extend

            double scale = maxRange / length;
            return new Position(
                origin.X + (int)(dx * scale),
                origin.Y + (int)(dy * scale),
                origin.Z + (int)(dz * scale));
        }
    }
}
