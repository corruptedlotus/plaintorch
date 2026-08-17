namespace Pleiades.Orchestration;

/// <summary>
/// The mechanism a timeframe uses to auto-include Polaris workitems (PEP100 patch — Timeframe Auto-Inclusion).
/// </summary>
/// <remarks>
/// Auto-inclusion is the timeframe side of affinity: an executive or reflective created into a Polaris cycle is
/// automatically affined to any timeframe that opts to include it. The kind is a deliberate extension point —
/// <see cref="College"/> is the first (and, for now, only) criterion, but further single-value criteria are meant
/// to slot in beside it without reshaping the timeframe. Each new kind pairs with the parameter column it reads.
/// </remarks>
public enum TimeframeInclusion
{
	/// <summary>The timeframe includes nothing automatically; affinity to it is set by hand only.</summary>
	None,

	/// <summary>
	/// The timeframe includes any workitem whose owning incentive belongs to a chosen college, read from
	/// <see cref="Timeframe.AutoInclusionCollege"/>.
	/// </summary>
	College,
}
