using Pleiades.Orchestration;
using Pleiades.Saga;

namespace Pleiades.Plaintorch.Api.Contracts;

/// <summary>
/// Represents the system-facing briefing payload used by the Obsidian briefing surface.
/// </summary>
public sealed record SystemBriefing(
	string Status,
	DateTimeOffset Timestamp,
	string ActiveVaultPath,
	string PleiadeanToday,
	int CelestronBanked,
	string WatcherStatus,
	int WatcherIssueCount,
	int WatcherCriticalIssueCount,
	int WatcherCriteriaCount,
	int WatcherFailedCriteriaCount,
	string? OnrushSelectionMode,
	OnrushSprint? CurrentOnrush,
	PolarisCycle? CurrentPolaris,
	IReadOnlyList<LorePage> ActiveLorePages);

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
/// Represents a PUCK resolution payload for system-level entity lookup.
/// </summary>
/// <param name="Puck">The PUCK identifier being resolved.</param>
/// <param name="Exists">Indicates whether an entity exists for the provided PUCK.</param>
/// <param name="EntityType">The resolved entity type name when found.</param>
/// <param name="Entity">The resolved entity payload when found.</param>
/// <param name="AssociatedNote">The associated vault-relative markdown path when found and file-backed.</param>
public sealed record EntityExistence(
	string Puck,
	bool Exists,
	string? EntityType = null,
	object? Entity = null,
	string? AssociatedNote = null);

/// <summary>
/// Represents a watcher issue record exposed through system diagnostics APIs.
/// </summary>
public sealed record WatcherIssueRecord(
	string Key,
	string Type,
	string Category,
	string Message,
	bool IsCritical,
	string Criterion,
	string ResolutionCriterion,
	int OccurrenceCount,
	string? OriginPath,
	string? OriginVaultRelativePath,
	DateTimeOffset? FirstObservedUtc,
	DateTimeOffset? LastObservedUtc);

/// <summary>
/// Represents a watcher criterion evaluation exposed through system diagnostics APIs.
/// </summary>
public sealed record WatcherCriterionRecord(
	string Criterion,
	bool Satisfied,
	DateTimeOffset EvaluatedUtc,
	string? ScopeKey,
	string? OriginPath,
	string? OriginVaultRelativePath,
	string? Detail);

/// <summary>
/// Represents a full watcher diagnostics report including active issues and evaluated criteria.
/// </summary>
public sealed record WatcherIssueReport(
	string Status,
	int IssueCount,
	int CriticalIssueCount,
	int CriteriaCount,
	int FailedCriteriaCount,
	string? ScopedPath,
	bool ScopedPathIsDirectory,
	IReadOnlyList<WatcherIssueRecord> Issues,
	IReadOnlyList<WatcherCriterionRecord> Criteria);

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
/// Represents the mutable fields of a Polaris cycle for generic update actions.
/// </summary>
public sealed record PolarisCycleUpdate(
	string? Title = null);

/// <summary>
/// Represents the mutable fields of an onrush sprint for generic update actions.
/// </summary>
public sealed record OnrushSprintUpdate(
	string? Title = null,
	DateOnly? StartDate = null,
	DateOnly? EndDate = null);

/// <summary>
/// Represents the data required to plan an onrush sprint.
/// </summary>
public sealed record OnrushSprintPlan(
	string Title,
	DateOnly? StartDate = null,
	DateOnly? EndDate = null);

/// <summary>
/// Represents the data required to issue an executive order against an onrush sprint.
/// </summary>
public sealed record ExecutiveOrderPlan(
	string Title,
	string? Summary = null,
	DateOnly? EffectiveFrom = null,
	DateOnly? EffectiveUntil = null);

/// <summary>
/// Represents the mutable fields of an executive order for generic update actions.
/// </summary>
public sealed record ExecutiveOrderUpdate(
	string? Title = null,
	string? Summary = null,
	DateOnly? EffectiveFrom = null,
	DateOnly? EffectiveUntil = null);

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
/// <remarks>
/// The optional <paramref name="Estimation"/>, <paramref name="Minimum"/>, and <paramref name="Maximum"/> time
/// allocations are whole-minute working time units that are reconciled through <see cref="Executive.NormalizeTimeAllocations"/>.
/// </remarks>
public sealed record PolarisExecutivePlan(
	PolarisExecutivePlanningMode Mode,
	string? Title = null,
	string? ExecutiveTitle = null,
	string? DirectiveId = null,
	string? ObjectiveId = null,
	string? OnrushSprintId = null,
	ObjectiveCollege? College = null,
	int? CelestronValue = null,
	bool ObjectiveIsEnduring = false,
	int? Estimation = null,
	int? Minimum = null,
	int? Maximum = null);

/// <summary>
/// Represents the outcome of planning a Polaris executive.
/// </summary>
public sealed record PolarisExecutivePlanResult(Objective? Objective, Executive Executive);

/// <summary>
/// Represents a mutable update to an executive record.
/// </summary>
/// <remarks>
/// The time allocation fields carry whole-minute working time units. Each is paired with a
/// <c>Clear*</c> flag so a caller can distinguish "leave unchanged" (null) from "unset" (clear).
/// After the values are applied the record is reconciled through <see cref="Executive.NormalizeTimeAllocations"/>.
/// </remarks>
public sealed record ExecutiveUpdate(
	bool? Executed = null,
	string? ObjectiveId = null,
	bool ClearObjective = false,
	int? Estimation = null,
	int? Minimum = null,
	int? Maximum = null,
	bool ClearEstimation = false,
	bool ClearMinimum = false,
	bool ClearMaximum = false);

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