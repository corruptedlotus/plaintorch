namespace Pleiades.Orbits;

// C# port of @pleiades/orbits engine.ts. The resolution logic, counter machinery, and
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
public sealed class OrbitEngine
{
	// Stream-global boundary limits, hoisted out of the AST once at construction time.
	// (@x/*x are NOT here — they are per-node running counters handled via provenance.)
	private readonly record struct ResolvedLimit(OrbitLimitKind Kind, long At, OrbitUnit Unit);

	// A limited node encountered on an emission's provenance path.
	private readonly record struct PathEntry(int Id, int? Instances, int? Iterations, OrbitUnit? ResetUnit, long Period);

	private sealed class Cursor(long ms)
	{
		public long Ms = ms;
	}

	private readonly OrbitAstNode _ast;
	private readonly IOrbitCalendar _calendar;
	private long _anchor;
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

	// Caches: index bases for list/range nodes (context-independent) and memoized
	// granularity per node. Random bases are context-dependent and computed live.
	private readonly Dictionary<OrbitTimeUnitNode, int[]> _baseCache = [];
	private readonly Dictionary<OrbitAstNode, OrbitUnit> _granMemo = [];

	// --- Per-node @x/*x counter machinery ---
	private readonly Dictionary<OrbitTimeUnitNode, int> _nodeId = [];
	private readonly Dictionary<OrbitTimeUnitNode, (int? Instances, int? Iterations, OrbitUnit? ResetUnit)> _limited = [];
	private Dictionary<int, OrbitCounterState> _counters = [];
	private HashSet<int> _deadNodes = [];
	private readonly Dictionary<int, (int? Instances, int? Iterations, OrbitUnit? ResetUnit)> _limitedById = [];
	private bool _autoReset;

	// --- Span format ---
	private readonly bool _spanFormat;
	private readonly OrbitAstNode _rawAst;
	private readonly IOrbitSpanStream? _spanRoot;

	/// <summary>
	/// Builds an engine over a parsed AST. <paramref name="granularOnly"/> forces granular
	/// resolution even when durations are present; the span resolver uses it for the
	/// per-operand sub-engines that enumerate starts.
	/// </summary>
	public OrbitEngine(OrbitAstNode ast, long anchorMs, IOrbitCalendar calendar, uint? seed = null, bool autoReset = false, bool granularOnly = false)
	{
		ArgumentNullException.ThrowIfNull(ast);
		_calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
		// Desugar z/Z datetime literals into their equivalent nested time-unit chain up
		// front, so every downstream pass (limits, normalize, span-building) only ever sees
		// OrbitTimeUnitNode / OrbitSetOperationNode and needs no literal awareness.
		var source = ExpandLiterals(ast);
		_rawAst = source;
		// Record each node's explicit (user-written) parent BEFORE normalization
		// adds synthetic y>M>... wrappers, then normalize and index the nodes.
		IndexLimits(source, null);
		_ast = NormalizeAst(source);
		AssignIds(_ast);
		foreach (var (node, info) in _limited)
		{
			_limitedById[_nodeId[node]] = info;
		}

		_anchor = anchorMs;
		_seed = seed ?? (uint)Random.Shared.NextInt64(0x1_0000_0000L);
		_autoReset = autoReset;
		_globalGranularity = CalcGranularity(_ast);
		_limits = CollectLimits(_ast);
		_cursor = SeededStart();

		_spanFormat = !granularOnly && HasDuration(source);
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
	public static OrbitEngine FromNotation(string notation, long epochMs, IOrbitCalendar calendar, uint? seed = null, bool autoReset = false)
	{
		var ast = new OrbitParser(notation).Parse();
		var engine = new OrbitEngine(ast, epochMs, calendar, seed, autoReset)
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

		var engine = FromNotation(snapshot.Notation, epochMs, calendar, (uint)snapshot.Seed, snapshot.AutoReset ?? false);

		if (snapshot.Cursor is not null)
		{
			// Full snapshot: restore the exact search origin and counters. This
			// overrides the constructor's '>t' seeding, which the stored cursor
			// already reflects.
			if (!JsDate.TryParseIso(snapshot.Cursor, out var cursorMs))
			{
				throw new FormatException($"Orbit snapshot cursor '{snapshot.Cursor}' is not a valid instant.");
			}

			engine._cursor = cursorMs;
			engine._emittedCount = snapshot.EmittedCount ?? 0;
			engine._exhausted = snapshot.Exhausted ?? false;
			if (snapshot.Counters is not null)
			{
				foreach (var (id, state) in snapshot.Counters)
				{
					engine._counters[int.Parse(id, System.Globalization.CultureInfo.InvariantCulture)] = state.Clone();
				}
			}

			engine.RecomputeDeadNodes();
		}
		else
		{
			// Promoted snapshot: epoch IS the last emitted (on-phase) entry, so we
			// resume strictly after it to avoid re-emitting the consumed entry.
			engine._cursor = engine._calendar.Add(epochMs, engine._globalGranularity, 1);
		}

		return engine;
	}

	/// <summary>
	/// True iff the schedule can be re-anchored onto any emitted entry without changing
	/// the stream — i.e. it carries no running-counter limit.
	/// </summary>
	public bool IsAnchorStable()
	{
		if (_spanFormat)
		{
			return false; // span schedules are not epoch-promotable
		}

		static bool HasCountLimit(OrbitAstNode node)
		{
			if (node is OrbitSetOperationNode set)
			{
				return HasCountLimit(set.Left) || HasCountLimit(set.Right);
			}

			var unitNode = (OrbitTimeUnitNode)node;
			if (unitNode.Limits.Any(l => l.Kind is OrbitLimitKind.Instances or OrbitLimitKind.Iterations))
			{
				return true;
			}

			return unitNode.Child is not null && HasCountLimit(unitNode.Child);
		}

		return !HasCountLimit(_ast);
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
		var snapshot = new OrbitSnapshot
		{
			Version = 1,
			Notation = n,
			Epoch = JsDate.ToIsoString(_anchor),
			Seed = _seed,
			Cursor = JsDate.ToIsoString(_cursor),
			EmittedCount = _emittedCount,
			Exhausted = _exhausted,
		};
		if (_counters.Count > 0)
		{
			snapshot.Counters = _counters.ToDictionary(
				pair => pair.Key.ToString(System.Globalization.CultureInfo.InvariantCulture),
				pair => pair.Value.Clone());
		}

		if (_autoReset)
		{
			snapshot.AutoReset = true;
		}

		return snapshot;
	}

	/// <summary>
	/// Compact snapshot that rebases the epoch onto the last emitted entry and drops
	/// cursor/count. Only valid for anchor-stable schedules (no @ / *), and only after
	/// at least one entry has been emitted.
	/// </summary>
	public OrbitSnapshot Promote(string? notation = null)
	{
		if (!IsAnchorStable())
		{
			throw new InvalidOperationException("Promote() is invalid for schedules with @ or * limits; use Serialize().");
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
	// position, charging @x/*x for everything they pass, and leave it advanced. Use
	// them to actually move through a schedule cheaply (no epoch replay).
	//
	// The `Resolve*` variants are NON-mutating: they run the same logic inside a
	// temporary traversal (positioned from the epoch for counter schedules, or seeked
	// straight to `start` for counter-free ones) and restore the live state, so a
	// query leaves no trace and reflects the schedule as defined from the epoch.

	/// <summary>
	/// MUTATING. Advances to <paramref name="startMs"/> (consuming earlier entries) then
	/// returns every entry whose start is in the half-open window [start, end); leaves
	/// the engine positioned at the window end.
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
		var savedCounters = _counters;
		var savedDeadNodes = _deadNodes;
		var savedAnchor = _anchor;
		var savedAutoReset = _autoReset;

		_emittedCount = 0;
		_exhausted = false;
		_lastEmitted = null;
		_counters = [];
		_deadNodes = [];
		_autoReset = false; // a transient query never rebases the live epoch
		// A counter-free schedule is history-independent, so jump straight to `start`
		// (never before the epoch). Otherwise replay from the epoch so the running
		// counters are accurate by the time we reach the window.
		_cursor = IsAnchorStable()
			? _calendar.SnapToStart(Math.Max(startMs, _anchor), _globalGranularity)
			: SeededStart();

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
			_counters = savedCounters;
			_deadNodes = savedDeadNodes;
			_anchor = savedAnchor;
			_autoReset = savedAutoReset;
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

		var current = (OrbitTimeUnitNode)node;
		while (Parent(current.Unit) is { } parentUnit)
		{
			current = new OrbitTimeUnitNode
			{
				Unit = parentUnit,
				Child = current,
			};
		}

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
		var inner = new OrbitEngine(node, _anchor, _calendar, _seed, autoReset: false, granularOnly: true);
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
	// falls at/after `boundMs` it is NOT consumed (cursor and counters are left
	// untouched, so it remains resolvable) and null is returned — this lets windowed
	// helpers stop at a boundary without over-consuming a later entry.
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
			// Structural solve (dead-node aware): find the next candidate ignoring
			// the @x/*x running budgets, which are enforced afterwards.
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

			// 1. Stream-global boundary limits (<t / >t).
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

			// 2. Per-node @x/*x budgets, checked against the candidate's provenance.
			var path = CollectPath(_ast, matchTime, null);
			if (RejectByCounters(path, matchTime))
			{
				continue; // over budget; cursor advanced / node killed
			}

			// 3. Entropic reset (opt-in): if this candidate opens a fresh era — every
			// reset scope has rolled over and nothing un-resettable has fired — rebase
			// the epoch onto the last (on-phase) entry and drop the now-zero counters.
			if (_autoReset && _counters.Count > 0 && _lastEmitted is not null && AtResetPoint(matchTime))
			{
				_anchor = _lastEmitted.Value;
				_counters.Clear();
				_deadNodes.Clear();
			}

			// Accepted: charge the counters, then emit.
			ChargeCounters(path, matchTime);
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

	// Returns true if a limited node on the path is over budget for this candidate,
	// having taken the appropriate corrective action (skip the cursor forward for a
	// per-cycle *x, or permanently kill the node for @x / global *x).
	private bool RejectByCounters(List<PathEntry> path, long matchTime)
	{
		foreach (var entry in path)
		{
			_counters.TryGetValue(entry.Id, out var state);

			// @x: a hard cap on total instances through this node.
			if (entry.Instances is not null && (state?.Fired ?? 0) >= entry.Instances)
			{
				_deadNodes.Add(entry.Id);
				return true;
			}

			// *x: a cap on distinct node-periods per reset scope.
			if (entry.Iterations is not null)
			{
				var cycleKey = entry.ResetUnit is { } resetUnit ? _calendar.SnapToStart(matchTime, resetUnit) : 0;
				var sameScope = state?.CycleKey == cycleKey;
				var iters = sameScope ? state?.Iters ?? 0 : 0;
				var lastPeriod = sameScope ? state?.LastPeriod : null;
				var isNewPeriod = entry.Period != lastPeriod;
				if (isNewPeriod && iters >= entry.Iterations)
				{
					if (entry.ResetUnit is { } unit)
					{
						// Per-cycle: jump to the start of the next parent cycle.
						_cursor = _calendar.Add(_calendar.SnapToStart(matchTime, unit), unit, 1);
					}
					else
					{
						// Global: this node can never open a new period again.
						_deadNodes.Add(entry.Id);
					}

					return true;
				}
			}
		}

		return false;
	}

	// Charges the @x/*x counters for every limited node the emission passed through.
	private void ChargeCounters(List<PathEntry> path, long matchTime)
	{
		foreach (var entry in path)
		{
			if (!_counters.TryGetValue(entry.Id, out var state))
			{
				state = new OrbitCounterState();
			}

			if (entry.Instances is not null)
			{
				state.Fired = (state.Fired ?? 0) + 1;
				if (state.Fired >= entry.Instances)
				{
					_deadNodes.Add(entry.Id);
				}
			}

			if (entry.Iterations is not null)
			{
				var cycleKey = entry.ResetUnit is { } resetUnit ? _calendar.SnapToStart(matchTime, resetUnit) : 0;
				if (state.CycleKey != cycleKey)
				{
					state.CycleKey = cycleKey;
					state.Iters = 1;
					state.LastPeriod = entry.Period;
				}
				else if (entry.Period != state.LastPeriod)
				{
					state.Iters = (state.Iters ?? 0) + 1;
					state.LastPeriod = entry.Period;
				}
			}

			_counters[entry.Id] = state;
		}
	}

	// Entropy is 0 at `matchTime` iff none of the currently-active counters carry
	// state that survival past this instant depends on.
	private bool AtResetPoint(long matchTime)
	{
		foreach (var (id, state) in _counters)
		{
			if (!_limitedById.TryGetValue(id, out var info))
			{
				continue;
			}

			if (info.Instances is not null && (state.Fired ?? 0) > 0)
			{
				return false;
			}

			if (info.Iterations is not null)
			{
				if (info.ResetUnit is null)
				{
					if ((state.Iters ?? 0) > 0)
					{
						return false; // global *x: never resettable once fired
					}
				}
				else if (state.CycleKey is not null)
				{
					var scope = _calendar.SnapToStart(matchTime, info.ResetUnit.Value);
					if (scope <= state.CycleKey)
					{
						return false; // still inside the counted cycle
					}
				}
			}
		}

		return true;
	}

	/// <summary>
	/// Whether the engine is currently at an entropic reset boundary — i.e. its state
	/// reduces to (epoch=last entry, seed) with no counters to carry.
	/// </summary>
	public bool IsAtResetPoint()
	{
		if (_spanFormat)
		{
			return false;
		}

		if (_counters.Count == 0)
		{
			return true;
		}

		var t = PeekNext();
		return t is null || AtResetPoint(t.Value); // null => nothing left to resolve
	}

	// The time of the next actually-emitted entry (respecting @x/*x budgets), without
	// disturbing live state.
	private long? PeekNext()
	{
		var savedCursor = _cursor;
		var savedEmittedCount = _emittedCount;
		var savedExhausted = _exhausted;
		var savedLastEmitted = _lastEmitted;
		var savedCounters = _counters;
		var savedDeadNodes = _deadNodes;
		var savedAnchor = _anchor;
		var savedAutoReset = _autoReset;

		// Deep-clone the counter states — charging mutates them in place, and we must
		// not touch the live ones.
		_counters = savedCounters.ToDictionary(pair => pair.Key, pair => pair.Value.Clone());
		_deadNodes = [.. savedDeadNodes];
		_autoReset = false;
		try
		{
			var resolution = Advance(long.MaxValue);
			return resolution?.TimestampMs;
		}
		finally
		{
			_cursor = savedCursor;
			_emittedCount = savedEmittedCount;
			_exhausted = savedExhausted;
			_lastEmitted = savedLastEmitted;
			_counters = savedCounters;
			_deadNodes = savedDeadNodes;
			_anchor = savedAnchor;
			_autoReset = savedAutoReset;
		}
	}

	private bool SolveNext(OrbitAstNode node, Cursor cursor, OrbitUnit? parentUnit)
	{
		if (node is OrbitSetOperationNode set)
		{
			return SolveSetOperation(set, cursor, parentUnit);
		}

		var unitNode = (OrbitTimeUnitNode)node;
		// A node whose budget is spent produces nothing; inside a set operation this
		// naturally lets sibling branches carry on, and on the spine it exhausts.
		if (_deadNodes.Contains(_nodeId[unitNode]))
		{
			return false;
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
				return true;
			}

			// 3. Drill down into deeper nested structural nodes.
			var childCursor = new Cursor(cursor.Ms);
			var childSuccess = SolveNext(node.Child, childCursor, node.Unit);

			// Verify the child match didn't bleed outside this parent's window.
			if (childSuccess && IsSameContext(node.Unit, cursor.Ms, childCursor.Ms, parentUnit))
			{
				cursor.Ms = childCursor.Ms;
				return true;
			}

			// --- Try-and-Fail Backtracking ---
			// The child couldn't be satisfied in this period; step THIS layer
			// forward by 1 (or its interval) and retry.
			var step = node.Interval is > 1 ? node.Interval.Value : 1;
			var currentVal = _calendar.Get(cursor.Ms, node.Unit, parentUnit);
			var nextVal = currentVal + step;

			if (nextVal > UnitMax(node.Unit, parentUnit, cursor.Ms))
			{
				return false;
			}

			cursor.Ms = UnitSet(cursor.Ms, node.Unit, nextVal, parentUnit);
			cursor.Ms = _calendar.SnapToStart(cursor.Ms, node.Unit);
			attempts++;
		}

		return false;
	}

	private bool AlignUnit(OrbitTimeUnitNode node, Cursor cursor, OrbitUnit? parentUnit)
	{
		var unit = node.Unit;
		var currentVal = _calendar.Get(cursor.Ms, unit, parentUnit);
		var maxVal = UnitMax(unit, parentUnit, cursor.Ms);
		var interval = node.Interval is > 1 ? node.Interval.Value : 1;
		var bases = ResolveBases(node, cursor.Ms, parentUnit);

		// Fast path: pure whitelist. Jump straight to the next allowed value
		// >= currentVal instead of scanning every value.
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
					cursor.Ms = UnitSet(cursor.Ms, unit, b, parentUnit);
				}

				return true;
			}

			return false;
		}

		for (var val = currentVal; val <= maxVal; val++)
		{
			// Local offset loop (e.g. h{4}%3 -> every 3h starting from hour 4).
			if (interval > 1 && bases is not null)
			{
				var matched = false;
				foreach (var b in bases)
				{
					if (val >= b && (val - b) % interval == 0)
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
				var projected = UnitSet(cursor.Ms, unit, val, parentUnit);
				if (Math.Abs(_calendar.Delta(projected, _anchor, unit)) % interval != 0)
				{
					continue;
				}
			}

			if (val != currentVal)
			{
				cursor.Ms = UnitSet(cursor.Ms, unit, val, parentUnit);
			}

			return true;
		}

		return false; // Exhausted this unit; forces the parent loop to roll over.
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
		var lo = UnitMin(unit, parentUnit, cursorMs);
		var hi = UnitMax(unit, parentUnit, cursorMs);

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

				// @x / *x are per-node running counters, tracked via provenance.
			}

			if (unitNode.Child is not null)
			{
				Visit(unitNode.Child);
			}
		}

		Visit(node);
		return output;
	}

	// --- Node identity, limit indexing, provenance ---

	// Walks the RAW (un-normalized) AST recording, for every node bearing an @x/*x
	// limit, its tightest counts and the unit of its nearest explicit ancestor
	// (the scope a *x resets per; null => the node is a root, so *x is global).
	private void IndexLimits(OrbitAstNode node, OrbitUnit? explicitParent)
	{
		if (node is OrbitSetOperationNode set)
		{
			// A set operation doesn't introduce a nesting level of its own; its
			// operands share the enclosing explicit parent.
			IndexLimits(set.Left, explicitParent);
			IndexLimits(set.Right, explicitParent);
			return;
		}

		var unitNode = (OrbitTimeUnitNode)node;
		int? instances = null;
		int? iterations = null;
		foreach (var limit in unitNode.Limits)
		{
			if (limit.Kind == OrbitLimitKind.Instances)
			{
				instances = instances is null ? limit.Count : Math.Min(instances.Value, limit.Count);
			}

			if (limit.Kind == OrbitLimitKind.Iterations)
			{
				iterations = iterations is null ? limit.Count : Math.Min(iterations.Value, limit.Count);
			}
		}

		if (instances is not null || iterations is not null)
		{
			_limited[unitNode] = (instances, iterations, explicitParent);
		}

		if (unitNode.Child is not null)
		{
			IndexLimits(unitNode.Child, unitNode.Unit);
		}
	}

	// Assigns a stable positional id to every time-unit node via a pre-order walk of
	// the normalized AST. Deterministic parsing makes these ids match across resume.
	private void AssignIds(OrbitAstNode node)
	{
		if (node is OrbitSetOperationNode set)
		{
			AssignIds(set.Left);
			AssignIds(set.Right);
			return;
		}

		var unitNode = (OrbitTimeUnitNode)node;
		_nodeId[unitNode] = _nodeId.Count;
		if (unitNode.Child is not null)
		{
			AssignIds(unitNode.Child);
		}
	}

	// Rebuilds the dead-node set from restored counters: an @x node whose instances
	// budget is spent is permanently dead. (Global *x exhaustion is re-detected
	// lazily during resolution, so it needs no seeding here.)
	private void RecomputeDeadNodes()
	{
		_deadNodes.Clear();
		foreach (var (node, info) in _limited)
		{
			if (info.Instances is null)
			{
				continue;
			}

			if (_counters.TryGetValue(_nodeId[node], out var state) && (state.Fired ?? 0) >= info.Instances)
			{
				_deadNodes.Add(_nodeId[node]);
			}
		}
	}

	// The limited nodes an emission at `matchTime` passed through, in root->leaf
	// order, each with the id/counts and (for *x) the period + reset scope needed
	// to charge its counter. Only branches that actually produced the match descend.
	private List<PathEntry> CollectPath(OrbitAstNode node, long matchTime, OrbitUnit? parentUnit)
	{
		var output = new List<PathEntry>();
		void Walk(OrbitAstNode n, OrbitUnit? pUnit)
		{
			if (n is OrbitSetOperationNode set)
			{
				var left = Produces(set.Left, matchTime, pUnit);
				var right = Produces(set.Right, matchTime, pUnit);
				// Exclusion (A-B) / symmetric-difference credit only the producing
				// side; union/intersection credit whichever side(s) yielded the match.
				if (set.Operator == OrbitSetOperator.Exclusion)
				{
					if (left)
					{
						Walk(set.Left, pUnit);
					}

					return;
				}

				if (left)
				{
					Walk(set.Left, pUnit);
				}

				if (right && set.Operator != OrbitSetOperator.SymmetricDifference)
				{
					Walk(set.Right, pUnit);
				}
				else if (right && !left)
				{
					Walk(set.Right, pUnit);
				}

				return;
			}

			var unitNode = (OrbitTimeUnitNode)n;
			if (_limited.TryGetValue(unitNode, out var info))
			{
				output.Add(new PathEntry(
					_nodeId[unitNode],
					info.Instances,
					info.Iterations,
					info.ResetUnit,
					_calendar.SnapToStart(matchTime, unitNode.Unit)));
			}

			if (unitNode.Child is not null)
			{
				Walk(unitNode.Child, unitNode.Unit);
			}
		}

		Walk(node, parentUnit);
		return output;
	}

	// Does this subtree structurally produce exactly `matchTime`? Used to attribute
	// a match to the branch(es) responsible inside a set operation.
	private bool Produces(OrbitAstNode node, long matchTime, OrbitUnit? parentUnit)
	{
		var cursor = new Cursor(matchTime);
		return SolveNext(node, cursor, parentUnit) && cursor.Ms == matchTime;
	}
}
