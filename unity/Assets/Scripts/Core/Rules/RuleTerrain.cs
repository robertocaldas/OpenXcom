using System.Collections.Generic;

namespace OpenXcom.Core.Rules
{
    public sealed class MapDataSetInfo
    {
        public string Name;
        public int Size;
    }

    /// <summary>
    /// A terrain's ordered list of MCD datasets. Port of
    /// RuleTerrain::getMapData (src/Mod/RuleTerrain.cpp:238-260): a raw,
    /// nonzero .MAP tile-part index resolves by walking the dataset list,
    /// subtracting each dataset's size, until it fits within one dataset.
    /// </summary>
    public sealed class RuleTerrain
    {
        public string Name { get; }
        public IReadOnlyList<MapDataSetInfo> DataSets { get; }

        public RuleTerrain(string name, IReadOnlyList<MapDataSetInfo> dataSets)
        {
            Name = name;
            DataSets = dataSets;
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
    }
}
