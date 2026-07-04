namespace OpenXcom.Core.Common
{
    /// <summary>
    /// Deterministic, seedable RNG for all gameplay rolls. Keeping randomness behind
    /// one seedable source (rather than UnityEngine.Random) makes combat reproducible
    /// in tests — you can assert exact outcomes for a given seed.
    ///
    /// xorshift128 — small, fast, good enough for gameplay (not cryptographic).
    /// </summary>
    public sealed class Rng
    {
        private uint _x, _y, _z, _w;

        public Rng(uint seed = 0x1234_5678)
        {
            // splitmix-ish seeding so a 0 seed still spreads bits
            _x = seed == 0 ? 0xDEAD_BEEF : seed;
            _y = _x * 0x9E37_79B9u + 1u;
            _z = _y * 0x9E37_79B9u + 1u;
            _w = _z * 0x9E37_79B9u + 1u;
        }

        private uint NextUInt()
        {
            uint t = _x ^ (_x << 11);
            _x = _y; _y = _z; _z = _w;
            _w = _w ^ (_w >> 19) ^ (t ^ (t >> 8));
            return _w;
        }

        /// <summary>Inclusive range [min, max]. Mirrors OXCE RNG::generate(min, max).</summary>
        public int Generate(int min, int max)
        {
            if (max <= min) return min;
            uint span = (uint)(max - min + 1);
            return min + (int)(NextUInt() % span);
        }

        /// <summary>Percent roll: true if a d100 (0..99) rolls under <paramref name="chance"/>.</summary>
        public bool Percent(int chance)
        {
            if (chance <= 0) return false;
            if (chance >= 100) return true;
            return Generate(0, 99) < chance;
        }
    }
}
