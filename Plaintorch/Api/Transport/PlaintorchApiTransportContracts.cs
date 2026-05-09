namespace Pleiades.Plaintorch.Api.Transport;

/// <summary>
/// Represents the transport payload used to create a directive.
/// </summary>
public sealed record CreateDirectiveRequest(
	string Title,
	string? Id = null,
	string? Codename = null,
	string? ParentDirectiveId = null);

/// <summary>
/// Represents the transport payload used to create an objective.
/// </summary>
public sealed record CreateObjectiveRequest(
	string Title,
	string? Id = null,
	string? DirectiveId = null,
	string? OnrushSprintId = null,
	bool IsEnduring = false);

/// <summary>
/// Represents the transport payload used to assign an objective to onrush.
/// </summary>
public sealed record AddObjectiveToOnrushRequest(string OnrushSprintId);

/// <summary>
/// Represents the transport payload used to shift onrush sprint dates.
/// </summary>
public sealed record OnrushSprintDateRequest(DateOnly? Date = null);

/// <summary>
/// Represents the transport payload used to shift Polaris cycle times.
/// </summary>
public sealed record PolarisCycleTimeRequest(DateTimeOffset? Time = null);

/// <summary>
/// Represents the transport payload used to plan a forecast Polaris cycle.
/// </summary>
public sealed record PolarisCyclePlanRequest(DateOnly ForecastReference, int DaysAhead, string? Body = null);