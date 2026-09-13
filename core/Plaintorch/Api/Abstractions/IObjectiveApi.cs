using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Contracts;

namespace Pleiades.Plaintorch.Api.Abstractions;

/// <summary>
/// Defines the objective-facing application actions exposed by PLAINTORCH.
/// </summary>
public interface IObjectiveApi
{
	/// <summary>
	/// Gets an objective by identifier.
	/// </summary>
	Task<Objective?> GetAsync(string objectiveId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Lists objectives.
	/// </summary>
	Task<IReadOnlyList<Objective>> ListAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Finds objectives using a free-text query.
	/// </summary>
	Task<IReadOnlyList<Objective>> FindAsync(SearchRequest search, CancellationToken cancellationToken = default);

	/// <summary>
	/// Creates a standalone objective.
	/// </summary>
	Task<Objective> CreateStandaloneAsync(string title, string? requestedId = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Creates an objective beneath a directive.
	/// </summary>
	Task<Objective> CreateFromDirectiveAsync(string directiveId, string title, string? onrushSprintId = null, string? requestedId = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Applies a generic update to an objective.
	/// </summary>
	Task<Objective> UpdateAsync(string objectiveId, ObjectiveUpdate update, CancellationToken cancellationToken = default);

	/// <summary>
	/// Applies a workflow shift to an objective.
	/// </summary>
	Task<Objective> ShiftWorkflowAsync(string objectiveId, ObjectiveWorkflowShift shift, CancellationToken cancellationToken = default);

	/// <summary>
	/// Assigns an objective to an onrush sprint.
	/// </summary>
	Task<Objective> AddToOnrushAsync(string objectiveId, string onrushSprintId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Removes an objective from the onrush sprint it belongs to.
	/// </summary>
	Task<Objective> RemoveFromOnrushAsync(string objectiveId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Prompts an implicit objective to materialize its markdown file and begin its synchronization boundary.
	/// </summary>
	Task<Objective> BeginBoundaryAsync(string objectiveId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Initializes an objective from an existing user-authored vault file (the policy-derived <c>init</c> action):
	/// the file is adopted into a new implicit objective, minting an identity when it carries none.
	/// </summary>
	Task<Objective> InitializeFromPathAsync(string vaultRelativePath, CancellationToken cancellationToken = default);

	/// <summary>
	/// Deletes an objective.
	/// </summary>
	Task DeleteAsync(string objectiveId, CancellationToken cancellationToken = default);
}