using System.Collections.Generic;
using OpenXcom.Core.Battle;
using OpenXcom.Core.Common;
using OpenXcom.Core.Rules;
using Xunit;

namespace OpenXcom.Core.Tests
{
	public class BlockPoolTests
	{
		[Fact]
		public void Pick_SingleEntryUnlimitedUses_AlwaysReturnsIt()
		{
			var pool = new BlockPool(ids: new[] { 5 }, freqs: new[] { 1 }, maxUses: new[] { -1 });
			var rng = new Rng(1);
			for (int i = 0; i < 10; i++)
				Assert.Equal(5, pool.Pick(rng));
		}

		[Fact]
		public void Pick_MaxUsesOne_SecondPickExcludesIt()
		{
			var pool = new BlockPool(ids: new[] { 1, 2 }, freqs: new[] { 1, 1 }, maxUses: new[] { 1, -1 });
			var rng = new Rng(1);
			var picks = new List<int>();
			for (int i = 0; i < 10; i++) picks.Add(pool.Pick(rng));

			Assert.Equal(1, picks.FindAll(p => p == 1).Count); // entry 1 used exactly once
			Assert.True(picks.FindAll(p => p == 2).Count >= 9);
		}

		[Fact]
		public void Pick_AllExhausted_ReturnsMinusOne()
		{
			var pool = new BlockPool(ids: new[] { 1 }, freqs: new[] { 1 }, maxUses: new[] { 1 });
			var rng = new Rng(1);
			Assert.Equal(1, pool.Pick(rng));
			Assert.Equal(-1, pool.Pick(rng));
		}

		[Fact]
		public void Pick_EmptyPool_ReturnsMinusOne()
		{
			var pool = new BlockPool(ids: System.Array.Empty<int>(), freqs: System.Array.Empty<int>(), maxUses: System.Array.Empty<int>());
			Assert.Equal(-1, pool.Pick(new Rng(1)));
		}

		[Fact]
		public void Pick_WeightedFrequencies_HigherFrequencyPickedMoreOften()
		{
			var pool = new BlockPool(ids: new[] { 1, 2 }, freqs: new[] { 99, 1 }, maxUses: new[] { -1, -1 });
			var rng = new Rng(1);
			int highCount = 0;
			for (int i = 0; i < 200; i++)
				if (pool.Pick(rng) == 1) highCount++;
			Assert.True(highCount > 150, $"Expected the 99-weight entry to dominate, got {highCount}/200.");
		}
	}

	public class MapScriptPlacementTests
	{
		[Fact]
		public void SelectPosition_EmptyGrid_FindsAllFittingSpots()
		{
			var occupied = new bool[3, 3];
			var rng = new Rng(1);

			bool found = MapScriptPlacement.SelectPosition(rng, occupied, System.Array.Empty<MapScriptRect>(),
				blockWidthTiles: 10, blockLengthTiles: 10, out int x, out int y);

			Assert.True(found);
			Assert.InRange(x, 0, 2);
			Assert.InRange(y, 0, 2);
		}

		[Fact]
		public void SelectPosition_FullyOccupiedGrid_ReturnsFalse()
		{
			var occupied = new bool[2, 2];
			for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) occupied[x, y] = true;

			bool found = MapScriptPlacement.SelectPosition(new Rng(1), occupied, System.Array.Empty<MapScriptRect>(),
				10, 10, out _, out _);

			Assert.False(found);
		}

		[Fact]
		public void SelectPosition_TwoCellBlock_NeverPicksAPositionThatWouldOverlapOccupiedCells()
		{
			// 3x1 grid, middle cell occupied - only a 1x1 block fits at x=0 or x=2.
			var occupied = new bool[3, 1];
			occupied[1, 0] = true;
			var rng = new Rng(1);

			for (int i = 0; i < 20; i++)
			{
				bool found = MapScriptPlacement.SelectPosition(rng, occupied, System.Array.Empty<MapScriptRect>(),
					10, 10, out int x, out int y);
				Assert.True(found);
				Assert.False(occupied[x, y]);
			}
		}

		[Fact]
		public void SelectPosition_RectConstrainsSearchArea()
		{
			var occupied = new bool[5, 5];
			var rects = new List<MapScriptRect> { new() { X = 3, Y = 3, W = 2, H = 2 } };
			var rng = new Rng(1);

			for (int i = 0; i < 20; i++)
			{
				bool found = MapScriptPlacement.SelectPosition(rng, occupied, rects, 10, 10, out int x, out int y);
				Assert.True(found);
				Assert.InRange(x, 3, 4);
				Assert.InRange(y, 3, 4);
			}
		}

		[Fact]
		public void SelectPosition_TwoByOneBlock_OnlyFitsWhereBothCellsAreFree()
		{
			// 2x1 grid, both free - a 20x10 (2x1 block-units) block must land at (0,0).
			var occupied = new bool[2, 1];
			bool found = MapScriptPlacement.SelectPosition(new Rng(1), occupied, System.Array.Empty<MapScriptRect>(),
				blockWidthTiles: 20, blockLengthTiles: 10, out int x, out int y);
			Assert.True(found);
			Assert.Equal(0, x);
			Assert.Equal(0, y);
		}
	}
}
