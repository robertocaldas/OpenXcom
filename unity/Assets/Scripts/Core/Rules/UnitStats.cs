namespace OpenXcom.Core.Rules
{
    /// <summary>
    /// The primary attribute block shared by soldiers and aliens.
    /// Mirrors OXCE's <c>UnitStats</c> (src/Mod/Unit.h), trimmed to slice-1 needs.
    /// </summary>
    public struct UnitStats
    {
        public int TimeUnits;
        public int Stamina;      // energy pool
        public int Health;
        public int Bravery;
        public int Reactions;
        public int Firing;       // firing accuracy stat
        public int Throwing;
        public int Strength;
        public int Melee;

        public static UnitStats Rookie => new()
        {
            TimeUnits = 50,
            Stamina = 60,
            Health = 30,
            Bravery = 50,
            Reactions = 30,
            Firing = 50,
            Throwing = 50,
            Strength = 25,
            Melee = 50,
        };
    }
}
