namespace Pleiades.Orchestration;

/// <summary>
/// Reads an occurrence's or a declarative's time zone (an IANA id): whether this host knows it, and what an instant
/// reads on its clock. A zone this host does not know counts as none — floating time, read on whatever clock reads it.
/// </summary>
public static class OccurrenceZones
{
	/// <summary>
	/// How far, in days, a civil moment on one zone's clock can sit from the same instant on another's: zones span
	/// UTC−12 to UTC+14, so never more than 26 hours. A query over moments stored on their own zones' clocks widens a
	/// window on this host's clock by this much on each side, then decides on the local reading.
	/// </summary>
	public const int ReachDays = 2;

	/// <summary>Finds the zone an id names on this host; <see langword="null"/> for no zone, or one this host does not know.</summary>
	public static TimeZoneInfo? Find(string? timeZone)
		=> !string.IsNullOrWhiteSpace(timeZone) && TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var zone) ? zone : null;

	/// <summary>
	/// Reads <paramref name="instant"/> as civil time on a zone's clock, or on this host's own when the zone is none or
	/// unknown — the "now" of a schedule that runs on that zone.
	/// </summary>
	public static DateTime Clock(string? timeZone, DateTimeOffset instant)
		=> Find(timeZone) is { } zone ? TimeZoneInfo.ConvertTime(instant, zone).DateTime : instant.LocalDateTime;
}
