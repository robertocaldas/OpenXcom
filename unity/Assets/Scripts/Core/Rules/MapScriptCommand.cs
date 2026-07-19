using System.Collections.Generic;

namespace OpenXcom.Core.Rules
{
    /// <summary>Port of MapScriptCommand (src/Mod/MapScript.h:47).</summary>
    public enum MapScriptCommandType
    {
        AddBlock, AddLine, AddCraft, AddUfo, DigTunnel, FillArea, CheckBlock, RemoveBlock, Resize,
    }

    /// <summary>Port of MapDirection (src/Mod/MapScript.h:30).</summary>
    public enum MapDirection { None, Vertical, Horizontal, Both }

    public sealed class MapScriptRect
    {
        public int X, Y, W, H;
    }

    /// <summary>
    /// One parsed terrain-script instruction. Port of MapScript's parsed
    /// field set (src/Mod/MapScript.h:170-250, load() at
    /// src/Mod/MapScript.cpp:54-296) — the data model only; execution lives
    /// in MapScriptInterpreter.
    /// </summary>
    public sealed class MapScriptCommand
    {
        public MapScriptCommandType Type;
        public List<MapScriptRect> Rects = new();
        public List<int> Groups = new();
        public List<int> Blocks = new();
        public List<int> Freqs = new();
        public List<int> MaxUses = new();
        public int SizeX = 1;
        public int SizeY = 1;
        public int SizeZ = 0;
        public MapDirection Direction = MapDirection.None;
        public int Executions = 1;
        public int ExecutionChances = 100;
        public int Label = 0;
        public List<int> Conditionals = new();
    }
}
