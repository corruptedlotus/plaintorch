using Pleiades.Orchestration;
using Pleiades.Saga;
using Pleiades.Vault.Media;

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
/// Represents a resolution payload for system-level entity lookup, produced by both PUCK identifier and
/// vault-note resolution. The payload is deliberately client-agnostic: surfaces derive their own presentation
/// concerns (such as custom-element tag names) from <see cref="EntityKind"/>.
/// </summary>
/// <param name="Puck">The PUCK identifier being resolved, or empty when a recognized note has no resolvable identity.</param>
/// <param name="Exists">Indicates whether the lookup resolves to a PLAINTORCH-backed entity.</param>
/// <param name="EntityType">The resolved entity CLR type name when found.</param>
/// <param name="EntityKind">The stable entity kind declared by the entity via its <c>PuckEntity</c> attribute when found.</param>
/// <param name="Entity">The resolved entity payload when found.</param>
/// <param name="AssociatedNote">The associated vault-relative markdown path when found and file-backed.</param>
public sealed record EntityExistence(
	string Puck,
	bool Exists,
	string? EntityType = null,
	string? EntityKind = null,
	object? Entity = null,
	string? AssociatedNote = null);

/// <summary>
/// Represents a watcher issue record exposed through system diagnostics APIs.
/// </summary>
/// <param name="Message">The reason's generic descriptor message (what this kind of issue means), resolved per request.</param>
/// <param name="Detail">
/// The specific detail of this occurrence — the policy's reason, an exception message, the offending files — or
/// <see langword="null"/> when the reason alone says it all. Kept apart from <paramref name="Message"/> so a client can
/// present, fold, or hide it independently.
/// </param>
public sealed record WatcherIssueRecord(
	string Key,
	string Type,
	string Category,
	string Message,
	string? Detail,
	bool IsCritical,
	string Severity,
	IReadOnlyList<string> Files,
	string Criterion,
	string ResolutionCriterion,
	int OccurrenceCount,
	string? OriginPath,
	string? OriginVaultRelativePath,
	DateTimeOffset? FirstObservedUtc,
	DateTimeOffset? LastObservedUtc,
	bool Dismissed = false);

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
/// Represents a request to dismiss or restore a watcher issue (PEP108 dismiss feature), identified by its opaque
/// <see cref="WatcherIssueRecord.Key"/>. <see cref="Scope"/> selects the breadth — <c>instance</c> (default), or the
/// reserved <c>file</c> / <c>reason</c> broader-snooze scopes.
/// </summary>
public sealed record WatcherIssueDismissalRequest(string Key, string? Scope = null);

/// <summary>
/// Represents an in-place edit of a lore page's mutable metadata. Its hierarchy (level and narrative index) stays
/// path-derived and is not editable here; the title (which renames the self-named folder) and the frontmatter-backed
/// beginning date are.
/// </summary>
public sealed record LorePageUpdate(
	Optional<string> Title = default,
	Optional<DateOnly?> Beginning = default);

/// <summary>
/// Represents a request to create a lore page. The level is derived from the parent (a null parent creates an Era);
/// the narrative index and PUCK identity are composed by the core from the parent and existing siblings.
/// </summary>
public sealed record LorePageCreateRequest(
	string? ParentPuck,
	string Title,
	DateOnly? Beginning = null);

/// <summary>
/// Represents a request to renumber a lore page to a new index at its own level, re-keying it and its descendants.
/// </summary>
public sealed record LorePageRenumberRequest(
	string Puck,
	int Index);

/// <summary>
/// Represents a generic text search request used by list/find style API actions.
/// </summary>
/// <param name="Query">The query text to search for.</param>
/// <param name="Take">An optional maximum result count.</param>
public sealed record SearchRequest(string Query, int? Take = null);

/// <summary>
/// A searchable activity — something that can be added to a Polaris cycle — unifying the two incentive kinds a
/// cycle accepts: an objective (which becomes an executive) or a decree (which materializes an attentive). Exactly
/// one of <paramref name="Objective"/> / <paramref name="Decree"/> is populated, matching <paramref name="Kind"/>.
/// </summary>
/// <param name="Kind">The discriminator: <c>objective</c> or <c>decree</c>.</param>
/// <param name="Objective">The objective, when <paramref name="Kind"/> is <c>objective</c>.</param>
/// <param name="Decree">The decree, when <paramref name="Kind"/> is <c>decree</c>.</param>
public sealed record Activity(string Kind, Objective? Objective = null, Decree? Decree = null)
{
	/// <summary>The activity's title, taken from whichever incentive it wraps — used for ordering and display.</summary>
	public string Title => Objective?.Title ?? Decree?.Title ?? string.Empty;

	/// <summary>Wraps an objective as an activity.</summary>
	public static Activity ForObjective(Objective objective) => new("objective", Objective: objective);

	/// <summary>Wraps a decree as an activity.</summary>
	public static Activity ForDecree(Decree decree) => new("decree", Decree: decree);
}

/// <summary>
/// Identifies which directive kind an action targets or filters (PEP100).
/// </summary>
public enum DirectiveKind
{
	/// <summary>
	/// A stellar (lifecycle-driven) directive.
	/// </summary>
	Stellar,

	/// <summary>
	/// A lunar (Moonlight everglow) directive.
	/// </summary>
	Lunar,
}

/// <summary>
/// Represents the mutable fields of a stellar directive for update actions, including its scheduling dates.
/// </summary>
public sealed record StellarDirectiveUpdate(
	string? Title = null,
	Optional<string?> Codename = default,
	Optional<string?> ParentDirectiveId = default,
	IReadOnlyList<string>? Tags = null,
	Optional<DateOnly?> Due = default,
	Optional<DateOnly?> StartDate = default,
	Optional<DateOnly?> EndDate = default);

/// <summary>
/// Represents the mutable fields of a lunar directive for update actions. Lunar directives are everglow and
/// therefore carry no scheduling dates (PEP100).
/// </summary>
public sealed record LunarDirectiveUpdate(
	string? Title = null,
	Optional<string?> Codename = default,
	Optional<string?> ParentDirectiveId = default,
	IReadOnlyList<string>? Tags = null);

/// <summary>
/// Represents a stellar directive workflow shift.
/// </summary>
/// <param name="Status">The new stellar directive status.</param>
public sealed record StellarDirectiveWorkflowShift(DirectiveStatus Status);

/// <summary>
/// Carries an uploaded media file for a directive icon or banner (PEP105). The bytes travel as Base64 because
/// the loopback transport carries JSON only and has no multipart support.
/// </summary>
/// <param name="FileName">The file name to store the asset under, including its image extension.</param>
/// <param name="ContentBase64">The Base64-encoded file bytes.</param>
public sealed record MediaUpload(string FileName, string ContentBase64);

/// <summary>
/// The stored key a media-domain upload returns (PEP105): a <c>media:</c> file for an entity-level upload or a
/// <c>vault:</c> file for a vault-level one. A field references this key through its own domain's set-reference call
/// — upload and selection stay separate.
/// </summary>
/// <param name="Key">The stored media key.</param>
public sealed record MediaStoreResult(string Key);

/// <summary>
/// Selects a directive's icon (PEP105): a raw <paramref name="Reference"/> key — a glyph/lucide name, or a
/// <c>media:</c>/<c>vault:</c> file already stored through the media domain — or <paramref name="Clear"/> to remove
/// it and fall back to the per-kind default glyph. Storing a custom image is the media domain's concern, not this
/// one's; a field only ever references a key.
/// </summary>
/// <param name="Reference">A raw media key to set directly: a glyph/lucide name, <c>media:file</c>, or <c>vault:file</c>.</param>
/// <param name="Clear">Removes the icon, archiving any self-stored image it pointed at.</param>
public sealed record DirectiveIconRequest(
	string? Reference = null,
	bool Clear = false);

/// <summary>
/// Selects a directive's banner image (PEP105): a raw <paramref name="Reference"/> key already stored through the
/// media domain, or <paramref name="Clear"/> to remove it.
/// </summary>
/// <param name="Reference">A raw media key to set directly: <c>media:file</c> or <c>vault:file</c>.</param>
/// <param name="Clear">Removes the banner, archiving any self-stored image it pointed at.</param>
public sealed record DirectiveBannerRequest(
	string? Reference = null,
	bool Clear = false);

/// <summary>
/// Represents the mutable fields of an objective for generic update actions.
/// </summary>
/// <remarks>
/// <paramref name="ParentIncentiveId"/> participates in the PEP100 parent system: an objective may name
/// another objective (subtask) or a fate declarative as its parent. <paramref name="ClearParentIncentive"/>
/// distinguishes "leave unchanged" (null) from "unset".
/// </remarks>
public sealed record ObjectiveUpdate(
	string? Title = null,
	Optional<string?> DirectiveId = default,
	string? OnrushSprintId = null,
	ObjectiveCollege? College = null,
	int? CelestronValue = null,
	Optional<Due?> Due = default,
	Optional<string?> ParentIncentiveId = default);

/// <summary>
/// Represents the editable fields of a checkpoint (PEP102): its name, its Celestron toll, and its external
/// condition.
/// </summary>
/// <remarks>
/// The toll and the condition are each optional on the checkpoint (a null means it has none), so a nullable
/// value carries "set to this" while a paired <c>Clear…</c> flag carries "remove it"; null with the flag
/// unset means "leave unchanged". Requiring a condition where there was none is <c>ExternalCondition = false</c>
/// (present but unmet).
/// </remarks>
public sealed record CheckpointUpdate(
	string? Title = null,
	Optional<int?> CelestronToll = default,
	Optional<bool?> ExternalCondition = default);

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
	Optional<DateOnly?> StartDate = default,
	Optional<DateOnly?> EndDate = default);

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
	Optional<string?> Summary = default,
	Optional<DateOnly?> EffectiveFrom = default,
	Optional<DateOnly?> EffectiveUntil = default);

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
/// There is no elapsed-time input here: a freshly planned executive has not been worked yet, so its tracked
/// minutes always start at <c>0</c> and are only accrued later through <see cref="ExecutiveUpdate"/>.
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
/// The time allocation fields carry whole-minute working time units, each an <see cref="Optional{T}"/>: an
/// omitted key leaves it unchanged, an explicit value sets it, and an explicit null clears it.
/// After the values are applied the record is reconciled through <see cref="Executive.NormalizeTimeAllocations"/>.
/// <paramref name="Elapsed"/> is the raw tracked-minute tally: <c>null</c> leaves it unchanged and any
/// supplied value (including <c>0</c> to reset) overwrites it. It has no <c>Clear*</c> flag because it is never unset.
/// <paramref name="AffinityTimeframeId"/> names a timeframe as the executive's preferred execution window (PEP100);
/// affinity is purely semantic.
/// </remarks>
public sealed record ExecutiveUpdate(
	bool? Executed = null,
	string? ObjectiveId = null,
	Optional<int?> Estimation = default,
	Optional<int?> Minimum = default,
	Optional<int?> Maximum = default,
	int? Elapsed = null,
	Optional<long?> AffinityTimeframeId = default,
	string? MoveToPolarisCycleId = null);

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
	bool? Executed = null,
	Optional<TimeOnly?> Time = default);

/// <summary>
/// Represents a moonlight workflow shift for a lunar directive (PEP100).
/// </summary>
/// <param name="Status">The new lunar directive status.</param>
public sealed record LunarDirectiveWorkflowShift(LunarDirectiveStatus Status);

/// <summary>
/// Represents the data required to create a fate declarative (PEP100). A fate is stored orbit-only (PEP111): the
/// <paramref name="Date"/>/<paramref name="StartTime"/>/<paramref name="EndTime"/>/<paramref name="EventDuration"/>
/// fields are one-off creation sugar — when no explicit <paramref name="Orbit"/> is given they fold into a
/// fixed-datetime <c>Z{y/M/d[Th:m]}</c> literal (with a <c>=&lt;dur&gt;</c> span for a timed window). An explicit
/// <paramref name="Orbit"/> takes precedence and the one-off fields are ignored.
/// </summary>
public sealed record FatePlan(
	string Title,
	string? Id = null,
	string? DirectiveId = null,
	string? ParentIncentiveId = null,
	DateOnly? Date = null,
	TimeOnly? StartTime = null,
	TimeOnly? EndTime = null,
	string? Orbit = null,
	int? EventDuration = null);

/// <summary>
/// Represents the mutable fields of a fate declarative for generic update actions. A fate is orbit-only (PEP111),
/// so rescheduling — whether a one-off or a recurrence — is done by setting a new <paramref name="Orbit"/>
/// (a <c>Z{…}</c> literal for a one-off).
/// </summary>
public sealed record FateUpdate(
	string? Title = null,
	FateStatus? Status = null,
	Optional<string?> DirectiveId = default,
	Optional<string?> ParentIncentiveId = default,
	string? Orbit = null);

/// <summary>
/// Represents the data required to create a decree declarative (PEP100).
/// </summary>
public sealed record DecreePlan(
	string Title,
	string? Id = null,
	string? DirectiveId = null,
	string? Orbit = null,
	int? DefaultLength = null,
	int ActiveCelestron = 0,
	bool Reflect = false);

/// <summary>
/// Represents the mutable fields of a decree declarative for generic update actions.
/// </summary>
public sealed record DecreeUpdate(
	string? Title = null,
	DecreeStatus? Status = null,
	Optional<string?> DirectiveId = default,
	string? Orbit = null,
	int? DefaultLength = null,
	int? ActiveCelestron = null,
	bool? Reflect = null);

/// <summary>
/// Represents the caller-supplied occurrence details when interacting with a fate or an objective due date
/// to materialize an eventive (PEP100).
/// </summary>
public sealed record EventiveMaterialization(
	DateOnly? Date = null,
	TimeOnly? StartTime = null,
	TimeOnly? EndTime = null);

/// <summary>
/// Represents a mutable update to an eventive occurrence. Eventives are never Polaris-bound, so their time
/// specification can always be moved.
/// </summary>
public sealed record EventiveUpdate(
	DateOnly? Date = null,
	Optional<TimeOnly?> StartTime = default,
	Optional<TimeOnly?> EndTime = default,
	EventiveResolution? Resolution = null);

/// <summary>
/// Represents the caller-supplied occurrence details when interacting with a decree to materialize an
/// unbound attentive (PEP111). Occurrences carry no time allocation — allocation lives on the executive.
/// </summary>
public sealed record AttentiveMaterialization(
	DateOnly? Date = null,
	TimeOnly? Time = null);

/// <summary>
/// Represents a mutable update to an attentive occurrence. An attentive is always unbound (PEP111), so
/// <paramref name="Date"/> reschedules it freely.
/// </summary>
public sealed record AttentiveUpdate(
	DateOnly? Date = null,
	Optional<TimeOnly?> Time = default,
	AttentiveResolution? Resolution = null,
	Optional<long?> AffinityTimeframeId = default);

/// <summary>
/// Addresses a single eventive occurrence by its owner UID (a fate or objective id) and RECURRENCE-ID
/// (Strategy 1): the recurrence-id resolves a projected occurrence and its hardened twin identically, so an
/// interaction hardens the occurrence and applies to it without ever needing a database row id.
/// </summary>
public sealed record EventiveOccurrenceRef(
	string OwnerId,
	DateOnly RecurrenceDate,
	TimeOnly? RecurrenceTime = null);

/// <summary>
/// Addresses a single attentive occurrence by its decree + RECURRENCE-ID (<paramref name="DecreeId"/> +
/// <paramref name="RecurrenceDate"/> + <paramref name="RecurrenceTime"/>): the recurrence-id resolves a projected
/// occurrence and its hardened twin identically, so an interaction hardens it without needing a row id. Attentives
/// are always unbound occurrences (PEP111); a decree placed into a cycle is an <see cref="Executive"/>, addressed
/// by its own row id.
/// </summary>
public sealed record AttentiveOccurrenceRef(
	string? DecreeId = null,
	DateOnly? RecurrenceDate = null,
	TimeOnly? RecurrenceTime = null);

/// <summary>
/// The request body for updating an eventive occurrence over the wire: the occurrence to address plus the
/// update to apply.
/// </summary>
public sealed record EventiveUpdateRequest(EventiveOccurrenceRef Occurrence, EventiveUpdate Update);

/// <summary>
/// The request body for updating an attentive occurrence over the wire: the occurrence to address plus the
/// update to apply.
/// </summary>
public sealed record AttentiveUpdateRequest(AttentiveOccurrenceRef Occurrence, AttentiveUpdate Update);

/// <summary>
/// Represents the data required to add a decree to a Polaris cycle, creating a decree-backed
/// <see cref="Executive"/> (PEP111). The cycle is the temporal context, so no occurrence date/time is carried.
/// </summary>
public sealed record PolarisDecreeAdd(
	string DecreeId,
	int? Estimation = null,
	int? Minimum = null,
	int? Maximum = null,
	long? AffinityTimeframeId = null);

/// <summary>
/// Represents the unbound items a Polaris cycle includes non-structurally because they fall within 24h of
/// its beginning (PEP100). The cycle never relationally owns these records.
/// </summary>
public sealed record PolarisCycleInclusions(
	IReadOnlyList<Eventive> Eventives,
	IReadOnlyList<Attentive> Attentives);

/// <summary>
/// Represents the day-level agenda: unbound attentives requiring attention (same-day/24h and previous
/// unattended) and upcoming eventives within a short horizon. Computed relative to today, independent of
/// any Polaris cycle (PEP100).
/// </summary>
public sealed record PolarisAgenda(
	IReadOnlyList<Attentive> Attentives,
	IReadOnlyList<Eventive> Eventives);

/// <summary>
/// Represents the data required to define a directive-level timeframe (PEP100). <paramref name="Icon"/> and the
/// auto-inclusion fields are the PEP100 patch additions.
/// </summary>
public sealed record TimeframePlan(
	string Title,
	TimeOnly StartTime,
	TimeOnly EndTime,
	string? Orbit = null,
	string? Icon = null,
	TimeframeInclusion AutoInclusion = TimeframeInclusion.None,
	IReadOnlyList<ObjectiveCollege>? AutoInclusionColleges = null);

/// <summary>
/// Represents the mutable fields of a timeframe definition. The auto-inclusion and icon fields are PEP100 patch
/// additions: <paramref name="AutoInclusion"/> and <paramref name="AutoInclusionColleges"/> are applied only when
/// supplied (a null college list leaves it unchanged; an empty one clears it), while the Orbit/Icon Optionals
/// distinguish keep/set/clear.
/// </summary>
public sealed record TimeframeUpdate(
	string? Title = null,
	TimeOnly? StartTime = null,
	TimeOnly? EndTime = null,
	Optional<string?> Orbit = default,
	Optional<string?> Icon = default,
	TimeframeInclusion? AutoInclusion = null,
	IReadOnlyList<ObjectiveCollege>? AutoInclusionColleges = null);

/// <summary>
/// Represents the emitted dependency lock for an entity (PEP101), computed from its unsatisfied incoming
/// dependencies. It is deliberately separate from the entity's own status field.
/// </summary>
/// <param name="EntityId">The blocked entity id (or eventive owner id).</param>
/// <param name="BlockedBegin">Whether an unmet begin-constraining dependency is currently blocking begin.</param>
/// <param name="BlockedFinish">Whether an unmet finish-constraining dependency is currently blocking finish.</param>
/// <param name="Unsatisfied">The unsatisfied incoming dependencies.</param>
public sealed record DependencyLockView(
	string EntityId,
	bool BlockedBegin,
	bool BlockedFinish,
	IReadOnlyList<Dependency> Unsatisfied);

/// <summary>
/// A candidate endpoint for a dependency, kind-tagged so a picker can tell one from another (PEP102). A
/// lightweight projection — id, title, and kind — over the entity kinds that may participate in a dependency:
/// stellar directives, objectives, and fates. Lunar directives, decrees, and Polaris-level records are not
/// endpoints and never appear here.
/// </summary>
/// <param name="Kind">Which endpoint kind this candidate is.</param>
/// <param name="Id">The candidate's PUCK id.</param>
/// <param name="Title">The candidate's title.</param>
public sealed record EndpointHit(
	DependencyEndpointKind Kind,
	string Id,
	string Title);

/// <summary>
/// Represents a timeframe together with a summary of the lunar directive that defines it, used by the global
/// timeframe listing that spans every lunar directive (PEP100).
/// </summary>
/// <param name="Id">The timeframe database identity.</param>
/// <param name="DirectiveId">The owning lunar directive identifier.</param>
/// <param name="DirectiveTitle">The owning lunar directive title.</param>
/// <param name="DirectiveCodename">The owning lunar directive codename, when set.</param>
/// <param name="DirectiveStatus">The owning lunar directive moonlight status.</param>
/// <param name="Title">The human-readable timeframe title.</param>
/// <param name="StartTime">The start of the flagged portion of the day.</param>
/// <param name="EndTime">The end of the flagged portion of the day.</param>
/// <param name="Orbit">The optional Orbit notation scoping the timeframe to particular Polaris cycles.</param>
/// <param name="Icon">The optional icon key (PEP100 patch).</param>
/// <param name="AutoInclusion">How the timeframe auto-includes Polaris workitems (PEP100 patch).</param>
/// <param name="AutoInclusionColleges">The colleges driving college-based auto-inclusion (PEP100 patch).</param>
public sealed record DirectiveTimeframeRecord(
	long Id,
	string DirectiveId,
	string DirectiveTitle,
	string? DirectiveCodename,
	LunarDirectiveStatus DirectiveStatus,
	string Title,
	TimeOnly StartTime,
	TimeOnly EndTime,
	string? Orbit,
	[property: Media] string? Icon,
	TimeframeInclusion AutoInclusion,
	IReadOnlyList<ObjectiveCollege> AutoInclusionColleges)
{
	/// <summary>
	/// Gets or sets the resolved companion of <see cref="Icon"/>, filled after the record is projected (its LINQ
	/// projection cannot call the media service). Never persisted.
	/// </summary>
	public MediaReference? IconMedia { get; set; }
}