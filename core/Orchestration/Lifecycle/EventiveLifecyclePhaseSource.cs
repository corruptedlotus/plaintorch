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
		if (eventive.Resolution is EventiveResolution.Missed or EventiveResolution.Cancelled or EventiveResolution.OptOut)
		{
			return true;
		}

		return OccurrenceEnd(eventive) <= DateTimeOffset.UtcNow;
	}

	private static DateTimeOffset OccurrenceStart(Eventive eventive)
	{
		return new DateTimeOffset(eventive.Epoch.Moment, TimeSpan.Zero);
	}

	private static DateTimeOffset OccurrenceEnd(Eventive eventive)
	{
		// The occurrence's end is its moment advanced by the duration, or by one granularity unit when it has
		// no explicit span — so an all-day (day-granular) occurrence still finishes at the end of its day.
		return new DateTimeOffset(eventive.Epoch.EndMoment, TimeSpan.Zero);
	}
}
