namespace Pleiades.Orbits;

// C# port of @pleiades/orbits engine.ts. The resolution logic, structural limits, and
// seeded randomness are ported statement-for-statement so both engines emit identical
// streams for the same (notation, epoch, seed).

/// <summary>
/// An emitted orbit entry: granular resolution or explicit span.
/// </summary>
public abstract record OrbitEntry;

/// <summary>
/// Granular resolution: an instant plus the unit window it stands for.
/// </summary>
public sealed record OrbitResolutionEntry(long TimestampMs, OrbitUnit Granularity) : OrbitEntry;

/// <summary>
/// Span resolution: an explicit [start, end) interval. Produced only by span-format
/// schedules (those using a <c>=&lt;dur&gt;</c> duration); never mixed with granular entries.
/// <see cref="Granularity"/> is the schedule's finest unit — the window the start sits in —
/// so a consumer can distinguish an anchored span (granularity at or finer than the duration)
/// from a floating one (granularity window larger than the duration, e.g. <c>d=2h</c>).
/// </summary>
public sealed record OrbitSpanEntry(long StartMs, long EndMs, OrbitUnit Granularity) : OrbitEntry
{
	public long DurationMs => EndMs - StartMs;
}

/// <summary>
/// The stateful orbit resolution engine.
/// </summary>
/// <remarks>
/// How the limits read (the notation's contract, resolved structurally by the solver):
/// <list type="bullet">
/// <item>A node's <b>life</b> is one period of its written parent — each parent period gives it a fresh life. A
/// top-level node (the root, or an operand of a top-level set operation) has a single life, from its first complete
/// period at or after the stream start (the anchor, snapped to the schedule's granularity and advanced past any
/// <c>&gt;t</c>). The period the stream starts in is complete unless one of the node's instances in it falls before
/// the start; an incomplete one is skipped whole, so a schedule never begins partway through one of its own
/// periods.</item>
/// <item><c>*x</c> limits a node's <b>repetition</b>. A bare unit is a single run from the start of its life, one
/// value after another (or every N-th under a continuous <c>%N</c>); an index is a run of one value, unless <c>%N</c>
/// steps it (b, b+N, … to the end of the parent period). Each run keeps its first x values — so <c>d*5</c> is five
/// days, <c>w[d*3]</c> the first three days of every week, <c>d{5}%3*4</c> the 5th, 8th, 11th and 14th, and <c>*x</c>
/// on an index <c>%</c> does not step (a list or a range) does nothing. A repetition counts whether or not the node's
/// children fire in it.</item>
/// <item><c>@x</c> limits a node's <b>emission</b>: its own instances (what its subtree produces, before any enclosing
/// set operation acts on them) are counted per life in time order, and only the first x fire — so
/// <c>M[w[d{1}%2]@7]</c> fires up to seven times a month, and a top-level <c>d{2,4,6,8}@2</c> fires twice in all.</item>
/// <item>A week inside a month (written, or implied by a top-level week index) or inside a written year (a calendar
/// week) is numbered from that cycle's first complete week: the week the cycle starts in belongs to it only when every
/// instance the week node asks for falls inside the cycle, so <c>M[w{1}[d{1}]]</c> is the first Monday of every month
/// and <c>y[w{1}[d{1}]]</c> the first of every year. The weeks run to the one the cycle ends in, whose days past its end
/// are cut off. A bare top-level week (<c>w[d{1,5}]</c>) belongs to no cycle: it runs week after week across months. A
/// day inside a written year is its day of the year (<c>y[d{100}]</c>).</item>
/// <item>A set operation may sit inside a chain (<c>M[d{15}+d{4}%4]</c>); its operands share the node above as their
/// parent. A span schedule lifts it to the top — <c>P[A op B]</c> reads as <c>P[A] op P[B]</c> — so its interval
/// algebra runs exactly as it would on top-level operands.</item>
/// </list>
/// Every limit is a pure function of the notation, epoch and seed: the engine keeps no running counters, a snapshot
/// is its cursor, and a preview seeks straight to its window.
/// </remarks>
public sealed class OrbitEngine
{
	// Stream-global boundary limits, hoisted out of the AST once at construction time.
	// (@x/*x are NOT here — they belong to their node and are resolved inside the solver.)
	private readonly record struct ResolvedLimit(OrbitLimitKind Kind, long At, OrbitUnit Unit);

	// What the engine knows about one written time-unit node (never a synthetic wrapper): the unit of its written
	// parent, or null for a top-level node, and its tightest limits (Repeat is *x, Emit is @x).
	private sealed record NodeInfo(OrbitUnit? Parent, int? Repeat, int? Emit);

	private sealed class Cursor(long ms)
	{
		public long Ms = ms;
	}

	private readonly OrbitAstNode _ast;
	private readonly IOrbitCalendar _calendar;
	private readonly long _anchor;
	private long _cursor;
	private readonly OrbitUnit _globalGranularity;

	private readonly List<ResolvedLimit> _limits;
	private int _emittedCount;
	private bool _exhausted;

	// RNG seed for {#n} randoms. Auto-generated when not supplied so two schedules
	// from the same notation differ; carried in the snapshot for reproducibility.
	private readonly uint _seed;
	// The orbit notation this engine was built from, when known (via FromNotation).
	private string? _notation;
	// The most recently emitted match, used as the on-phase epoch when promoting.
	private long? _lastEmitted;
	// True for an engine resumed from a promoted snapshot: its stream has already started, so no top-level life is
	// skipped at the (re-anchored) epoch.
	private bool _continuing;

	// Caches: index bases for list/range nodes (context-independent) and memoized
	// granularity per node. Random bases are context-dependent and computed live.
	private readonly Dictionary<OrbitTimeUnitNode, int[]> _baseCache = [];
	private readonly Dictionary<OrbitAstNode, OrbitUnit> _granMemo = [];

	// --- Structural limits ---
	// Every written node, with its written parent and limits.
	private readonly Dictionary<OrbitTimeUnitNode, NodeInfo> _nodes = [];
	// A top-level node's outermost synthetic wrapper (the node itself for a year), so its instances can be enumerated
	// on their own.
	private readonly Dictionary<OrbitTimeUnitNode, OrbitTimeUnitNode> _wrapperRoots = [];
	// Pure caches, each a function of (notation, epoch, seed): where a top-level node's life starts, a node's @x
	// cutoff per life, and a week-in-month node's offset per month.
	private readonly Dictionary<OrbitTimeUnitNode, long> _lifeStarts = [];
	private readonly Dictionary<OrbitTimeUnitNode, Dictionary<long, long>> _cutoffs = [];
	private readonly Dictionary<OrbitTimeUnitNode, Dictionary<long, int>> _weekOffsets = [];
	// Nodes whose own limits are set aside while the solver works out where a life starts (all of them) or where an
	// @x cuts off (the emission only), since both are defined by the node's instances without those limits.
	private readonly HashSet<OrbitTimeUnitNode> _lifeSetAside = [];
	private readonly HashSet<OrbitTimeUnitNode> _emitSetAside = [];

	// --- Span format ---
	private readonly bool _spanFormat;
	private readonly OrbitAstNode _rawAst;
	private readonly IOrbitSpanStream? _spanRoot;

	/// <summary>
	/// Builds an engine over a parsed AST. <paramref name="granularOnly"/> forces granular
	/// resolution even when durations are present; the span resolver uses it for the
	/// per-operand sub-engines that enumerate starts.
	/// </summary>
	public OrbitEngine(OrbitAstNode ast, long anchorMs, IOrbitCalendar calendar, uint? seed = null, bool granularOnly = false)
	{
		ArgumentNullException.ThrowIfNull(ast);
		_calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
		// Desugar z/Z datetime literals into their equivalent nested time-unit chain up
		// front, so every downstream pass (limits, normalize, span-building) only ever sees
		// OrbitTimeUnitNode / OrbitSetOperationNode and needs no literal awareness.
		var source = ExpandLiterals(ast);
		// Record each node's written parent BEFORE normalization adds synthetic y>M>...
		// wrappers, then normalize.
		IndexNodes(source, null);
		_ast = NormalizeAst(source);

		_anchor = anchorMs;
		_seed = seed ?? (uint)Random.Shared.NextInt64(0x1_0000_0000L);
		_globalGranularity = CalcGranularity(_ast);
		_limits = CollectLimits(_ast);
		_cursor = SeededStart();

		_spanFormat = !granularOnly && HasDuration(source);
		_rawAst = _spanFormat ? LiftSets(source) : source;
		if (_spanFormat)
		{
			ValidateSpanFormat();
			_spanRoot = BuildStream(_rawAst);
		}
	}

	/// <summary>The engine's RNG seed.</summary>
	public uint Seed => _seed;

	// The epoch-snapped search origin, advanced past any '>t' (after) lower bound so
	// we don't waste iterations solving-then-rejecting everything before it.
	private long SeededStart()
	{
		var start = _calendar.SnapToStart(_anchor, _globalGranularity);
		foreach (var limit in _limits)
		{
			if (limit.Kind == OrbitLimitKind.After && limit.At > start)
			{
				start = _calendar.SnapToStart(limit.At, _globalGranularity);
			}
		}

		return start;
	}

	// --- Construction / persistence ---

	/// <summary>
	/// Builds an engine directly from orbit notation. The engine remembers the notation
	/// so <see cref="Serialize"/>/<see cref="Promote"/> can produce a self-contained snapshot.
	/// </summary>
	public static OrbitEngine FromNotation(string notation, long epochMs, IOrbitCalendar calendar, uint? seed = null)
	{
		var ast = new OrbitParser(notation).Parse();
		var engine = new OrbitEngine(ast, epochMs, calendar, seed)
		{
			_notation = notation,
		};
		return engine;
	}

	/// <summary>
	/// Rehydrates an engine from a snapshot, restoring progress exactly.
	/// </summary>
	public static OrbitEngine Resume(OrbitSnapshot snapshot, IOrbitCalendar calendar)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		if (snapshot.Version != 1)
		{
			throw new NotSupportedException($"Unsupported OrbitSnapshot version: {snapshot.Version}");
		}

		if (!JsDate.TryParseIso(snapshot.Epoch, out var epochMs))
		{
			throw new FormatException($"Orbit snapshot epoch '{snapshot.Epoch}' is not a valid instant.");
		}

		var engine = FromNotation(snapshot.Notation, epochMs, calendar, (uint)snapshot.Seed);

		if (snapshot.Cursor is not null)
		{
			// Full snapshot: restore the exact search origin. This overrides the
			// constructor's '>t' seeding, which the stored cursor already reflects.
			if (!JsDate.TryParseIso(snapshot.Cursor, out var cursorMs))
			{
				throw new FormatException($"Orbit snapshot cursor '{snapshot.Cursor}' is not a valid instant.");
			}

			engine._cursor = cursorMs;
			engine._emittedCount = snapshot.EmittedCount ?? 0;
			// A snapshot carrying running counters came from an engine that still counted @x/*x as it went, and may
			// have ended a stream those limits no longer end: its exhaustion is re-derived instead.
			engine._exhausted = snapshot.Counters is null && (snapshot.Exhausted ?? false);
		}
		else
		{
			// Promoted snapshot: epoch IS the last emitted (on-phase) entry, so we
			// resume strictly after it to avoid re-emitting the consumed entry.
			engine._continuing = true;
			engine._cursor = engine._calendar.Add(epochMs, engine._globalGranularity, 1);
		}

		return engine;
	}

	/// <summary>
	/// True iff the schedule can be re-anchored onto any emitted entry without changing
	/// the stream. A top-level <c>@x</c> or <c>*x</c> counts from the start of the node's
	/// life, which a re-anchored epoch would move, so either blocks it; a nested one counts
	/// within its parent's periods and does not.
	/// </summary>
	public bool IsAnchorStable()
	{
		if (_spanFormat)
		{
			return false; // span schedules are not epoch-promotable
		}

		foreach (var info in _nodes.Values)
		{
			if (info.Parent is null && (info.Repeat is not null || info.Emit is not null))
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// Full, always-correct snapshot. Requires a known notation (pass one if this engine
	/// was built from a bare AST rather than <see cref="FromNotation"/>).
	/// </summary>
	public OrbitSnapshot Serialize(string? notation = null)
	{
		if (_spanFormat)
		{
			throw new NotSupportedException("Serialize() is not yet supported for span-format schedules.");
		}

		var n = notation ?? _notation
			?? throw new InvalidOperationException("Serialize() needs the notation: build via OrbitEngine.FromNotation() or pass it explicitly.");
		return new OrbitSnapshot
		{
			Version = 1,
			Notation = n,
			Epoch = JsDate.ToIsoString(_anchor),
			Seed = _seed,
			Cursor = JsDate.ToIsoString(_cursor),
			EmittedCount = _emittedCount,
			Exhausted = _exhausted,
		};
	}

	/// <summary>
	/// Compact snapshot that rebases the epoch onto the last emitted entry and drops
	/// cursor/count. Only valid for anchor-stable schedules (no top-level @ / *), and only
	/// after at least one entry has been emitted.
	/// </summary>
	public OrbitSnapshot Promote(string? notation = null)
	{
		if (!IsAnchorStable())
		{
			throw new InvalidOperationException("Promote() is invalid for schedules with a top-level @ or * limit; use Serialize().");
		}

		if (_lastEmitted is null)
		{
			throw new InvalidOperationException("Promote() requires at least one emitted entry.");
		}

		var n = notation ?? _notation
			?? throw new InvalidOperationException("Promote() needs the notation: build via OrbitEngine.FromNotation() or pass it explicitly.");
		return new OrbitSnapshot
		{
			Version = 1,
			Notation = n,
			Epoch = JsDate.ToIsoString(_lastEmitted.Value),
			Seed = _seed,
		};
	}

	// --- Windowed resolution utilities ---
	//
	// The `Next*` variants MUTATE: they hop the live engine forward from its current
	// position and leave it advanced. Use them to actually move through a schedule.
	//
	// The `Resolve*` variants are NON-mutating: they run the same logic inside a
	// temporary traversal seeked straight to `start` (every limit is structural, so no
	// replay from the epoch is needed) and restore the live state, so a query leaves no
	// trace and reflects the schedule as defined from the epoch.

	/// <summary>
	/// MUTATING. Advances to <paramref name="startMs"/> (consuming earlier entries) then
	/// returns every entry whose start is in the half-open window [start, end); leaves the
	/// engine positioned at the window end.
	/// </summary>
	public List<OrbitEntry> NextWithin(long startMs, long endMs)
	{
		var output = new List<OrbitEntry>();
		if (_spanFormat)
		{
			while (_spanRoot!.Peek() is { } peeked && peeked.Start < endMs)
			{
				var interval = _spanRoot.Next()!.Value;
				if (interval.Start >= startMs)
				{
					output.Add(new OrbitSpanEntry(interval.Start, interval.End, _globalGranularity));
				}
			}

			return output;
		}

		// Advance() stops before consuming the first entry at/after `end`.
		while (Advance(endMs) is { } resolution)
		{
			if (resolution.TimestampMs >= startMs)
			{
				output.Add(resolution);
			}
		}

		return output;
	}

	/// <summary>
	/// MUTATING. Advances to <paramref name="startMs"/> (consuming earlier entries) then
	/// returns the next <paramref name="count"/> entries at or after it.
	/// </summary>
	public List<OrbitEntry> NextFrom(long startMs, int count)
	{
		var output = new List<OrbitEntry>();
		if (count <= 0)
		{
			return output;
		}

		if (_spanFormat)
		{
			while (output.Count < count && _spanRoot!.Next() is { } interval)
			{
				if (interval.Start >= startMs)
				{
					output.Add(new OrbitSpanEntry(interval.Start, interval.End, _globalGranularity));
				}
			}

			return output;
		}

		while (output.Count < count && Advance(long.MaxValue) is { } resolution)
		{
			if (resolution.TimestampMs >= startMs)
			{
				output.Add(resolution);
			}
		}

		return output;
	}

	/// <summary>
	/// NON-mutating. Every entry whose start is in [start, end), as defined from the epoch.
	/// </summary>
	public List<OrbitEntry> ResolveWithin(long startMs, long endMs)
	{
		if (_spanFormat)
		{
			var stream = BuildStream(_rawAst);
			var output = new List<OrbitEntry>();
			while (stream.Peek() is { } peeked && peeked.Start < endMs)
			{
				var interval = stream.Next()!.Value;
				if (interval.Start >= startMs)
				{
					output.Add(new OrbitSpanEntry(interval.Start, interval.End, _globalGranularity));
				}
			}

			return output;
		}

		return Query(startMs, () => NextWithin(startMs, endMs));
	}

	/// <summary>
	/// NON-mutating. The first <paramref name="count"/> entries at or after
	/// <paramref name="startMs"/>, as defined from the epoch.
	/// </summary>
	public List<OrbitEntry> ResolveFrom(long startMs, int count)
	{
		if (_spanFormat)
		{
			var output = new List<OrbitEntry>();
			if (count <= 0)
			{
				return output;
			}

			var stream = BuildStream(_rawAst);
			while (output.Count < count && stream.Next() is { } interval)
			{
				if (interval.Start >= startMs)
				{
					output.Add(new OrbitSpanEntry(interval.Start, interval.End, _globalGranularity));
				}
			}

			return output;
		}

		return Query(startMs, () => NextFrom(startMs, count));
	}

	// Runs `fn` over a temporary traversal beginning at `start`, then restores this
	// engine's live state so the query leaves no trace.
	private T Query<T>(long startMs, Func<T> fn)
	{
		var savedCursor = _cursor;
		var savedEmittedCount = _emittedCount;
		var savedExhausted = _exhausted;
		var savedLastEmitted = _lastEmitted;

		_emittedCount = 0;
		_exhausted = false;
		_lastEmitted = null;
		// The stream is history-independent, so jump straight to `start` (never before the epoch).
		_cursor = _calendar.SnapToStart(Math.Max(startMs, _anchor), _globalGranularity);

		try
		{
			return fn();
		}
		finally
		{
			_cursor = savedCursor;
			_emittedCount = savedEmittedCount;
			_exhausted = savedExhausted;
			_lastEmitted = savedLastEmitted;
		}
	}

	// Desugars every z/Z datetime literal in a tree into the equivalent nested time-unit
	// chain, leaving OrbitTimeUnitNode / OrbitSetOperationNode subtrees untouched.
	private OrbitAstNode ExpandLiterals(OrbitAstNode node)
	{
		if (node is OrbitSetOperationNode set)
		{
			return new OrbitSetOperationNode
			{
				Operator = set.Operator,
				Left = ExpandLiterals(set.Left),
				Right = ExpandLiterals(set.Right),
			};
		}

		if (node is OrbitDateTimeLiteralNode literal)
		{
			return ExpandDateTimeLiteral(literal);
		}

		var unit = (OrbitTimeUnitNode)node;
		if (unit.Child is not null)
		{
			unit.Child = ExpandLiterals(unit.Child);
		}

		return unit;
	}

	// A datetime literal becomes a fully-pinned singleton chain from its coarsest present
	// component down to its finest — Z{2027/6/5T18:00} -> y{2027}[M{6}[d{5}[h{18}[m{0}]]]],
	// z{12:00} -> h{12}[m{0}]. The deepest component is the leaf, so CalcGranularity reads
	// the intended granularity for free. Modifiers ride on the chain root.
	private static OrbitTimeUnitNode ExpandDateTimeLiteral(OrbitDateTimeLiteralNode literal)
	{
		var parts = new List<(OrbitUnit Unit, int Value)>();
		if (literal.Year is { } year)
		{
			parts.Add((OrbitUnit.Year, year));
		}

		if (literal.Month is { } month)
		{
			parts.Add((OrbitUnit.Month, month));
		}

		if (literal.Day is { } day)
		{
			parts.Add((OrbitUnit.Day, day));
		}

		if (literal.Hour is { } hour)
		{
			parts.Add((OrbitUnit.Hour, hour));
		}

		if (literal.Minute is { } minute)
		{
			parts.Add((OrbitUnit.Minute, minute));
		}

		if (literal.Second is { } second)
		{
			parts.Add((OrbitUnit.Second, second));
		}

		if (parts.Count == 0)
		{
			throw new FormatException("A datetime literal must carry at least one component.");
		}

		OrbitTimeUnitNode? chain = null;
		for (var i = parts.Count - 1; i >= 0; i--)
		{
			var (unit, value) = parts[i];
			chain = new OrbitTimeUnitNode
			{
				Unit = unit,
				Indices = new OrbitIndexSpec { Kind = OrbitIndexKind.List, Values = [value] },
				Child = chain,
			};
		}

		var root = chain!;
		root.Interval = literal.Interval;
		if (literal.Limits.Count > 0)
		{
			root.Limits.AddRange(literal.Limits);
		}

		root.Duration = literal.Duration;
		return root;
	}

	private OrbitAstNode NormalizeAst(OrbitAstNode node)
	{
		if (node is OrbitSetOperationNode set)
		{
			return new OrbitSetOperationNode
			{
				Operator = set.Operator,
				Left = NormalizeAst(set.Left),
				Right = NormalizeAst(set.Right),
			};
		}

		static OrbitUnit? Parent(OrbitUnit unit) => unit switch
		{
			OrbitUnit.Year => null,
			OrbitUnit.Month => OrbitUnit.Year,
			OrbitUnit.Week => OrbitUnit.Month,
			OrbitUnit.Day => OrbitUnit.Month,
			OrbitUnit.Hour => OrbitUnit.Day,
			OrbitUnit.Minute => OrbitUnit.Hour,
			OrbitUnit.Second => OrbitUnit.Minute,
			_ => null,
		};

		var written = (OrbitTimeUnitNode)node;
		var current = written;
		while (Parent(current.Unit) is { } parentUnit)
		{
			current = new OrbitTimeUnitNode
			{
				Unit = parentUnit,
				Child = current,
			};
		}

		_wrapperRoots[written] = current;
		return current;
	}

	/// <summary>
	/// Resolves and consumes the next entry, mutating the engine.
	/// </summary>
	public OrbitEntry? Next()
	{
		if (_spanFormat)
		{
			return SpanNext();
		}

		return Advance(long.MaxValue);
	}

	private OrbitSpanEntry? SpanNext()
	{
		if (_exhausted)
		{
			return null;
		}

		var interval = _spanRoot!.Next();
		if (interval is null)
		{
			_exhausted = true;
			return null;
		}

		return new OrbitSpanEntry(interval.Value.Start, interval.Value.End, _globalGranularity);
	}

	// --- Span construction (interval algebra) ---

	// Builds a lazy interval stream for a subtree. Top-level set operations become
	// interval combinators; each base operand becomes a granular sub-engine that
	// enumerates occurrence starts, with the duration attached and overlaps coalesced.
	private IOrbitSpanStream BuildStream(OrbitAstNode node)
	{
		if (node is OrbitSetOperationNode set)
		{
			return set.Operator switch
			{
				OrbitSetOperator.Union => OrbitSpanStreams.Union(BuildStream(set.Left), BuildStream(set.Right)),
				OrbitSetOperator.Intersection => OrbitSpanStreams.Intersect(BuildStream(set.Left), BuildStream(set.Right)),
				OrbitSetOperator.Exclusion => OrbitSpanStreams.Difference(BuildStream(set.Left), BuildStream(set.Right)),
				OrbitSetOperator.SymmetricDifference => OrbitSpanStreams.Union(
					OrbitSpanStreams.Difference(BuildStream(set.Left), BuildStream(set.Right)),
					OrbitSpanStreams.Difference(BuildStream(set.Right), BuildStream(set.Left))),
				_ => throw new InvalidOperationException("Unsupported set operator."),
			};
		}

		var parts = SpineDuration((OrbitTimeUnitNode)node);
		// A granular sub-engine resolves this operand's occurrence starts (reusing
		// indices, intervals, randoms, and @x/*x/<>t limits); the duration is layered on.
		var inner = new OrbitEngine(node, _anchor, _calendar, _seed, granularOnly: true);
		OrbitInterval? Raw()
		{
			if (inner.Next() is not OrbitResolutionEntry resolution)
			{
				return null;
			}

			var start = resolution.TimestampMs;
			return new OrbitInterval(start, ApplyDuration(start, parts));
		}

		return OrbitSpanStreams.Coalesce(Raw);
	}

	// A span schedule's interval algebra runs over whole operands, so a set operation nested in a chain is lifted to the
	// top: P[A op B] becomes P[A] op P[B], the chain above repeated in each operand, exactly as it would be written at the
	// top level. A *x or @x above such a set would count the set's combined instances, which no top-level operand can
	// say, so it is refused.
	private static OrbitAstNode LiftSets(OrbitAstNode node)
	{
		if (node is OrbitSetOperationNode set)
		{
			return new OrbitSetOperationNode
			{
				Operator = set.Operator,
				Left = LiftSets(set.Left),
				Right = LiftSets(set.Right),
			};
		}

		if (node is not OrbitTimeUnitNode { Child: not null } unitNode)
		{
			return node;
		}

		var child = LiftSets(unitNode.Child);
		if (child is not OrbitSetOperationNode lifted)
		{
			return ReferenceEquals(child, unitNode.Child) ? unitNode : WithChild(unitNode, child);
		}

		if (unitNode.Limits.Any(limit => limit.Kind is OrbitLimitKind.Iterations or OrbitLimitKind.Instances))
		{
			throw new FormatException("A span set operation cannot sit under a node with a * or @ limit; write it at the top level.");
		}

		return new OrbitSetOperationNode
		{
			Operator = lifted.Operator,
			Left = LiftSets(WithChild(unitNode, lifted.Left)),
			Right = LiftSets(WithChild(unitNode, lifted.Right)),
		};
	}

	// A copy of a time-unit node with another child, its own index and modifiers kept.
	private static OrbitTimeUnitNode WithChild(OrbitTimeUnitNode node, OrbitAstNode child)
	{
		var copy = new OrbitTimeUnitNode
		{
			Unit = node.Unit,
			Indices = node.Indices,
			Child = child,
			Interval = node.Interval,
			Duration = node.Duration,
		};
		copy.Limits.AddRange(node.Limits);
		return copy;
	}

	private long ApplyDuration(long startMs, List<OrbitDurationPart> parts)
	{
		var current = startMs;
		foreach (var part in parts)
		{
			current = _calendar.Add(current, part.Unit, part.Count);
		}

		return current;
	}

	// The single duration on an operand's spine (throws if missing or duplicated).
	private static List<OrbitDurationPart> SpineDuration(OrbitTimeUnitNode node)
	{
		List<OrbitDurationPart>? found = null;
		OrbitAstNode? current = node;
		while (current is OrbitTimeUnitNode unitNode)
		{
			if (unitNode.Duration is { Count: > 0 })
			{
				if (found is not null)
				{
					throw new FormatException("A span operand may carry only one duration.");
				}

				found = unitNode.Duration;
			}

			current = unitNode.Child;
		}

		return found ?? throw new FormatException("Span operand is missing a duration (=<dur>).");
	}

	private static bool HasDuration(OrbitAstNode node)
	{
		if (node is OrbitSetOperationNode set)
		{
			return HasDuration(set.Left) || HasDuration(set.Right);
		}

		var unitNode = (OrbitTimeUnitNode)node;
		if (unitNode.Duration is { Count: > 0 })
		{
			return true;
		}

		return unitNode.Child is not null && HasDuration(unitNode.Child);
	}

	// Enforces the format boundary: every top-level operand must span (exactly one
	// duration on its spine) and no stray durations may hide elsewhere. This rejects
	// mixing span and granular operands within one expression.
	private void ValidateSpanFormat()
	{
		var operands = new List<OrbitTimeUnitNode>();
		void Collect(OrbitAstNode n)
		{
			if (n is OrbitSetOperationNode set)
			{
				Collect(set.Left);
				Collect(set.Right);
			}
			else
			{
				operands.Add((OrbitTimeUnitNode)n);
			}
		}

		Collect(_rawAst);

		var total = 0;
		void Count(OrbitAstNode n)
		{
			if (n is OrbitSetOperationNode set)
			{
				Count(set.Left);
				Count(set.Right);
				return;
			}

			var unitNode = (OrbitTimeUnitNode)n;
			if (unitNode.Duration is { Count: > 0 })
			{
				total++;
			}

			if (unitNode.Child is not null)
			{
				Count(unitNode.Child);
			}
		}

		Count(_rawAst);

		foreach (var operand in operands)
		{
			SpineDuration(operand); // one per operand, or throw
		}

		if (total != operands.Count)
		{
			throw new FormatException("Cannot mix span and granular sub-expressions; every operand must have exactly one =<dur>.");
		}
	}

	// Resolves and consumes the next entry, mutating the engine. If the next candidate
	// falls at/after `boundMs` it is NOT consumed (the cursor is left untouched, so it
	// remains resolvable) and null is returned — this lets windowed helpers stop at a
	// boundary without over-consuming a later entry.
	private OrbitResolutionEntry? Advance(long boundMs)
	{
		if (_exhausted)
		{
			return null;
		}

		// Bounded outer loop: each pass either emits, exhausts, or skips forward
		// (for an 'after' bound). The guard prevents a pathological infinite skip.
		var guard = 0;
		while (guard++ < 100_000)
		{
			var testCursor = new Cursor(_cursor);
			// Structural solve: every node limit (*x, @x, a top-level life) is resolved
			// inside the solver, so the candidate it finds is final.
			if (!SolveNext(_ast, testCursor, null))
			{
				_exhausted = true;
				return null;
			}

			var matchTime = testCursor.Ms;

			// Upper bound: stop before consuming anything at/after the window end.
			if (matchTime >= boundMs)
			{
				return null;
			}

			// Stream-global boundary limits (<t / >t).
			var verdict = ApplyLimits(matchTime);
			if (verdict == LimitVerdict.Stop)
			{
				_exhausted = true;
				return null;
			}

			if (verdict == LimitVerdict.Skip)
			{
				continue; // cursor advanced by ApplyLimits
			}

			_cursor = _calendar.Add(matchTime, _globalGranularity, 1);
			_lastEmitted = matchTime;
			_emittedCount++;
			return new OrbitResolutionEntry(matchTime, _globalGranularity);
		}

		_exhausted = true;
		return null;
	}

	private enum LimitVerdict
	{
		Ok,
		Skip,
		Stop,
	}

	// Evaluates the stream-global boundary limits against a candidate match.
	private LimitVerdict ApplyLimits(long matchTime)
	{
		foreach (var limit in _limits)
		{
			if (limit.Kind == OrbitLimitKind.Before)
			{
				// "<t": bound the stream to strictly before timestamp t.
				if (matchTime >= limit.At)
				{
					return LimitVerdict.Stop;
				}
			}
			else
			{
				// ">t": skip anything before the lower bound and jump the cursor to it.
				if (matchTime < limit.At)
				{
					_cursor = _calendar.SnapToStart(limit.At, _globalGranularity);
					return LimitVerdict.Skip;
				}
			}
		}

		return LimitVerdict.Ok;
	}

	private bool SolveNext(OrbitAstNode node, Cursor cursor, OrbitUnit? parentUnit)
	{
		if (node is OrbitSetOperationNode set)
		{
			return SolveSetOperation(set, cursor, parentUnit);
		}

		var unitNode = (OrbitTimeUnitNode)node;
		if (_nodes.TryGetValue(unitNode, out var info) && info.Parent is null && !_lifeSetAside.Contains(unitNode))
		{
			// A top-level node produces nothing before its life starts, or from its end on; inside a set
			// operation this lets sibling branches carry on, and on the spine it exhausts.
			if (cursor.Ms >= EndOf(unitNode))
			{
				return false;
			}

			var lifeStart = LifeStartOf(unitNode);
			if (cursor.Ms < lifeStart)
			{
				cursor.Ms = lifeStart;
			}
		}

		return SolveTimeUnit(unitNode, cursor, parentUnit);
	}

	private bool SolveTimeUnit(OrbitTimeUnitNode node, Cursor cursor, OrbitUnit? parentUnit)
	{
		var attempts = 0;
		while (attempts < 2_000)
		{
			var initialTime = cursor.Ms;

			// 1. Align this calendar layer to its immediate structural constraints.
			if (!AlignUnit(node, cursor, parentUnit))
			{
				return false;
			}

			// If alignment jumped forward, cascade-clear all deeper metrics.
			if (cursor.Ms > initialTime)
			{
				cursor.Ms = _calendar.SnapToStart(cursor.Ms, node.Unit);
			}

			// 2. Leaf level matched cleanly with no remaining children.
			if (node.Child is null)
			{
				return WithinEmission(node, cursor.Ms, parentUnit);
			}

			// 3. Drill down into deeper nested structural nodes.
			var childCursor = new Cursor(cursor.Ms);
			var childSuccess = SolveNext(node.Child, childCursor, node.Unit);

			// Verify the child match didn't bleed outside this parent's window — unless the node is a week that runs
			// across months.
			if (childSuccess && (RunsAcrossMonths(node, parentUnit) || IsSameContext(node.Unit, cursor.Ms, childCursor.Ms, parentUnit)))
			{
				cursor.Ms = childCursor.Ms;
				return WithinEmission(node, cursor.Ms, parentUnit);
			}

			// --- Try-and-Fail Backtracking ---
			// The child couldn't be satisfied in this period; step THIS layer
			// forward by 1 (or its interval) and retry.
			var step = node.Interval is > 1 ? node.Interval.Value : 1;
			var currentVal = NodeGet(node, cursor.Ms, parentUnit);
			var nextVal = currentVal + step;

			if (nextVal > NodeMax(node, parentUnit, cursor.Ms))
			{
				return false;
			}

			cursor.Ms = NodeSet(node, cursor.Ms, nextVal, parentUnit);
			cursor.Ms = _calendar.SnapToStart(cursor.Ms, node.Unit);
			attempts++;
		}

		return false;
	}

	// @x: a node's instances fire only up to its x-th in the life the match falls in. Past that the node has nothing
	// more in this life: a nested node waits for its parent's next period, and a top-level node is done (EndOf stops
	// the solver asking it again).
	private bool WithinEmission(OrbitTimeUnitNode node, long cursorMs, OrbitUnit? parentUnit)
	{
		if (!_nodes.TryGetValue(node, out var info) || info.Emit is null || _emitSetAside.Contains(node) || _lifeSetAside.Contains(node))
		{
			return true;
		}

		var life = info.Parent is null ? LifeStartOf(node) : _calendar.SnapToStart(cursorMs, parentUnit!.Value);
		return cursorMs <= CutoffOf(node, life, parentUnit);
	}

	// The time of a node's x-th instance in the life starting at `life` — its @x cutoff — or long.MaxValue when the
	// life holds fewer (long.MinValue for @0, which lets nothing through). A nested node's instances are counted from
	// the start of its parent's period, whatever the anchor; a top-level node's from the start of its life.
	private long CutoffOf(OrbitTimeUnitNode node, long life, OrbitUnit? parentUnit)
	{
		if (!_cutoffs.TryGetValue(node, out var lives))
		{
			lives = [];
			_cutoffs[node] = lives;
		}

		if (lives.TryGetValue(life, out var cached))
		{
			return cached;
		}

		var info = _nodes[node];
		var cutoff = long.MaxValue;
		if (info.Emit <= 0)
		{
			cutoff = long.MinValue;
		}
		// A promoted (continuing) engine has no life start to count from; Promote() refuses such schedules.
		else if (life != long.MinValue)
		{
			var topLevel = info.Parent is null;
			var unit = CalcGranularity(node);
			var cursor = new Cursor(life);
			_emitSetAside.Add(node);
			try
			{
				for (var count = 0; ;)
				{
					var found = topLevel
						? SolveNext(_wrapperRoots[node], cursor, null)
						: SolveTimeUnit(node, cursor, parentUnit) && _calendar.SnapToStart(cursor.Ms, parentUnit!.Value) == life;
					if (!found)
					{
						break;
					}

					if (++count >= info.Emit)
					{
						cutoff = cursor.Ms;
						break;
					}

					cursor.Ms = _calendar.Add(cursor.Ms, unit, 1);
				}
			}
			finally
			{
				_emitSetAside.Remove(node);
			}
		}

		lives[life] = cutoff;
		return cutoff;
	}

	// Where a top-level node's single life starts: the period of its unit the stream starts in, unless one of the
	// node's instances in that period falls before the start — then that period is skipped whole and the life starts
	// with the next one (the next on-phase one under a continuous %N).
	private long LifeStartOf(OrbitTimeUnitNode node)
	{
		if (_lifeStarts.TryGetValue(node, out var cached))
		{
			return cached;
		}

		var lifeStart = long.MinValue;
		if (!_continuing)
		{
			var start = SeededStart();
			var period = _calendar.SnapToStart(start, node.Unit);
			var periodEnd = _calendar.Add(period, node.Unit, 1);
			lifeStart = period;
			_lifeSetAside.Add(node);
			try
			{
				var first = new Cursor(period);
				if (SolveNext(_wrapperRoots[node], first, null) && first.Ms < start && first.Ms < periodEnd)
				{
					var step = node.Indices is null && node.Interval is > 1 ? node.Interval.Value : 1;
					lifeStart = _calendar.Add(period, node.Unit, step);
				}
			}
			finally
			{
				_lifeSetAside.Remove(node);
			}
		}

		_lifeStarts[node] = lifeStart;
		return lifeStart;
	}

	// Where a top-level node's instances end: its *x window's end (for a bare unit, whose single run is the node's own
	// repetition), or just after its @x cutoff, whichever comes first; long.MaxValue when neither applies.
	private long EndOf(OrbitTimeUnitNode node)
	{
		var info = _nodes[node];
		var end = long.MaxValue;
		if (info.Repeat is { } repeat && node.Indices is null)
		{
			end = Math.Min(end, RepeatEnd(node, repeat));
		}

		if (info.Emit is not null && !_emitSetAside.Contains(node))
		{
			var cutoff = CutoffOf(node, LifeStartOf(node), null);
			end = Math.Min(end, cutoff == long.MaxValue ? long.MaxValue : cutoff + 1);
		}

		return end;
	}

	// The end of a top-level bare node's *x window: x steps of its unit (every N-th under %N) from its life start.
	private long RepeatEnd(OrbitTimeUnitNode node, int repeat)
	{
		var lifeStart = LifeStartOf(node);
		if (lifeStart == long.MinValue)
		{
			return long.MaxValue;
		}

		var step = node.Interval is > 1 ? node.Interval.Value : 1;
		return _calendar.Add(lifeStart, node.Unit, repeat * step);
	}

	private bool AlignUnit(OrbitTimeUnitNode node, Cursor cursor, OrbitUnit? parentUnit)
	{
		var unit = node.Unit;
		var minVal = NodeMin(node, parentUnit, cursor.Ms);
		var currentVal = NodeGet(node, cursor.Ms, parentUnit);
		var maxVal = NodeMax(node, parentUnit, cursor.Ms);
		var interval = node.Interval is > 1 ? node.Interval.Value : 1;
		var bases = ResolveBases(node, cursor.Ms, parentUnit);
		_nodes.TryGetValue(node, out var info);
		var repeat = _lifeSetAside.Contains(node) ? null : info?.Repeat;

		// Fast path: pure whitelist. Jump straight to the next allowed value
		// >= currentVal instead of scanning every value. (A *x changes nothing
		// here: an index % does not step is a run of one value.)
		if (interval == 1 && bases is not null)
		{
			foreach (var b in bases)
			{
				if (b < currentVal || b > maxVal)
				{
					continue;
				}

				if (b != currentVal)
				{
					cursor.Ms = NodeSet(node, cursor.Ms, b, parentUnit);
				}

				return true;
			}

			return false;
		}

		// *x on a bare unit bounds its single run: a nested node's run starts at its parent period's first value (the
		// first on-phase one under %N); a top-level node's runs from its life start, as a window of time.
		var windowEnd = long.MaxValue;
		if (repeat is { } limit && bases is null)
		{
			if (info!.Parent is null)
			{
				windowEnd = RepeatEnd(node, limit);
			}
			else
			{
				maxVal = Math.Min(maxVal, RunLast(node, cursor.Ms, parentUnit, minVal, maxVal, interval, limit));
			}
		}

		for (var val = Math.Max(currentVal, minVal); val <= maxVal; val++)
		{
			// Local offset loop (e.g. h{4}%3 -> every 3h starting from hour 4); a *x
			// keeps each run's first x values.
			if (interval > 1 && bases is not null)
			{
				var matched = false;
				foreach (var b in bases)
				{
					if (val >= b && (val - b) % interval == 0 && (repeat is null || (val - b) / interval < repeat))
					{
						matched = true;
						break;
					}
				}

				if (!matched)
				{
					continue;
				}
			}

			// Continuous global interval (e.g. M[d%2] -> every 2 days from anchor).
			if (interval > 1 && bases is null)
			{
				var projected = NodeSet(node, cursor.Ms, val, parentUnit);
				if (Math.Abs(_calendar.Delta(projected, _anchor, unit)) % interval != 0)
				{
					continue;
				}
			}

			// Past a top-level window's end every later value is too.
			if (windowEnd != long.MaxValue && _calendar.SnapToStart(NodeSet(node, cursor.Ms, val, parentUnit), unit) >= windowEnd)
			{
				return false;
			}

			if (val != currentVal)
			{
				cursor.Ms = NodeSet(node, cursor.Ms, val, parentUnit);
			}

			return true;
		}

		return false; // Exhausted this unit; forces the parent loop to roll over.
	}

	// The last value a nested bare node's *x lets its run reach in the current parent period: the run starts at the
	// period's first value (its first on-phase one under a continuous %N) and keeps x of its steps.
	private int RunLast(OrbitTimeUnitNode node, long cursorMs, OrbitUnit? parentUnit, int minVal, int maxVal, int interval, int repeat)
	{
		var first = minVal;
		if (interval > 1)
		{
			while (first <= maxVal && Math.Abs(_calendar.Delta(NodeSet(node, cursorMs, first, parentUnit), _anchor, node.Unit)) % interval != 0)
			{
				first++;
			}
		}

		return first + (repeat - 1) * interval;
	}

	// Resolves a node's index spec into a sorted list of concrete unit values,
	// or null when the node has no indexing (matches every value).
	private int[]? ResolveBases(OrbitTimeUnitNode node, long cursorMs, OrbitUnit? parentUnit)
	{
		var idx = node.Indices;
		if (idx is null)
		{
			return null;
		}

		if (idx.Kind == OrbitIndexKind.List)
		{
			if (!_baseCache.TryGetValue(node, out var cached))
			{
				cached = [.. idx.Values.OrderBy(value => value)];
				_baseCache[node] = cached;
			}

			return cached;
		}

		if (idx.Kind == OrbitIndexKind.Range)
		{
			if (!_baseCache.TryGetValue(node, out var cached))
			{
				cached = new int[Math.Max(0, idx.End - idx.Start + 1)];
				for (var i = 0; i < cached.Length; i++)
				{
					cached[i] = idx.Start + i;
				}

				_baseCache[node] = cached;
			}

			return cached;
		}

		// Random: pick `count` distinct values, deterministic per parent period so
		// the choice is stable across backtracking within the same period but
		// varies between periods (e.g. different random days each week).
		return PickRandom(node, cursorMs, parentUnit, idx.Count);
	}

	private static int Imul(int a, int b) => unchecked(a * b);

	private int[] PickRandom(OrbitTimeUnitNode node, long cursorMs, OrbitUnit? parentUnit, int count)
	{
		var unit = node.Unit;
		var lo = NodeMin(node, parentUnit, cursorMs);
		var hi = NodeMax(node, parentUnit, cursorMs);

		var pool = new List<int>();
		for (var v = lo; v <= hi; v++)
		{
			pool.Add(v);
		}

		// Seed off the engine seed + the start of the enclosing period, so the pick
		// is deterministic per (seed, period) but varies between periods and seeds.
		var seedBase = _calendar.SnapToStart(cursorMs, parentUnit ?? unit);
		var secs = JsDate.FloorDiv(seedBase, 1000);
		// JS bitwise XOR coerces operands with ToInt32 (mod 2^32, signed).
		var secsI32 = unchecked((int)(uint)JsDate.FloorMod(secs, 4_294_967_296L));
		var seed = unchecked((int)(uint)(
			(uint)unchecked((int)_seed ^ secsI32 ^ Imul(OrbitUnits.ToChar(unit), unchecked((int)0x9e3779b1)) ^ Imul(count, unchecked((int)0x85ebca6b)))));

		double Rand()
		{
			seed = unchecked(seed + 0x6d2b79f5);
			var t = Imul(seed ^ (int)((uint)seed >> 15), 1 | seed);
			t = unchecked((t + Imul(t ^ (int)((uint)t >> 7), 61 | t)) ^ t);
			return ((uint)(t ^ (int)((uint)t >> 14))) / 4294967296.0;
		}

		// Partial Fisher-Yates: shuffle only the first `n` slots we need.
		var n = Math.Min(count, pool.Count);
		for (var i = 0; i < n; i++)
		{
			var j = i + (int)Math.Floor(Rand() * (pool.Count - i));
			(pool[i], pool[j]) = (pool[j], pool[i]);
		}

		var picked = pool.Take(n).ToArray();
		Array.Sort(picked);
		return picked;
	}

	private bool SolveSetOperation(OrbitSetOperationNode node, Cursor cursor, OrbitUnit? parentUnit)
	{
		if (node.Operator == OrbitSetOperator.Union)
		{
			var leftCursor = new Cursor(cursor.Ms);
			var rightCursor = new Cursor(cursor.Ms);
			var leftValid = SolveNext(node.Left, leftCursor, parentUnit);
			var rightValid = SolveNext(node.Right, rightCursor, parentUnit);

			if (!leftValid && !rightValid)
			{
				return false;
			}

			if (leftValid && !rightValid)
			{
				cursor.Ms = leftCursor.Ms;
				return true;
			}

			if (!leftValid && rightValid)
			{
				cursor.Ms = rightCursor.Ms;
				return true;
			}

			cursor.Ms = Math.Min(leftCursor.Ms, rightCursor.Ms);
			return true;
		}

		if (node.Operator == OrbitSetOperator.Intersection)
		{
			var attempts = 0;
			while (attempts < 1_000)
			{
				if (!SolveNext(node.Left, cursor, parentUnit))
				{
					return false;
				}

				var rightCursor = new Cursor(cursor.Ms);
				if (SolveNext(node.Right, rightCursor, parentUnit) && cursor.Ms == rightCursor.Ms)
				{
					return true;
				}

				// Right side branched further ahead; pull master cursor forward and re-verify.
				cursor.Ms = rightCursor.Ms;
				attempts++;
			}

			return false;
		}

		if (node.Operator == OrbitSetOperator.Exclusion)
		{
			var attempts = 0;
			while (attempts < 1_000)
			{
				if (!SolveNext(node.Left, cursor, parentUnit))
				{
					return false;
				}

				var rightCursor = new Cursor(cursor.Ms);
				var rightValid = SolveNext(node.Right, rightCursor, parentUnit);

				if (!rightValid || rightCursor.Ms > cursor.Ms)
				{
					return true;
				}

				cursor.Ms = _calendar.Add(cursor.Ms, _globalGranularity, 1);
				attempts++;
			}

			return false;
		}

		if (node.Operator == OrbitSetOperator.SymmetricDifference)
		{
			var attempts = 0;
			while (attempts < 1_000)
			{
				var leftCursor = new Cursor(cursor.Ms);
				var rightCursor = new Cursor(cursor.Ms);
				var leftValid = SolveNext(node.Left, leftCursor, parentUnit);
				var rightValid = SolveNext(node.Right, rightCursor, parentUnit);

				if (!leftValid && !rightValid)
				{
					return false;
				}

				if (leftValid && !rightValid)
				{
					cursor.Ms = leftCursor.Ms;
					return true;
				}

				if (!leftValid && rightValid)
				{
					cursor.Ms = rightCursor.Ms;
					return true;
				}

				if (leftCursor.Ms != rightCursor.Ms)
				{
					cursor.Ms = Math.Min(leftCursor.Ms, rightCursor.Ms);
					return true;
				}

				// Both sides agree on this instant -> excluded; step past it and retry.
				cursor.Ms = _calendar.Add(cursor.Ms, _globalGranularity, 1);
				attempts++;
			}

			return false;
		}

		return false;
	}

	private bool IsSameContext(OrbitUnit unit, long originalMs, long updatedMs, OrbitUnit? parentUnit)
	{
		if (unit == OrbitUnit.Year)
		{
			return true;
		}

		if (unit == OrbitUnit.Month)
		{
			return _calendar.Get(originalMs, OrbitUnit.Year) == _calendar.Get(updatedMs, OrbitUnit.Year);
		}

		// A week or a day counted within a year is cut at the year's end, as one within a month is at the month's.
		if ((unit is OrbitUnit.Week or OrbitUnit.Day) && parentUnit == OrbitUnit.Year)
		{
			return _calendar.Get(originalMs, OrbitUnit.Year) == _calendar.Get(updatedMs, OrbitUnit.Year);
		}

		if (unit == OrbitUnit.Week)
		{
			return _calendar.Get(originalMs, OrbitUnit.Month) == _calendar.Get(updatedMs, OrbitUnit.Month)
				&& _calendar.Get(originalMs, OrbitUnit.Year) == _calendar.Get(updatedMs, OrbitUnit.Year);
		}

		if (unit == OrbitUnit.Day)
		{
			var scope = parentUnit == OrbitUnit.Week ? OrbitUnit.Week : OrbitUnit.Month;
			return _calendar.Get(originalMs, scope) == _calendar.Get(updatedMs, scope)
				&& _calendar.Get(originalMs, OrbitUnit.Year) == _calendar.Get(updatedMs, OrbitUnit.Year);
		}

		return _calendar.Get(originalMs, OrbitUnit.Day) == _calendar.Get(updatedMs, OrbitUnit.Day)
			&& _calendar.Get(originalMs, OrbitUnit.Year) == _calendar.Get(updatedMs, OrbitUnit.Year);
	}

	// --- Node-aware unit helpers ---
	// A week inside a month or a year is numbered from that cycle's first complete week (see WeekOffset), and a day inside
	// a year is its day of the year; every other node reads its unit as the parent-aware unit helpers below do.

	// The cycle a week node is counted within: a month — written, or the one a top-level week index addresses — or a
	// written year (a calendar week). A bare top-level week has none: it runs week after week.
	private OrbitUnit? WeekCycle(OrbitTimeUnitNode node, OrbitUnit? parentUnit)
	{
		if (node.Unit != OrbitUnit.Week || !_nodes.TryGetValue(node, out var info))
		{
			return null;
		}

		if (parentUnit == OrbitUnit.Year && info.Parent == OrbitUnit.Year)
		{
			return OrbitUnit.Year;
		}

		if (parentUnit == OrbitUnit.Month && (info.Parent is not null || node.Indices is not null))
		{
			return OrbitUnit.Month;
		}

		return null;
	}

	// Whether a node is a bare top-level week: it runs week after week, so the month it is implicitly wrapped in does not
	// cut it, and a week that straddles two months keeps its days in both. (Cut there, the days past the seam were lost:
	// stepping on to the next week lands in the new month's second week.)
	private bool RunsAcrossMonths(OrbitTimeUnitNode node, OrbitUnit? parentUnit)
		=> node.Unit == OrbitUnit.Week && parentUnit == OrbitUnit.Month && _nodes.TryGetValue(node, out var info) && info.Parent is null && node.Indices is null;

	// Whether a node is a day counted within a written year: its day of the year.
	private static bool IsDayOfYear(OrbitTimeUnitNode node, OrbitUnit? parentUnit)
		=> node.Unit == OrbitUnit.Day && parentUnit == OrbitUnit.Year;

	// A week's place in a cycle, counting the week the cycle starts in as the first (before any offset).
	private int WeekOfCycle(long cursorMs, long cycleStartMs)
		=> (int)_calendar.Delta(_calendar.SnapToStart(cursorMs, OrbitUnit.Week), _calendar.SnapToStart(cycleStartMs, OrbitUnit.Week), OrbitUnit.Week) + 1;

	// 1 when the week a cycle starts in is not the cycle's, else 0. That week belongs to the cycle only when the first
	// instance its node asks for (a leaf week asks for the whole week) falls inside the cycle — so a month that starts
	// midweek has M[w{1}[d{1}]] land on its first Monday, not on the Monday of the week before.
	private int WeekOffset(OrbitTimeUnitNode node, long cursorMs, OrbitUnit cycle)
	{
		var start = _calendar.SnapToStart(cursorMs, cycle);
		if (!_weekOffsets.TryGetValue(node, out var byCycle))
		{
			byCycle = [];
			_weekOffsets[node] = byCycle;
		}

		if (byCycle.TryGetValue(start, out var cached))
		{
			return cached;
		}

		var offset = 0;
		var week = _calendar.SnapToStart(start, OrbitUnit.Week);
		if (week < start)
		{
			var first = week;
			if (node.Child is not null)
			{
				var probe = new Cursor(week);
				var weekEnd = _calendar.Add(week, OrbitUnit.Week, 1);
				first = SolveNext(node.Child, probe, OrbitUnit.Week) && probe.Ms < weekEnd ? probe.Ms : long.MaxValue;
			}

			if (first < start)
			{
				offset = 1;
			}
		}

		byCycle[start] = offset;
		return offset;
	}

	private int NodeGet(OrbitTimeUnitNode node, long cursorMs, OrbitUnit? parentUnit)
	{
		if (WeekCycle(node, parentUnit) is { } cycle)
		{
			return WeekOfCycle(cursorMs, _calendar.SnapToStart(cursorMs, cycle)) - WeekOffset(node, cursorMs, cycle);
		}

		if (IsDayOfYear(node, parentUnit))
		{
			return (int)_calendar.Delta(_calendar.SnapToStart(cursorMs, OrbitUnit.Day), _calendar.SnapToStart(cursorMs, OrbitUnit.Year), OrbitUnit.Day) + 1;
		}

		return _calendar.Get(cursorMs, node.Unit, parentUnit);
	}

	private int NodeMin(OrbitTimeUnitNode node, OrbitUnit? parentUnit, long contextMs)
	{
		return WeekCycle(node, parentUnit) is not null || IsDayOfYear(node, parentUnit) ? 1 : UnitMin(node.Unit, parentUnit, contextMs);
	}

	// A week's last in its cycle is the one the cycle ends in, so a week index never overflows into the next cycle; a
	// day's last in its year is the year's last day.
	private int NodeMax(OrbitTimeUnitNode node, OrbitUnit? parentUnit, long contextMs)
	{
		if (WeekCycle(node, parentUnit) is { } cycle)
		{
			var start = _calendar.SnapToStart(contextMs, cycle);
			var lastDay = _calendar.Add(_calendar.Add(start, cycle, 1), OrbitUnit.Day, -1);
			return WeekOfCycle(lastDay, start) - WeekOffset(node, contextMs, cycle);
		}

		if (IsDayOfYear(node, parentUnit))
		{
			var start = _calendar.SnapToStart(contextMs, OrbitUnit.Year);
			return (int)_calendar.Delta(_calendar.Add(start, OrbitUnit.Year, 1), start, OrbitUnit.Day);
		}

		return UnitMax(node.Unit, parentUnit, contextMs);
	}

	private long NodeSet(OrbitTimeUnitNode node, long cursorMs, int value, OrbitUnit? parentUnit)
	{
		if (WeekCycle(node, parentUnit) is { } cycle)
		{
			var current = WeekOfCycle(cursorMs, _calendar.SnapToStart(cursorMs, cycle));
			return _calendar.Add(cursorMs, OrbitUnit.Week, value + WeekOffset(node, cursorMs, cycle) - current);
		}

		if (IsDayOfYear(node, parentUnit))
		{
			return _calendar.Add(cursorMs, OrbitUnit.Day, value - NodeGet(node, cursorMs, parentUnit));
		}

		return UnitSet(cursorMs, node.Unit, value, parentUnit);
	}

	// --- Parent-aware unit helpers ---
	// A day inside a week ('d' under 'w') is a day-of-week (1..7), which the raw
	// calendar set/max don't understand (they assume day-of-month). These wrappers
	// bridge that so day-of-week indexing resolves and advances correctly.

	private int UnitMax(OrbitUnit unit, OrbitUnit? parentUnit, long contextMs)
	{
		if (unit == OrbitUnit.Day && parentUnit == OrbitUnit.Week)
		{
			return 7;
		}

		return _calendar.Max(unit, contextMs);
	}

	private int UnitMin(OrbitUnit unit, OrbitUnit? parentUnit, long contextMs)
	{
		if (unit == OrbitUnit.Day && parentUnit == OrbitUnit.Week)
		{
			return 1;
		}

		return _calendar.Min(unit, contextMs);
	}

	private long UnitSet(long cursorMs, OrbitUnit unit, int value, OrbitUnit? parentUnit)
	{
		if (unit == OrbitUnit.Day && parentUnit == OrbitUnit.Week)
		{
			var weekStart = _calendar.SnapToStart(cursorMs, OrbitUnit.Week);
			return _calendar.Add(weekStart, OrbitUnit.Day, value - 1);
		}

		// Moving a month or a year from a day the target lacks (the 31st into a 30-day month, the 29th of February
		// into a common year) would roll over into the period after it, so the move starts from the period's first day.
		if (unit is OrbitUnit.Month or OrbitUnit.Year)
		{
			return _calendar.Set(_calendar.SnapToStart(cursorMs, unit), unit, value);
		}

		return _calendar.Set(cursorMs, unit, value);
	}

	private OrbitUnit CalcGranularity(OrbitAstNode node)
	{
		if (_granMemo.TryGetValue(node, out var memo))
		{
			return memo;
		}

		OrbitUnit result;
		if (node is OrbitSetOperationNode set)
		{
			var left = CalcGranularity(set.Left);
			var right = CalcGranularity(set.Right);
			result = (int)left >= (int)right ? left : right;
		}
		else
		{
			var unitNode = (OrbitTimeUnitNode)node;
			var deepest = unitNode.Unit;
			var current = unitNode.Child;
			while (current is not null)
			{
				if (current is OrbitTimeUnitNode childUnit)
				{
					if ((int)childUnit.Unit > (int)deepest)
					{
						deepest = childUnit.Unit;
					}

					current = childUnit.Child;
				}
				else
				{
					var setGranularity = CalcGranularity(current);
					if ((int)setGranularity > (int)deepest)
					{
						deepest = setGranularity;
					}

					break;
				}
			}

			result = deepest;
		}

		_granMemo[node] = result;
		return result;
	}

	private static List<ResolvedLimit> CollectLimits(OrbitAstNode node)
	{
		var output = new List<ResolvedLimit>();
		void Visit(OrbitAstNode n)
		{
			if (n is OrbitSetOperationNode set)
			{
				Visit(set.Left);
				Visit(set.Right);
				return;
			}

			var unitNode = (OrbitTimeUnitNode)n;
			foreach (var limit in unitNode.Limits)
			{
				if (limit.Kind is OrbitLimitKind.Before or OrbitLimitKind.After)
				{
					if (JsDate.TryParseIso(limit.Timestamp, out var at))
					{
						output.Add(new ResolvedLimit(limit.Kind, at, unitNode.Unit));
					}
				}

				// @x / *x belong to their node and are resolved inside the solver.
			}

			if (unitNode.Child is not null)
			{
				Visit(unitNode.Child);
			}
		}

		Visit(node);
		return output;
	}

	// Walks the RAW (un-normalized) AST recording every written node's parent unit (null for a top-level node) and
	// its tightest *x and @x.
	private void IndexNodes(OrbitAstNode node, OrbitUnit? parent)
	{
		if (node is OrbitSetOperationNode set)
		{
			// A set operation doesn't introduce a nesting level of its own; its
			// operands share the enclosing written parent.
			IndexNodes(set.Left, parent);
			IndexNodes(set.Right, parent);
			return;
		}

		var unitNode = (OrbitTimeUnitNode)node;
		int? repeat = null;
		int? emit = null;
		foreach (var limit in unitNode.Limits)
		{
			if (limit.Kind == OrbitLimitKind.Iterations)
			{
				repeat = repeat is null ? limit.Count : Math.Min(repeat.Value, limit.Count);
			}

			if (limit.Kind == OrbitLimitKind.Instances)
			{
				emit = emit is null ? limit.Count : Math.Min(emit.Value, limit.Count);
			}
		}

		_nodes[unitNode] = new NodeInfo(parent, repeat, emit);
		if (unitNode.Child is not null)
		{
			IndexNodes(unitNode.Child, unitNode.Unit);
		}
	}
}
