namespace Pleiades.Orchestration;

/// <summary>
/// The mechanism a timeframe uses to auto-include Polaris workitems (PEP100 patch — Timeframe Auto-Inclusion).
/// </summary>
/// <remarks>
/// Auto-inclusion is the timeframe side of affinity: an executive or reflective created into a Polaris cycle is
/// automatically affined to any timeframe that opts to include it. The kind is a deliberate extension point —
/// further single-value criteria are meant to slot in beside the existing ones without reshaping the timeframe.
/// A kind reads its parameter from wherever it naturally lives: <see cref="College"/> reads the timeframe's own
/// <see cref="Timeframe.AutoInclusionColleges"/> column, while <see cref="Availability"/> has no parameter on the
/// timeframe at all — the choice lives on the directive (<see cref="Directive.AvailabilityTimeframeId"/>, PEP100
/// patch 2). The enum is stored as an integer, so members are only ever appended, never reordered.
/// </remarks>
public enum TimeframeInclusion
{
	/// <summary>The timeframe includes nothing automatically; affinity to it is set by hand only.</summary>
	None,

	/// <summary>
	/// The timeframe includes any workitem whose owning incentive belongs to a chosen college, read from
	/// <see cref="Timeframe.AutoInclusionColleges"/>.
	/// </summary>
	College,

	/// <summary>
	/// The timeframe is a directive availability (PEP100 patch 2): a directive picks it as its
	/// <see cref="Directive.AvailabilityTimeframeId"/>, and every workitem whose owning incentive belongs to that
	/// directive or one of its descendants is affined to it on creation. The nearest directive with an availability
	/// wins, and availability takes precedence over <see cref="College"/>.
	/// </summary>
	Availability,
}
