using Pleiades.Orchestration;

namespace Pleiades.Plaintorch.Api.Transport;

/// <summary>
/// Represents one endpoint of a dependency in a transport payload (PEP101): a kind plus an id, optionally
/// qualified by an occurrence slot (iCalendar <c>RECURRENCE-ID</c>) for an eventive endpoint.
/// </summary>
public sealed record DependencyEndpointRequest(
	DependencyEndpointKind Kind,
	string Id,
	DateOnly? RecurrenceDate = null,
	TimeOnly? RecurrenceTime = null);

/// <summary>
/// Represents the transport payload used to create a dependency edge (source blocks target) (PEP101).
/// </summary>
public sealed record CreateDependencyRequest(
	DependencyEndpointRequest Source,
	DependencyEndpointRequest Target,
	DependencyTrigger? Trigger = null,
	DependencyConstraint? Constraint = null);

/// <summary>
/// Represents the transport payload used to create a checkpoint (PEP101).
/// </summary>
public sealed record CreateCheckpointRequest(
	string Title,
	string? Id = null,
	int? CelestronToll = null,
	bool? ExternalCondition = null);

/// <summary>
/// Represents the transport payload used to set a checkpoint's external condition switch (PEP101).
/// </summary>
public sealed record SetCheckpointConditionRequest(bool Met);

/// <summary>
/// Represents the transport payload used to create a directive.
/// </summary>
public sealed record CreateDirectiveRequest(
	string Title,
	string? Id = null,
	string? Codename = null,
	string? ParentDirectiveId = null);

/// <summary>
/// Represents the transport payload used to initialize a directive from an existing vault file.
/// </summary>
public sealed record InitDirectiveRequest(string Path);

/// <summary>
/// Represents the transport payload used to create a lunar (Moonlight) directive (PEP100).
/// </summary>
public sealed record CreateLunarDirectiveRequest(
	string Title,
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