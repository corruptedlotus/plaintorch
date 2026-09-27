using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Contracts;

namespace Pleiades.Plaintorch.Api.Abstractions;

/// <summary>
/// Defines the Polaris cycle-facing application actions exposed by PLAINTORCH.
/// </summary>
public interface IPolarisCycleApi
{
	/// <summary>
	/// Plans a forecast Polaris cycle.
	/// </summary>
	Task<PolarisCycle> PlanAsync(DateOnly forecastReference, int daysAhead, string? body = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Begins a Polaris cycle.
	/// </summary>
	Task<PolarisCycle> BeginAsync(string? polarisCycleId = null, DateTimeOffset? startTime = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Creates and immediately begins a new Polaris cycle.
	/// </summary>
	Task<PolarisCycle> StartNewAsync(DateTimeOffset? startTime = null, string? body = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Ends a Polaris cycle.
	/// </summary>
	Task<PolarisCycle> EndAsync(string? polarisCycleId = null, DateTimeOffset? endTime = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Gets a Polaris cycle by identifier, or the active cycle when no identifier is supplied.
	/// </summary>
	Task<PolarisCycle?> GetAsync(string? polarisCycleId = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Lists forecast Polaris cycles with their assigned executives.
	/// </summary>
	Task<IReadOnlyList<PolarisCycle>> ListForecastsAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Updates the mutable fields of a Polaris cycle.
	/// </summary>
	Task<PolarisCycle> UpdateAsync(string polarisCycleId, PolarisCycleUpdate update, CancellationToken cancellationToken = default);

	/// <summary>
	/// Plans an executive against a Polaris cycle. The executive's affinity follows the plan's tri-state
	/// <see cref="PolarisExecutivePlan.AffinityTimeframeId"/> (PEP100 patch 2): omitted is Auto (the objective's
	/// nearest directive availability, else its college; none for a one-shot), <see langword="null"/> is none, and an
	/// id is used as given. The returned executive carries the resolved <see cref="Executive.AffinityTimeframe"/>.
	/// </summary>
	/// <exception cref="InvalidOperationException">An explicit affinity names a timeframe that does not exist.</exception>
	Task<PolarisExecutivePlanResult> PlanExecutiveAsync(PolarisExecutivePlan plan, string? polarisCycleId = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Applies an update to an executive record.
	/// </summary>
	Task<Executive> UpdateExecutiveAsync(long executiveId, ExecutiveUpdate update, CancellationToken cancellationToken = default);

	/// <summary>
	/// Defers an executive to the next day's Polaris cycle (PEP111), creating that day's forecast cycle when none
	/// exists yet. The carried-forward work becomes the new allocation envelope — estimation, minimum, and maximum
	/// are all set to the tracked (elapsed) minutes and the elapsed tally is reset to zero — so the next day starts
	/// from a fresh estimate of what is left. Refused when the target cycle already holds the same incentive.
	/// </summary>
	Task<Executive> MoveExecutiveToNextPolarisAsync(long executiveId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Gets the unbound eventives and attentives a Polaris cycle includes non-structurally because they fall
	/// within 24h of its beginning (PEP100).
	/// </summary>
	Task<PolarisCycleInclusions> GetInclusionsAsync(string? polarisCycleId = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Gets the day-level agenda relative to today: unbound attentives requiring attention, attentives resolved
	/// during the past hour, and upcoming eventives within a short horizon (PEP100).
	/// </summary>
	Task<PolarisAgenda> GetAgendaAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Removes an executive from its Polaris cycle, deleting the record and nothing else: its objective stays, in
	/// whatever state it has — the core advances backlog state on cycle participation and never walks it back.
	/// </summary>
	Task RemoveExecutiveAsync(long executiveId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Adds a decree to a Polaris cycle, creating a decree-backed executive (PEP111). Removal is through
	/// <see cref="RemoveExecutiveAsync"/>, like any other executive. The executive's affinity follows the request's
	/// tri-state <see cref="PolarisDecreeAdd.AffinityTimeframeId"/> (PEP100 patch 2): omitted is Auto (the decree's
	/// nearest directive availability, else its college), <see langword="null"/> is none, and an id is used as given.
	/// The returned executive carries the resolved <see cref="Executive.AffinityTimeframe"/>.
	/// </summary>
	/// <exception cref="InvalidOperationException">An explicit affinity names a timeframe that does not exist.</exception>
	Task<Executive> AddDecreeExecutiveAsync(PolarisDecreeAdd request, string? polarisCycleId = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Draws reflectives for a Polaris cycle.
	/// </summary>
	Task<IReadOnlyList<Reflective>> DrawReflectivesAsync(ReflectiveDrawRequest request, CancellationToken cancellationToken = default);

	/// <summary>
	/// Applies an update to a reflective record.
	/// </summary>
	Task<Reflective> UpdateReflectiveAsync(long reflectiveId, ReflectiveUpdate update, CancellationToken cancellationToken = default);
}