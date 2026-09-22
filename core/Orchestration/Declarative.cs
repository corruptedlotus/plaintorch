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
	/// to the kind default (decrees Pleiadean, fates Gregorian) until an explicit calendar or user preference
	/// (PEP116) is chosen. Resolution-only: month/week/year boundaries shift with the calendar, but resolved
	/// occurrence instants are stored as concrete civil datetimes, so this never affects an occurrence's stored value.
	/// </summary>
	[MarkdownField("calendar")]
	public DeclarativeCalendar? Calendar { get; set; }

	/// <summary>
	/// Gets or sets the denormalized moment of this declarative's next upcoming occurrence — the earliest occurrence
	/// at or after the last refresh (PEP111), or <see langword="null"/> when the schedule has no upcoming occurrence
	/// (unscheduled, or a one-off that has passed). A civil (wall-clock) moment, matching <see cref="Epoch.Moment"/>.
	/// </summary>
	/// <remarks>
	/// A read-model cache, not a source of truth: it is recomputed from the schedule state whenever the declarative
	/// is saved (create, orbit edit, or a frontmatter sync) and advanced by the rolling harden pass as occurrences
	/// pass, so it is database-only and never written to frontmatter.
	/// </remarks>
	public DateTime? NextOccurrence { get; set; }
}
