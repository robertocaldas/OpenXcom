using System;

namespace OpenXcom.Core.Common
{
    /// <summary>
    /// Integer tile coordinate on the battlefield. z is the vertical level.
    /// Mirrors OXCE's <c>Position</c> (src/Battlescape/Position.h) but Unity-agnostic.
    /// </summary>
    public readonly struct Position : IEquatable<Position>
    {
        public readonly int X;
        public readonly int Y;
        public readonly int Z;

        public Position(int x, int y, int z = 0)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static Position operator +(Position a, Position b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Position operator -(Position a, Position b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static bool operator ==(Position a, Position b) => a.Equals(b);
        public static bool operator !=(Position a, Position b) => !a.Equals(b);

        /// <summary>Chebyshev (king-move) distance on the same level, ignoring z.</summary>
        public int ChebyshevDistance(Position o) => Math.Max(Math.Abs(X - o.X), Math.Abs(Y - o.Y));

        /// <summary>Euclidean tile distance on the same level (used for accuracy drop-off).</summary>
        public double Distance(Position o)
        {
            int dx = X - o.X, dy = Y - o.Y, dz = Z - o.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        public bool Equals(Position other) => X == other.X && Y == other.Y && Z == other.Z;
        public override bool Equals(object obj) => obj is Position p && Equals(p);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        public override string ToString() => $"({X},{Y},{Z})";
    }

    /// <summary>8 compass directions, 0 = north, clockwise. Matches OXCE direction indexing.</summary>
    public static class Directions
    {
        // N, NE, E, SE, S, SW, W, NW
        public static readonly Position[] Offsets =
        {
            new(0, -1), new(1, -1), new(1, 0), new(1, 1),
            new(0, 1), new(-1, 1), new(-1, 0), new(-1, -1),
        };

        public static bool IsDiagonal(int dir) => (dir & 1) == 1;

        /// <summary>Reverse lookup of Offsets: which of the 8 compass indices
        /// this delta is, or -1 if it isn't one of them. Used by path-preview
        /// rendering (Phase 7) to pick an arrow frame from a PathStep-to-
        /// PathStep delta.</summary>
        public static int IndexOf(Position delta)
        {
            for (int i = 0; i < Offsets.Length; i++)
                if (Offsets[i] == delta)
                    return i;
            return -1;
        }
    }
}
