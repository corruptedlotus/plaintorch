namespace Pleiades.Orchestration.Lifecycle;

/// <summary>
/// Supplies the lifecycle state of <see cref="Eventive"/> occurrences (PEP103). An eventive's passing is temporal, not
/// stateful ("an occurrence that simply happened stays Pending"), so its state combines its <see cref="EventiveResolution"/>
/// with its time window: a resolution that sets it aside (Missed, Cancelled, OptOut) defers it, and a pending one is open
/// until its start, started until its end, and finished after.
/// </summary>
/// <remarks>
/// A timed occurrence anchored in a time zone is compared at the instant it names (<see cref="Epoch.Instant"/>). A
/// floating one is compared naively (wall-clock UTC against the occurrence's date/time), matching the rest of the
/// codebase's direct <see cref="DateTimeOffset.UtcNow"/> usage. A deferring resolution gives a state independent of the
/// clock.
/// </remarks>
public sealed class EventiveLifecycleStateSource : ILifecycleStateSource
{
	/// <inheritdoc />
	public IReadOnlySet<LifecycleState> States { get; } = new HashSet<LifecycleState>
	{
		LifecycleState.Deferred,
		LifecycleState.Open,
		LifecycleState.Started,
		LifecycleState.Finished,
	};

	/// <inheritdoc />
	/// <remarks>Only Open and Deferred are reached by a resolution; time starts and finishes an occurrence.</remarks>
	public Type? StatusType => typeof(EventiveResolution);

	/// <inheritdoc />
	public bool Handles(Type entityType) => entityType == typeof(Eventive);

	/// <inheritdoc />
	public LifecycleState StateOf(object entity)
	{
		var eventive = (Eventive)entity;
		if (LifecycleDeclarations.StateOf(eventive.Resolution) == LifecycleState.Deferred)
		{
			return LifecycleState.Deferred;
		}

		// The window closes at its moment advanced by the duration, or by one granularity unit when it has no explicit span,
		// so an all-day occurrence finishes at the end of its day (Epoch.ClosesAt, the reading a fate's passing shares).
		var now = DateTimeOffset.UtcNow;
		if (eventive.Epoch.ClosesAt() <= now)
		{
			return LifecycleState.Finished;
		}

		return eventive.Epoch.OpensAt() <= now ? LifecycleState.Started : LifecycleState.Open;
	}
}
