using System.Collections.Generic;
using System.Linq;
using OpenXcom.Core.Common;

namespace OpenXcom.Core.Rules
{
    public sealed class MapDataSetInfo
    {
        public string Name;
        public int Size;
    }

    public sealed class MapBlockInfo
    {
        public string Name;
        public int Width;
        public int Length;
        public IReadOnlyList<int> Groups;
    }

    /// <summary>
    /// A terrain's ordered list of MCD datasets, plus (Phase 10) its full
    /// mapblock catalog and default script name. Port of
    /// RuleTerrain::getMapData (src/Mod/RuleTerrain.cpp:238-260): a raw,
    /// nonzero .MAP tile-part index resolves by walking the dataset list,
    /// subtracting each dataset's size, until it fits within one dataset.
    /// </summary>
    public sealed class RuleTerrain
    {
        public string Name { get; }
        public IReadOnlyList<MapDataSetInfo> DataSets { get; }
        public IReadOnlyList<MapBlockInfo> Blocks { get; }
        public string Script { get; }

        public RuleTerrain(string name, IReadOnlyList<MapDataSetInfo> dataSets,
            IReadOnlyList<MapBlockInfo> blocks = null, string script = null)
        {
            Name = name;
            DataSets = dataSets;
            Blocks = blocks ?? System.Array.Empty<MapBlockInfo>();
            Script = script ?? "";
        }

        public (string DatasetName, int LocalIndex) Resolve(int rawIndex)
        {
            int id = rawIndex;
            foreach (var ds in DataSets)
            {
                if (id < ds.Size)
                    return (ds.Name, id);
                id -= ds.Size;
            }
            // Corrupt/out-of-range reference: fall back to the first
            // dataset's record 0 (matches the C++'s "BLANKS 0" fallback).
            return (DataSets[0].Name, 0);
        }

        /// <summary>
        /// Port of RuleTerrain::getRandomMapBlock (src/Mod/RuleTerrain.cpp:195-216):
        /// among this terrain's blocks tagged with `group` whose size fits
        /// within the given cap (or matches exactly, when force is true),
        /// picks one at random. Null if none qualify.
        /// </summary>
        public MapBlockInfo PickRandomBlock(Rng rng, int maxWidthTiles, int maxLengthTiles, int group, bool force = false)
        {
            var compliant = new List<MapBlockInfo>();
            foreach (var b in Blocks)
            {
                bool widthOk = b.Width == maxWidthTiles || (!force && b.Width < maxWidthTiles);
                bool lengthOk = b.Length == maxLengthTiles || (!force && b.Length < maxLengthTiles);
                if (widthOk && lengthOk && b.Groups.Contains(group))
                    compliant.Add(b);
            }
            if (compliant.Count == 0) return null;
            return compliant[rng.Generate(0, compliant.Count - 1)];
        }
    }
}
