namespace Pleiades.Orchestration;

/// <summary>
/// Where an onrush sprint or a Polaris cycle stands in its run (PEP103 patch 4). Both are cycles of a kind, and they run the
/// same way: planned until they begin, running until they end, then ended. How they are planned differs — a sprint is
/// planned as the one placeholder (<see cref="OnrushSprint.PlanningPlaceholderId"/>), a cycle as a forecast for its own day —
/// so only this reading is shared, not their machinery.
/// </summary>
public enum CyclePhase
{
	/// <summary>
	/// Not begun: the planning placeholder, a forecast, or a cycle created and not yet begun.
	/// </summary>
	Planned,

	/// <summary>
	/// Begun and not ended.
	/// </summary>
	Active,

	/// <summary>
	/// Ended. An ended sprint or cycle never begins again, and its end is not moved (PEP103 patch 4).
	/// </summary>
	Ended,
}

/// <summary>
/// Reads the <see cref="CyclePhase"/> of sprints and cycles in one place.
/// </summary>
public static class CyclePhases
{
	/// <summary>
	/// The phase of a sprint. The planning placeholder is planned whatever dates it carries: they are the plan's.
	/// </summary>
	public static CyclePhase Of(OnrushSprint sprint)
	{
		ArgumentNullException.ThrowIfNull(sprint);
		return string.Equals(sprint.Id, OnrushSprint.PlanningPlaceholderId, StringComparison.OrdinalIgnoreCase)
			? CyclePhase.Planned
			: Of(sprint.StartDate is not null, sprint.EndDate is not null);
	}

	/// <summary>
	/// The phase of a Polaris cycle.
	/// </summary>
	public static CyclePhase Of(PolarisCycle cycle)
	{
		ArgumentNullException.ThrowIfNull(cycle);
		return Of(cycle.StartTime is not null, cycle.EndTime is not null);
	}

	private static CyclePhase Of(bool begun, bool ended) => ended ? CyclePhase.Ended : begun ? CyclePhase.Active : CyclePhase.Planned;
}
