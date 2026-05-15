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
	/// Plans an executive against a Polaris cycle.
	/// </summary>
	Task<PolarisExecutivePlanResult> PlanExecutiveAsync(PolarisExecutivePlan plan, string? polarisCycleId = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Applies an update to an executive record.
	/// </summary>
	Task<Executive> UpdateExecutiveAsync(long executiveId, ExecutiveUpdate update, CancellationToken cancellationToken = default);

	/// <summary>
	/// Draws reflectives for a Polaris cycle.
	/// </summary>
	Task<IReadOnlyList<Reflective>> DrawReflectivesAsync(ReflectiveDrawRequest request, CancellationToken cancellationToken = default);

	/// <summary>
	/// Applies an update to a reflective record.
	/// </summary>
	Task<Reflective> UpdateReflectiveAsync(long reflectiveId, ReflectiveUpdate update, CancellationToken cancellationToken = default);
}