using Pleiades.Orchestration;

namespace Pleiades.Plaintorch.Api.Contracts;

/// <summary>
/// Represents a compact system brief describing the currently active PLAINTORCH state.
/// </summary>
/// <param name="Timestamp">The service timestamp when the brief was produced.</param>
/// <param name="ActiveVaultPath">The currently active vault path.</param>
/// <param name="ActiveOnrushSprintId">The active onrush sprint identifier, when one exists.</param>
/// <param name="ActivePolarisCycleId">The active Polaris cycle identifier, when one exists.</param>
/// <param name="CelestronBanked">The currently banked Celestron total.</param>
public sealed record SystemBrief(
	DateTimeOffset Timestamp,
	string ActiveVaultPath,
	string? ActiveOnrushSprintId,
	string? ActivePolarisCycleId,
	int CelestronBanked);

/// <summary>
/// Represents the system-facing briefing payload used by the Obsidian briefing surface.
/// </summary>
public sealed record SystemBriefing(
	string Status,
	DateTimeOffset Timestamp,
	string ActiveVaultPath,
	string PleiadeanToday,
	int CelestronBanked,
	SystemBriefingOnrushSprint? CurrentOnrush,
	SystemBriefingPolarisCycle? CurrentPolaris);

/// <summary>
/// Represents an onrush sprint payload for the briefing surface.
/// </summary>
public sealed record SystemBriefingOnrushSprint(
	string SelectionMode,
	string Id,
	string Title,
	DateOnly? StartDate,
	DateOnly? EndDate,
	IReadOnlyList<SystemBriefingObjective> Objectives);

/// <summary>
/// Represents an objective summary for the briefing surface.
/// </summary>
public sealed record SystemBriefingObjective(
	string Id,
	string Title,
	string Status,
	string College,
	int CelestronValue,
	bool IsEnduring);

/// <summary>
/// Represents a Polaris cycle payload for the briefing surface.
/// </summary>
public sealed record SystemBriefingPolarisCycle(
	string Id,
	string Title,
	DateTimeOffset? StartTime,
	DateTimeOffset? EndTime,
	bool IsForecast,
	IReadOnlyList<SystemBriefingExecutive> Executives);

/// <summary>
/// Represents an executive summary for the briefing surface.
/// </summary>
public sealed record SystemBriefingExecutive(
	long Id,
	string? Title,
	bool Executed,
	string? ObjectiveId,
	string? ObjectiveTitle);

/// <summary>
/// Represents the authoritative PLAINTORCH interpretation of a vault markdown path.
/// </summary>
/// <param name="VaultRelativePath">The vault-relative markdown path that was resolved.</param>
/// <param name="IsPlaintorchEntity">Indicates whether the path resolves to a PLAINTORCH-backed entity.</param>
/// <param name="EntityKind">The normalized plugin-facing entity kind when the path is recognized.</param>
/// <param name="EntityName">The CLR/domain entity name when the path is recognized.</param>
/// <param name="TagName">The matching custom-element tag name when the path is recognized.</param>
/// <param name="Puck">The parsed PUCK token when one is present in the file identity.</param>
/// <param name="Title">The resolved entity title from the file identity.</param>
public sealed record VaultNoteAuthorityResolution(
	string VaultRelativePath,
	bool IsPlaintorchEntity,
	string? EntityKind = null,
	string? EntityName = null,
	string? TagName = null,
	string? Puck = null,
	string? Title = null);

/// <summary>
/// Represents a saga lore page payload exposed by the API.
/// </summary>
public sealed record LorePageRecord(
	string Puck,
	string Title,
	string? OverrideIdentifier,
	string? ParentPuck,
	DateOnly? Beginning,
	string Level,
	string RelativePath,
	int? Era,
	int? Chapter,
	int? Act,
	int? Phase,
	DateTimeOffset IndexedUtc);

/// <summary>
/// Represents a generic text search request used by list/find style API actions.
/// </summary>
/// <param name="Query">The query text to search for.</param>
/// <param name="Take">An optional maximum result count.</param>
public sealed record SearchRequest(string Query, int? Take = null);

/// <summary>
/// Represents the mutable fields of a directive for generic update actions.
/// </summary>
public sealed record DirectiveUpdate(
	string? Title = null,
	string? Codename = null,
	string? ParentDirectiveId = null,
	IReadOnlyList<string>? Tags = null,
	DateOnly? Due = null,
	string? AlternativeLoreDirectory = null,
	DateOnly? StartDate = null,
	DateOnly? EndDate = null);

/// <summary>
/// Represents a workflow shift for a directive.
/// </summary>
/// <param name="Status">The new directive status.</param>
public sealed record DirectiveWorkflowShift(DirectiveStatus Status);

/// <summary>
/// Represents the mutable fields of an objective for generic update actions.
/// </summary>
public sealed record ObjectiveUpdate(
	string? Title = null,
	string? DirectiveId = null,
	string? OnrushSprintId = null,
	ObjectiveCollege? College = null,
	int? CelestronValue = null,
	bool? IsEnduring = null);

/// <summary>
/// Represents a workflow shift for an objective.
/// </summary>
/// <param name="Status">The new objective status.</param>
public sealed record ObjectiveWorkflowShift(ObjectiveStatus Status);

/// <summary>
/// Represents the data required to plan an onrush sprint.
/// </summary>
public sealed record OnrushSprintPlan(
	string Title,
	DateOnly? StartDate = null,
	DateOnly? EndDate = null);

/// <summary>
/// Represents the supported sources for planning a Polaris executive.
/// </summary>
public enum PolarisExecutivePlanningMode
{
	/// <summary>
	/// Plans a one-shot executive with no durable objective.
	/// </summary>
	OneShot,

	/// <summary>
	/// Plans an executive with no pre-existing directive or objective context.
	/// </summary>
	Standalone,

	/// <summary>
	/// Plans an executive and creates a new objective beneath a directive.
	/// </summary>
	FromDirective,

	/// <summary>
	/// Plans an executive from an existing objective.
	/// </summary>
	FromObjective,
}

/// <summary>
/// Represents the data needed to plan a Polaris executive.
/// </summary>
public sealed record PolarisExecutivePlan(
	PolarisExecutivePlanningMode Mode,
	string? Title = null,
	string? ExecutiveTitle = null,
	string? DirectiveId = null,
	string? ObjectiveId = null,
	string? OnrushSprintId = null,
	ObjectiveCollege? College = null,
	int? CelestronValue = null,
	bool ObjectiveIsEnduring = false);

/// <summary>
/// Represents the outcome of planning a Polaris executive.
/// </summary>
public sealed record PolarisExecutivePlanResult(Objective? Objective, Executive Executive);

/// <summary>
/// Represents a mutable update to an executive record.
/// </summary>
public sealed record ExecutiveUpdate(
	bool? Executed = null,
	string? ObjectiveId = null,
	string? Title = null,
	bool ClearObjective = false);

/// <summary>
/// Represents the inputs used to draw reflectives for a Polaris cycle.
/// </summary>
public sealed record ReflectiveDrawRequest(
	string? PolarisCycleId = null,
	int Count = 1,
	bool IncludeRoutine = true,
	bool IncludeRandom = true);

/// <summary>
/// Represents a mutable update to a reflective record.
/// </summary>
public sealed record ReflectiveUpdate(
	string? Description = null,
	bool? Executed = null);