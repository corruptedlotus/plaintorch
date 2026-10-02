using Pleiades.Vault.Markdown;

namespace Pleiades.Orchestration;

/// <summary>
/// The non-hierarchical base of the declarative ecosystem's scheduling incentives (PEP100): fates and decrees.
/// A declarative drives occurrences from an Orbit rather than being acted on directly, and resolves that Orbit
/// against a <see cref="Calendar"/>. It is a table sibling of <see cref="Objective"/> on the shared incentive
/// table; unlike objectives, declaratives are never acted on directly — each occurrence materializes an
/// <see cref="Eventive"/> (fates) or <see cref="Attentive"/> (decrees) instead.
/// </summary>
public abstract class Declarative : Incentive
{
	/// <summary>
	/// Gets or sets the calendar this declarative's Orbit resolves against. <see langword="null"/> falls back
	/// to the vault default (<c>AgendaPreferences.DefaultCalendar</c>, PEP116). Resolution-only for a recurrence:
	/// month/week/year boundaries shift with the calendar, but resolved occurrence instants are stored as concrete
	/// civil datetimes, so this never affects an occurrence's stored value. A one-off <c>Z{y/M/d}</c> literal names a
	/// day on this calendar, so changing the calendar through the API rewrites the literal to keep its day.
	/// </summary>
	[MarkdownField("calendar")]
	public DeclarativeCalendar? Calendar { get; set; }

	/// <summary>
	/// Gets or sets the IANA time zone this declarative's schedule runs on, or <see langword="null"/> for wall/floating
	/// time. Its orbit is resolved in civil time on this zone's clock: every orbit-born occurrence is stamped with it
	/// (<see cref="Epoch.TimeZone"/>), so a timed one names the instant that clock reads, and the orbit is sought and
	/// counted from "now" as this zone reads it. It is also the zone an occurrence added by hand starts in. An
	/// occurrence already hardened keeps the zone it was stamped with.
	/// </summary>
	[MarkdownField("timezone")]
	public string? TimeZone { get; set; }

	/// <summary>
	/// Gets or sets the denormalized moment of this declarative's next upcoming occurrence — the earliest occurrence
	/// at or after the last refresh (PEP111), or <see langword="null"/> when the schedule has no upcoming occurrence
	/// (unscheduled, or a one-off that has passed). A civil (wall-clock) moment, matching <see cref="Epoch.Moment"/>: on
	/// the clock of <see cref="TimeZone"/> when the declarative has one.
	/// </summary>
	/// <remarks>
	/// A read-model cache, not a source of truth: it is recomputed from the schedule state whenever the declarative
	/// is saved (create, orbit edit, or a frontmatter sync) and advanced by the rolling harden pass as occurrences
	/// pass, so it is database-only and never written to frontmatter.
	/// </remarks>
	public DateTime? NextOccurrence { get; set; }
}
