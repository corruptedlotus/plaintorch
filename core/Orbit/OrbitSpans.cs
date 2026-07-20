namespace Pleiades.Orbits;

// C# port of @pleiades/orbits spans.ts — lazy interval algebra over sorted, disjoint
// time spans. All streams are pure ms-interval logic; the engine supplies the raw span
// sources and the calendar arithmetic.

/// <summary>
/// A half-open interval in epoch milliseconds: start inclusive, end exclusive.
/// </summary>
public readonly record struct OrbitInterval(long Start, long End);

/// <summary>
/// A peekable stream of disjoint intervals in increasing start order. Streams may be
/// infinite; <see cref="Peek"/>/<see cref="Next"/> return null only when the source is
/// genuinely exhausted.
/// </summary>
public interface IOrbitSpanStream
{
	OrbitInterval? Peek();
	OrbitInterval? Next();
}

/// <summary>
/// Interval stream combinators.
/// </summary>
public static class OrbitSpanStreams
{
	private sealed class BufferedStream(Func<OrbitInterval?> build) : IOrbitSpanStream
	{
		private OrbitInterval? _slot;
		private bool _primed;

		public OrbitInterval? Peek()
		{
			if (!_primed)
			{
				_slot = build();
				_primed = true;
			}

			return _slot;
		}

		public OrbitInterval? Next()
		{
			var value = Peek();
			_slot = null;
			_primed = false;
			return value;
		}
	}

	/// <summary>Wraps a build function into a peekable stream with one-slot buffering.</summary>
	public static IOrbitSpanStream Buffered(Func<OrbitInterval?> build) => new BufferedStream(build);

	private static OrbitInterval Merge(OrbitInterval a, OrbitInterval b)
		=> new(Math.Min(a.Start, b.Start), Math.Max(a.End, b.End));

	/// <summary>
	/// Turns a raw producer of start-sorted (possibly overlapping) intervals into a
	/// disjoint stream, coalescing consecutive intervals that overlap or touch.
	/// </summary>
	public static IOrbitSpanStream Coalesce(Func<OrbitInterval?> raw)
	{
		var ahead = raw();
		return Buffered(() =>
		{
			if (ahead is null)
			{
				return null;
			}

			var current = ahead.Value;
			ahead = raw();
			var guard = 0;
			while (ahead is not null && ahead.Value.Start <= current.End && guard++ < 100_000)
			{
				if (ahead.Value.End > current.End)
				{
					current = current with { End = ahead.Value.End };
				}

				ahead = raw();
			}

			return current;
		});
	}

	/// <summary>Union: A ∪ B, with overlapping/touching intervals merged.</summary>
	public static IOrbitSpanStream Union(IOrbitSpanStream a, IOrbitSpanStream b)
	{
		OrbitInterval? TakeEarlier()
		{
			var pa = a.Peek();
			var pb = b.Peek();
			if (pa is null && pb is null)
			{
				return null;
			}

			if (pb is null)
			{
				return a.Next();
			}

			if (pa is null)
			{
				return b.Next();
			}

			return pa.Value.Start <= pb.Value.Start ? a.Next() : b.Next();
		}

		return Buffered(() =>
		{
			var current = TakeEarlier();
			if (current is null)
			{
				return null;
			}

			var value = current.Value;
			while (true)
			{
				var pa = a.Peek();
				var pb = b.Peek();
				if (pa is not null && pa.Value.Start <= value.End)
				{
					value = Merge(value, a.Next()!.Value);
					continue;
				}

				if (pb is not null && pb.Value.Start <= value.End)
				{
					value = Merge(value, b.Next()!.Value);
					continue;
				}

				break;
			}

			return value;
		});
	}

	/// <summary>Intersection: A ∩ B — only the overlapping portions.</summary>
	public static IOrbitSpanStream Intersect(IOrbitSpanStream a, IOrbitSpanStream b)
	{
		return Buffered(() =>
		{
			var guard = 0;
			while (guard++ < 1_000_000)
			{
				var pa = a.Peek();
				var pb = b.Peek();
				if (pa is null || pb is null)
				{
					return null;
				}

				var lo = Math.Max(pa.Value.Start, pb.Value.Start);
				var hi = Math.Min(pa.Value.End, pb.Value.End);
				if (pa.Value.End <= pb.Value.End)
				{
					a.Next(); // drop whichever ends first
				}
				else
				{
					b.Next();
				}

				if (lo < hi)
				{
					return new OrbitInterval(lo, hi);
				}
			}

			return null;
		});
	}

	/// <summary>Difference: A \ B — the parts of A not covered by B.</summary>
	public static IOrbitSpanStream Difference(IOrbitSpanStream a, IOrbitSpanStream b)
	{
		OrbitInterval? currentA = null;
		return Buffered(() =>
		{
			var guard = 0;
			while (guard++ < 1_000_000)
			{
				if (currentA is null)
				{
					currentA = a.Next();
					if (currentA is null)
					{
						return null;
					}
				}

				while (b.Peek() is { } dropped && dropped.End <= currentA.Value.Start)
				{
					b.Next(); // drop B fully before A
				}

				var bx = b.Peek();
				if (bx is null || bx.Value.Start >= currentA.Value.End)
				{
					var output = currentA.Value;
					currentA = null;
					return output;
				}

				if (bx.Value.Start > currentA.Value.Start)
				{
					// Emit the gap before B, then keep processing A from B's start.
					var output = new OrbitInterval(currentA.Value.Start, bx.Value.Start);
					currentA = new OrbitInterval(bx.Value.Start, currentA.Value.End);
					return output;
				}

				// B covers A from its start.
				if (bx.Value.End >= currentA.Value.End)
				{
					currentA = null; // B covers the rest; keep B for the next A
					continue;
				}

				currentA = new OrbitInterval(bx.Value.End, currentA.Value.End); // B covers a prefix; consume it
				b.Next();
			}

			return null;
		});
	}
}
