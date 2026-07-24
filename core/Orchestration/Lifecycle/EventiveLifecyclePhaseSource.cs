namespace Pleiades.Orchestration.Lifecycle;

/// <summary>
/// Supplies lifecycle phases for <see cref="Eventive"/> occurrences (PEP101). An eventive's passing is
/// temporal, not stateful ("an occurrence that simply happened stays Pending"), so begin/finish combine its
/// terminal <see cref="EventiveResolution"/> with its time window rather than reading a status enum.
/// </summary>
/// <remarks>
/// The temporal comparison is naive (wall-clock UTC against the occurrence's date/time), matching the rest of
/// the codebase's direct <see cref="DateTimeOffset.UtcNow"/> usage. Terminal resolutions give a deterministic
/// finished signal independent of the clock.
/// </remarks>
public sealed class EventiveLifecyclePhaseSource : ILifecyclePhaseSource
{
	/// <inheritdoc />
	public bool Handles(Type entityType) => entityType == typeof(Eventive);

	/// <inheritdoc />
	public bool HasBegun(object entity)
	{
		var eventive = (Eventive)entity;
		return HasFinished(eventive) || OccurrenceStart(eventive) <= DateTimeOffset.UtcNow;
	}

	/// <inheritdoc />
	public bool HasFinished(object entity)
	{
		var eventive = (Eventive)entity;
		if (eventive.Resolution is EventiveResolution.Missed or EventiveResolution.Cancelled)
		{
			return true;
		}

		return OccurrenceEnd(eventive) <= DateTimeOffset.UtcNow;
	}

	private static DateTimeOffset OccurrenceStart(Eventive eventive)
	{
		var start = eventive.StartTime ?? TimeOnly.MinValue;
		return new DateTimeOffset(eventive.Date.ToDateTime(start), TimeSpan.Zero);
	}

	private static DateTimeOffset OccurrenceEnd(Eventive eventive)
	{
		if (eventive.EndTime is { } endTime)
		{
			return new DateTimeOffset(eventive.Date.ToDateTime(endTime), TimeSpan.Zero);
		}

		// All-day or open-ended occurrences finish at the end of their day.
		return new DateTimeOffset(eventive.Date.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
	}
}
