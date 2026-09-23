namespace Pleiades.Orbits;

/// <summary>
/// A concrete occurrence instance of an orbit schedule, projected into the calendar-date shape the
/// PLAINTORCH declarative ecosystem materializes (PEP100).
/// </summary>
/// <param name="Date">The occurrence's start date — together with <paramref name="StartTime"/> this is the instance identity.</param>
/// <param name="StartTime">The time of day for sub-day granularities and span starts; null for day-or-coarser instants.</param>
/// <param name="EndTime">The same-day end time for span occurrences; null otherwise (or when a span crosses days).</param>
/// <param name="DurationMinutes">The span length in whole minutes, when the schedule is span-format.</param>
/// <param name="PeriodEndExclusive">The exclusive end date of the occurrence's period. Super-day granularities
/// (week/month/year) span multiple days, so multiple Polaris cycles can collide with one instance.</param>
/// <param name="Granularity">The schedule's finest unit — the accuracy the occurrence stands for (its
/// don't-care window): a day-granular occurrence occupies a whole day, an hour-granular one an hour, and so on.</param>
public sealed record OrbitOccurrenceInstance(
	DateOnly Date,
	TimeOnly? StartTime,
	TimeOnly? EndTime,
	int? DurationMinutes,
	DateOnly PeriodEndExclusive,
	OrbitUnit Granularity)
{
	/// <summary>
	/// Gets the occurrence's start as a civil moment — its date at its start time, or midnight for a
	/// day-or-coarser instant. This is the slot a materialized occurrence pins as its RECURRENCE-ID.
	/// </summary>
	public DateTime Moment => Date.ToDateTime(StartTime ?? TimeOnly.MinValue);
}

/// <summary>
/// The calendar-date view of orbit schedules used by the PLAINTORCH declarative ecosystem (PEP100).
/// </summary>
/// <remarks>
/// Two resolution modes exist and must not be confused:
/// <list type="bullet">
/// <item><b>Seeking</b> (<see cref="SeekOccurrencesThrough"/>): advances the persisted schedule state,
/// consuming every pending occurrence up to the window end (catch-up included) so nothing is resolved twice;
/// use it when materializing instances for a committed window.</item>
/// <item><b>Preview</b> (<see cref="PreviewOccurrencesWithin"/>/<see cref="MatchesDay"/>): trace-free
/// resolution as defined from the epoch; use it for lookahead and for validating a single (possibly future)
/// occurrence without pushing state forward. Because instances are keyed by their occurrence date (and time),
/// a previewed future instance is recognized — not duplicated — when the seeking pass later reaches it.</item>
/// </list>
/// The resolver calendar is modular: decree orbits (attentives) and reflective day-matching default to the
/// Pleiadean calendar, while fate orbits (eventives) keep the Gregorian calendar.
/// </remarks>
public static class OrbitDays
{
	/// <summary>The Gregorian resolver calendar (fate orbits / eventives).</summary>
	public static readonly OrbitGregorianCalendar Gregorian = new();

	/// <summary>The Pleiadean resolver calendar (decree orbits / attentives, and reflective matching).</summary>
	public static readonly OrbitPleiadeanCalendar Pleiadean = new();

	/// <summary>
	/// Validates that an orbit notation parses at all. Throws <see cref="FormatException"/> otherwise.
	/// </summary>
	public static void ValidateParses(string notation)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(notation);
		new OrbitParser(notation).Parse();
	}

	/// <summary>
	/// Validates an orbit notation for granular (non-span) use: it must parse and must not carry span
	/// durations. Any granularity — from years down to minutes — is allowed.
	/// </summary>
	public static void ValidateGranular(string notation)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(notation);
		var ast = new OrbitParser(notation).Parse();
		if (HasDuration(ast))
		{
			throw new FormatException("This orbit must be granular; span durations (=<dur>) are not allowed here.");
		}
	}

	/// <summary>
	/// Validates an orbit notation for strict day-granularity use (reflective schedules and timeframes):
	/// it must parse, must not be span-format, and must not resolve finer than a day.
	/// </summary>
	public static void ValidateDayGranularity(string notation)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(notation);
		var ast = new OrbitParser(notation).Parse();
		Walk(ast);

		static void Walk(OrbitAstNode node)
		{
			if (node is OrbitSetOperationNode set)
			{
				Walk(set.Left);
				Walk(set.Right);
				return;
			}

			if (node is OrbitDateTimeLiteralNode literal)
			{
				// A date-only Z{y/M/d} is day granularity (allowed); a z/Z carrying a time of day
				// resolves finer than a day, and a span duration is likewise disallowed here.
				if (literal.Hour is not null || literal.Minute is not null || literal.Second is not null)
				{
					throw new FormatException("This orbit resolves at day granularity; a z/Z time of day is not allowed.");
				}

				if (literal.Duration is { Count: > 0 })
				{
					throw new FormatException("This orbit resolves at day granularity; span durations (=<dur>) are not allowed.");
				}

				return;
			}

			var unitNode = (OrbitTimeUnitNode)node;
			if (unitNode.Unit is OrbitUnit.Hour or OrbitUnit.Minute or OrbitUnit.Second)
			{
				throw new FormatException($"This orbit resolves at day granularity; '{OrbitUnits.ToChar(unitNode.Unit)}' nodes are not allowed.");
			}

			if (unitNode.Duration is { Count: > 0 })
			{
				throw new FormatException("This orbit resolves at day granularity; span durations (=<dur>) are not allowed.");
			}

			if (unitNode.Child is not null)
			{
				Walk(unitNode.Child);
			}
		}
	}

	/// <summary>
	/// The calendar date a notation resolves to when it is a lone fixed-datetime literal (a one-off
	/// <c>Z{y/M/d[Th:m]}</c>, with or without a span), else <see langword="null"/>. A one-off's schedule cursor is
	/// anchored at this date rather than today, so its single occurrence is caught by a seek whether it lies in the
	/// past or the future. A recurring notation — or a bare time-of-day <c>z{h:m}</c>, which recurs daily and has no
	/// fixed date — returns <see langword="null"/> and anchors at today as usual.
	/// </summary>
	public static DateOnly? FixedLiteralDate(string notation)
	{
		if (string.IsNullOrWhiteSpace(notation))
		{
			return null;
		}

		OrbitAstNode ast;
		try
		{
			ast = new OrbitParser(notation).Parse();
		}
		catch (FormatException)
		{
			return null;
		}

		return ast is OrbitDateTimeLiteralNode { Year: { } year, Month: { } month, Day: { } day }
			? new DateOnly(year, month, day)
			: null;
	}

	/// <summary>
	/// Creates the initial persisted schedule state for a notation anchored at an epoch day.
	/// Span-format schedules carry no serializable engine state; their state is the epoch itself.
	/// </summary>
	public static string CreateState(string notation, DateOnly epoch, IOrbitCalendar calendar, uint? seed = null)
	{
		var engine = OrbitEngine.FromNotation(notation, ToMs(epoch), calendar, seed);
		if (IsSpanFormat(notation))
		{
			// Span engines cannot serialize a cursor; persist the compact identity blob instead.
			return new OrbitSnapshot
			{
				Notation = notation,
				Epoch = JsDate.ToIsoString(ToMs(epoch)),
				Seed = engine.Seed,
			}.ToJson();
		}

		return engine.Serialize().ToJson();
	}

	/// <summary>
	/// SEEKING: resolves every pending occurrence up to <paramref name="endExclusive"/> — including
	/// catch-up occurrences the schedule passed since the last seek — and advances the state.
	/// Returns the occurrences and the advanced state to persist.
	/// </summary>
	public static (IReadOnlyList<OrbitOccurrenceInstance> Occurrences, string AdvancedState) SeekOccurrencesThrough(
		string state, DateOnly endExclusive, IOrbitCalendar calendar)
		=> SeekThroughMs(state, ToMs(endExclusive), calendar);

	/// <summary>
	/// SEEKING to an exact instant: resolves and consumes every pending occurrence strictly before
	/// <paramref name="endExclusive"/> — an instant, not a whole day — and advances the state to it. This is how
	/// the harden-on-time pass catches up occurrences whose time has already arrived without also consuming the
	/// still-future occurrences of the same day.
	/// </summary>
	public static (IReadOnlyList<OrbitOccurrenceInstance> Occurrences, string AdvancedState) SeekOccurrencesThroughInstant(
		string state, DateTime endExclusive, IOrbitCalendar calendar)
		=> SeekThroughMs(state, ToMs(endExclusive), calendar);

	private static (IReadOnlyList<OrbitOccurrenceInstance> Occurrences, string AdvancedState) SeekThroughMs(
		string state, long endExclusiveMs, IOrbitCalendar calendar)
	{
		var snapshot = OrbitSnapshot.FromJson(state);
		if (IsSpanFormat(snapshot.Notation))
		{
			// Span engines re-resolve from the epoch; the cursor is tracked in the snapshot manually.
			return SeekSpansThroughMs(snapshot, endExclusiveMs, calendar);
		}

		var engine = OrbitEngine.Resume(snapshot, calendar);
		var entries = engine.NextWithin(long.MinValue, endExclusiveMs);
		return (ToOccurrences(entries, calendar), engine.Serialize().ToJson());
	}

	/// <summary>
	/// PREVIEW: resolves the occurrences in <c>[start, endExclusive)</c> as defined from the epoch,
	/// leaving the schedule state untouched.
	/// </summary>
	public static IReadOnlyList<OrbitOccurrenceInstance> PreviewOccurrencesWithin(
		string state, DateOnly start, DateOnly endExclusive, IOrbitCalendar calendar)
	{
		var snapshot = OrbitSnapshot.FromJson(state);
		var engine = ResumeForPreview(snapshot, calendar);
		return ToOccurrences(engine.ResolveWithin(ToMs(start), ToMs(endExclusive)), calendar);
	}

	/// <summary>
	/// PREVIEW: the moment of the earliest occurrence whose start is at or after <paramref name="from"/>, resolved
	/// from the schedule state without advancing it, or <see langword="null"/> when none falls within the lookahead
	/// horizon. This is the denormalized "next occurrence" the declarative caches.
	/// </summary>
	public static DateTime? NextOccurrence(string state, DateTime from, IOrbitCalendar calendar)
	{
		var fromDay = DateOnly.FromDateTime(from);
		// A year-plus horizon catches every occurrence up to a yearly cadence; a rarer super-annual schedule simply
		// has no cached next occurrence until it comes closer.
		var occurrences = PreviewOccurrencesWithin(state, fromDay, fromDay.AddDays(NextOccurrenceHorizonDays), calendar);
		DateTime? earliest = null;
		foreach (var occurrence in occurrences)
		{
			var moment = occurrence.Date.ToDateTime(occurrence.StartTime ?? TimeOnly.MinValue);
			if (moment >= from && (earliest is null || moment < earliest))
			{
				earliest = moment;
			}
		}

		return earliest;
	}

	/// <summary>The lookahead used when caching a declarative's next occurrence: a little over a year.</summary>
	private const int NextOccurrenceHorizonDays = 400;

	/// <summary>
	/// PREVIEW: whether the schedule has an occurrence whose period covers the given day. This is the
	/// day-granularity match rule (used, for example, to test a reflective decree's orbit against the day a
	/// Polaris cycle started). Super-day periods cover every day they span.
	/// </summary>
	public static bool MatchesDay(string state, DateOnly day, IOrbitCalendar calendar)
	{
		// Look back far enough that a super-day period starting earlier can still cover the day.
		var lookbackStart = day.AddDays(-400);
		return PreviewOccurrencesWithin(state, lookbackStart, day.AddDays(1), calendar)
			.Any(occurrence => occurrence.Date <= day && day < occurrence.PeriodEndExclusive);
	}

	private static (IReadOnlyList<OrbitOccurrenceInstance> Occurrences, string AdvancedState) SeekSpansThroughMs(
		OrbitSnapshot snapshot, long endMs, IOrbitCalendar calendar)
	{
		if (!JsDate.TryParseIso(snapshot.Epoch, out var epochMs))
		{
			throw new FormatException($"Orbit snapshot epoch '{snapshot.Epoch}' is not a valid instant.");
		}

		// The manual span cursor lives in the (otherwise unused) cursor slot.
		var cursorMs = epochMs;
		if (snapshot.Cursor is not null && JsDate.TryParseIso(snapshot.Cursor, out var storedCursor))
		{
			cursorMs = storedCursor;
		}

		var engine = OrbitEngine.FromNotation(snapshot.Notation, epochMs, calendar, (uint)snapshot.Seed);
		var entries = engine.ResolveWithin(cursorMs, endMs);
		snapshot.Cursor = JsDate.ToIsoString(Math.Max(cursorMs, endMs));
		return (ToOccurrences(entries, calendar), snapshot.ToJson());
	}

	private static OrbitEngine ResumeForPreview(OrbitSnapshot snapshot, IOrbitCalendar calendar)
	{
		if (!IsSpanFormat(snapshot.Notation))
		{
			return OrbitEngine.Resume(snapshot, calendar);
		}

		if (!JsDate.TryParseIso(snapshot.Epoch, out var epochMs))
		{
			throw new FormatException($"Orbit snapshot epoch '{snapshot.Epoch}' is not a valid instant.");
		}

		return OrbitEngine.FromNotation(snapshot.Notation, epochMs, calendar, (uint)snapshot.Seed);
	}

	private static bool IsSpanFormat(string notation)
	{
		return notation.Contains('=');
	}

	private static bool HasDuration(OrbitAstNode node)
	{
		if (node is OrbitSetOperationNode set)
		{
			return HasDuration(set.Left) || HasDuration(set.Right);
		}

		if (node is OrbitDateTimeLiteralNode literal)
		{
			return literal.Duration is { Count: > 0 };
		}

		var unitNode = (OrbitTimeUnitNode)node;
		if (unitNode.Duration is { Count: > 0 })
		{
			return true;
		}

		return unitNode.Child is not null && HasDuration(unitNode.Child);
	}

	private static long ToMs(DateOnly day)
	{
		return JsDate.DaysFromCivil(day.Year, day.Month, day.Day) * JsDate.MsPerDay;
	}

	private static long ToMs(DateTime localInstant)
	{
		// The engine works in calendar-naive civil milliseconds, and occurrence times of day round-trip as local
		// wall time, so a local wall-clock instant maps to civil midnight plus its time of day.
		return ToMs(DateOnly.FromDateTime(localInstant)) + (long)localInstant.TimeOfDay.TotalMilliseconds;
	}

	private static DateOnly ToDate(long ms)
	{
		var (year, month, day) = JsDate.CivilFromDays(JsDate.EpochDays(ms));
		return new DateOnly(year, month, day);
	}

	private static List<OrbitOccurrenceInstance> ToOccurrences(List<OrbitEntry> entries, IOrbitCalendar calendar)
	{
		var occurrences = new List<OrbitOccurrenceInstance>(entries.Count);
		foreach (var entry in entries)
		{
			switch (entry)
			{
				case OrbitResolutionEntry resolution:
				{
					var date = ToDate(resolution.TimestampMs);
					var timeOfDayMs = JsDate.TimeOfDayMs(resolution.TimestampMs);
					var startTime = resolution.Granularity is OrbitUnit.Hour or OrbitUnit.Minute or OrbitUnit.Second
						? TimeOnly.FromTimeSpan(TimeSpan.FromMilliseconds(timeOfDayMs))
						: (TimeOnly?)null;
					var periodEnd = resolution.Granularity is OrbitUnit.Year or OrbitUnit.Month or OrbitUnit.Week
						? ToDate(calendar.Add(calendar.SnapToStart(resolution.TimestampMs, resolution.Granularity), resolution.Granularity, 1))
						: date.AddDays(1);
					occurrences.Add(new OrbitOccurrenceInstance(date, startTime, null, null, periodEnd, resolution.Granularity));
					break;
				}
				case OrbitSpanEntry span:
				{
					var date = ToDate(span.StartMs);
					var startTime = TimeOnly.FromTimeSpan(TimeSpan.FromMilliseconds(JsDate.TimeOfDayMs(span.StartMs)));
					var endDate = ToDate(span.EndMs);
					var endTime = endDate == date
						? TimeOnly.FromTimeSpan(TimeSpan.FromMilliseconds(JsDate.TimeOfDayMs(span.EndMs)))
						: (TimeOnly?)null;
					var periodEnd = JsDate.TimeOfDayMs(span.EndMs) > 0 ? endDate.AddDays(1) : endDate;
					if (periodEnd <= date)
					{
						periodEnd = date.AddDays(1);
					}

					occurrences.Add(new OrbitOccurrenceInstance(
						date,
						startTime,
						endTime,
						(int)(span.DurationMs / 60000),
						periodEnd,
						span.Granularity));
					break;
				}
			}
		}

		return occurrences;
	}
}
