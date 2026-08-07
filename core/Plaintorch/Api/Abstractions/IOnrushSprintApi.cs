using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Contracts;

namespace Pleiades.Plaintorch.Api.Abstractions;

/// <summary>
/// Defines the onrush sprint-facing application actions exposed by PLAINTORCH.
/// </summary>
public interface IOnrushSprintApi
{
	/// <summary>
	/// Plans an onrush sprint.
	/// </summary>
	Task<OnrushSprint> PlanAsync(OnrushSprintPlan plan, CancellationToken cancellationToken = default);

	/// <summary>
	/// Begins an onrush sprint.
	/// </summary>
	Task<OnrushSprint> BeginAsync(string onrushSprintId, DateOnly? startDate = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Creates and immediately begins a new onrush sprint.
	/// </summary>
	Task<OnrushSprint> StartNewAsync(DateOnly? startDate = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Ends an onrush sprint.
	/// </summary>
	Task<OnrushSprint> EndAsync(string onrushSprintId, DateOnly? endDate = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Deletes an onrush sprint outright: its milestone checkpoint and executive orders go with it, its
	/// tracked objectives and other checkpoints are detached (they survive on their own), and its note is
	/// removed.
	/// </summary>
	Task DeleteAsync(string onrushSprintId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Gets an onrush sprint by identifier, or the active sprint when no identifier is supplied.
	/// </summary>
	Task<OnrushSprint?> GetAsync(string? onrushSprintId = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Gets the current in-planning onrush sprint, when one exists.
	/// </summary>
	Task<OnrushSprint?> GetPlanningAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Gets available onrush sprints for immediate use (active and in-planning), when they exist.
	/// </summary>
	Task<IReadOnlyList<OnrushSprint>> GetAvailableAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Lists onrush sprints.
	/// </summary>
	Task<IReadOnlyList<OnrushSprint>> ListAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Updates the mutable fields of an onrush sprint.
	/// </summary>
	Task<OnrushSprint> UpdateAsync(string onrushSprintId, OnrushSprintUpdate update, CancellationToken cancellationToken = default);

	/// <summary>
	/// Persists the dependency-canvas layout of a sprint (PEP102): a JSON map of node key to position, or
	/// <see langword="null"/> to forget it. Database-only UI state, so it touches neither the vault nor the
	/// audit log — it is written often (after a drag) and losing it costs only a re-layout.
	/// </summary>
	Task SetGraphLayoutAsync(string onrushSprintId, string? graphLayout, CancellationToken cancellationToken = default);

	/// <summary>
	/// Assigns all onrush-state objectives to the target sprint.
	/// </summary>
	Task<IReadOnlyList<Objective>> AssignAllOnrushStateObjectivesToSelfAsync(string onrushSprintId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Issues a new executive order against an onrush sprint.
	/// </summary>
	Task<ExecutiveOrder> IssueExecutiveOrderAsync(string onrushSprintId, ExecutiveOrderPlan plan, CancellationToken cancellationToken = default);

	/// <summary>
	/// Lists the executive orders issued against an onrush sprint.
	/// </summary>
	Task<IReadOnlyList<ExecutiveOrder>> ListExecutiveOrdersAsync(string onrushSprintId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Updates the mutable fields of an executive order.
	/// </summary>
	Task<ExecutiveOrder> UpdateExecutiveOrderAsync(string executiveOrderId, ExecutiveOrderUpdate update, CancellationToken cancellationToken = default);

	/// <summary>
	/// Deletes an executive order and its markdown file.
	/// </summary>
	Task DeleteExecutiveOrderAsync(string executiveOrderId, CancellationToken cancellationToken = default);
}