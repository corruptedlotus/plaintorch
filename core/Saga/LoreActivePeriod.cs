namespace Pleiades.Saga;

/// <summary>
/// The span in which a lore page is active (<see cref="LorePage.ResolveActivePeriod"/>): from its beginning until the
/// day its period ends, exclusive, or still running when nothing has ended it.
/// </summary>
/// <param name="Beginning">The page's beginning, the first day of the period.</param>
/// <param name="EndingExclusive">The day the next period begins, or <see langword="null"/> while the period runs.</param>
public sealed record LoreActivePeriod(DateOnly Beginning, DateOnly? EndingExclusive)
{
	/// <summary>
	/// The last day inside the period: the day before it ends, or <paramref name="today"/> while it is still running.
	/// Earlier than <see cref="Beginning"/> when a running period has not begun yet.
	/// </summary>
	public DateOnly Through(DateOnly today) => EndingExclusive?.AddDays(-1) ?? today;
}
