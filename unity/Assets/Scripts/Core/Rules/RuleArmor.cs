namespace OpenXcom.Core.Rules
{
    /// <summary>
    /// Immutable armor template. Directional armor values are subtracted from
    /// incoming damage on the corresponding hit side. Loftemps is the index
    /// into LOFTEMPS.DAT's voxel bitmask templates used for voxel-level hit
    /// detection (BattleUnit::getLoftemps(int entry), src/Savegame/BattleUnit.h:587) - single value
    /// since this rewrite has no big (2x2) units.
    /// Mirrors OXCE <c>Armor</c> (src/Mod/Armor.h), slice-1 subset.
    /// </summary>
    public sealed class RuleArmor
    {
        public string Id { get; }
        public int Front { get; }
        public int Side { get; }   // used for both Left and Right in slice 1
        public int Rear { get; }
        public int Under { get; }
        public int Loftemps { get; }

        public RuleArmor(string id, int front, int side, int rear, int under, int loftemps = 0)
        {
            Id = id;
            Front = front;
            Side = side;
            Rear = rear;
            Under = under;
            Loftemps = loftemps;
        }

        public int ValueFor(UnitSide s) => s switch
        {
            UnitSide.Front => Front,
            UnitSide.Left => Side,
            UnitSide.Right => Side,
            UnitSide.Rear => Rear,
            UnitSide.Under => Under,
            _ => Front,
        };

        /// <summary>Basic personal armor, roughly X-COM "Personal Armor" tier.</summary>
        public static RuleArmor Personal => new("STR_PERSONAL_ARMOR", front: 12, side: 8, rear: 5, under: 2, loftemps: 3);

        /// <summary>Unarmored (rookie in flight suit).</summary>
        public static RuleArmor None => new("STR_NONE", front: 2, side: 2, rear: 2, under: 2, loftemps: 3);
    }
}
