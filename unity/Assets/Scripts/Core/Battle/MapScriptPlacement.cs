using System.Collections.Generic;
using OpenXcom.Core.Common;
using OpenXcom.Core.Rules;

namespace OpenXcom.Core.Battle
{
	/// <summary>
	/// A weighted, uses-limited random pool of block indices or group ids.
	/// Port of MapScript::getBlockNumber/getGroupNumber
	/// (src/Mod/MapScript.cpp:345-410): each Pick rolls within the pool's
	/// cumulative frequency, returns the matching entry, and decrements that
	/// entry's remaining uses - dropping it from the pool once exhausted.
	/// Constructed once per command (matches MapScript::init(), called once
	/// per command before any of its `executions` repeats) and reused across
	/// every pick that command makes.
	/// </summary>
	public sealed class BlockPool
	{
		private readonly List<int> _ids;
		private readonly List<int> _freqs;
		private readonly List<int> _maxUses;
		private int _cumulativeFrequency;

		public BlockPool(IReadOnlyList<int> ids, IReadOnlyList<int> freqs, IReadOnlyList<int> maxUses)
		{
			_ids = new List<int>(ids);
			_freqs = new List<int>(freqs);
			_maxUses = new List<int>(maxUses);
			_cumulativeFrequency = 0;
			foreach (var f in _freqs) _cumulativeFrequency += f;
		}

		public int Pick(Rng rng)
		{
			if (_cumulativeFrequency <= 0) return -1;

			int pick = rng.Generate(0, _cumulativeFrequency - 1);
			for (int i = 0; i < _ids.Count; i++)
			{
				if (pick < _freqs[i])
				{
					int result = _ids[i];
					if (_maxUses[i] > 0 && --_maxUses[i] == 0)
					{
						_ids.RemoveAt(i);
						_cumulativeFrequency -= _freqs[i];
						_freqs.RemoveAt(i);
						_maxUses.RemoveAt(i);
					}
					return result;
				}
				pick -= _freqs[i];
			}
			return -1;
		}
	}

	/// <summary>
	/// Grid-fitting primitive for terrain-script placement. Port of
	/// BattlescapeGenerator::selectPosition (src/Battlescape/BattlescapeGenerator.cpp:4204-4262):
	/// every position (in 10-tile block units) within `rects` (or the whole
	/// map, if `rects` is empty) that a block of the given tile size fits
	/// into without overlapping an already-occupied cell, one picked at
	/// random. `occupied` is indexed in the same block units as the result.
	/// </summary>
	public static class MapScriptPlacement
	{
		public static bool SelectPosition(
			Rng rng, bool[,] occupied, IReadOnlyList<MapScriptRect> rects,
			int blockWidthTiles, int blockLengthTiles, out int x, out int y)
		{
			int mapW = occupied.GetLength(0);
			int mapH = occupied.GetLength(1);
			int sizeX = blockWidthTiles / 10;
			int sizeY = blockLengthTiles / 10;

			IReadOnlyList<MapScriptRect> areas = rects.Count > 0
				? rects
				: new[] { new MapScriptRect { X = 0, Y = 0, W = mapW, H = mapH } };

			var valid = new List<(int x, int y)>();
			foreach (var rect in areas)
			{
				if (sizeX > rect.W || sizeY > rect.H) continue;

				for (int rx = rect.X; rx + sizeX <= rect.X + rect.W && rx + sizeX <= mapW; rx++)
				{
					for (int ry = rect.Y; ry + sizeY <= rect.Y + rect.H && ry + sizeY <= mapH; ry++)
					{
						if (valid.Contains((rx, ry))) continue;

						bool free = true;
						for (int xc = rx; xc < rx + sizeX && free; xc++)
							for (int yc = ry; yc < ry + sizeY && free; yc++)
								if (occupied[xc, yc]) free = false;

						if (free) valid.Add((rx, ry));
					}
				}
			}

			if (valid.Count == 0) { x = -1; y = -1; return false; }

			var selection = valid[rng.Generate(0, valid.Count - 1)];
			x = selection.x;
			y = selection.y;
			return true;
		}
	}
}
