using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Contracts;

namespace Pleiades.Plaintorch.Api.Abstractions;

/// <summary>
/// Defines the declarative-facing application actions exposed by PLAINTORCH (PEP100):
/// fates and decrees, plus the eventive and attentive occurrence instances they materialize.
/// </summary>
public interface IDeclarativeApi
{
	/// <summary>
	/// Gets a fate declarative by identifier.
	/// </summary>
	Task<Fate?> GetFateAsync(string fateId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Lists fate declaratives.
	/// </summary>
	Task<IReadOnlyList<Fate>> ListFatesAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Creates a fate declarative.
	/// </summary>
	Task<Fate> CreateFateAsync(FatePlan plan, CancellationToken cancellationToken = default);

	/// <summary>
	/// Applies a generic update to a fate declarative.
	/// </summary>
	Task<Fate> UpdateFateAsync(string fateId, FateUpdate update, CancellationToken cancellationToken = default);

	/// <summary>
	/// Deletes a fate declarative along with its materialized eventives.
	/// </summary>
	Task DeleteFateAsync(string fateId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Prompts an implicit fate to materialize its markdown file and begin its synchronization boundary.
	/// </summary>
	Task<Fate> BeginFateBoundaryAsync(string fateId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Gets a decree declarative by identifier.
	/// </summary>
	Task<Decree?> GetDecreeAsync(string decreeId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Lists decree declaratives.
	/// </summary>
	Task<IReadOnlyList<Decree>> ListDecreesAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Creates a decree declarative.
	/// </summary>
	Task<Decree> CreateDecreeAsync(DecreePlan plan, CancellationToken cancellationToken = default);

	/// <summary>
	/// Applies a generic update to a decree declarative.
	/// </summary>
	Task<Decree> UpdateDecreeAsync(string decreeId, DecreeUpdate update, CancellationToken cancellationToken = default);

	/// <summary>
	/// Deletes a decree declarative along with its materialized attentives.
	/// </summary>
	Task DeleteDecreeAsync(string decreeId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Prompts an implicit decree to materialize its markdown file and begin its synchronization boundary.
	/// </summary>
	Task<Decree> BeginDecreeBoundaryAsync(string decreeId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Materializes (or returns) the eventive for a fate occurrence — the interaction trigger.
	/// The created instance is never Polaris-bound.
	/// </summary>
	Task<Eventive> MaterializeEventiveAsync(string fateId, EventiveMaterialization request, CancellationToken cancellationToken = default);

	/// <summary>
	/// Materializes (or returns) the unbound attentive for a decree occurrence — the interaction trigger.
	/// </summary>
	Task<Attentive> MaterializeAttentiveAsync(string decreeId, AttentiveMaterialization request, CancellationToken cancellationToken = default);

	/// <summary>
	/// Lists eventive occurrences, optionally filtered by owning fate or objective.
	/// </summary>
	Task<IReadOnlyList<Eventive>> ListEventivesAsync(string? fateId = null, string? objectiveId = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Lists attentive occurrences, optionally filtered by owning decree.
	/// </summary>
	Task<IReadOnlyList<Attentive>> ListAttentivesAsync(string? decreeId = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Applies a mutable update to an eventive occurrence.
	/// </summary>
	Task<Eventive> UpdateEventiveAsync(long eventiveId, EventiveUpdate update, CancellationToken cancellationToken = default);

	/// <summary>
	/// Applies a mutable update to an attentive occurrence, honouring the bound/unbound mobility rules.
	/// </summary>
	Task<Attentive> UpdateAttentiveAsync(long attentiveId, AttentiveUpdate update, CancellationToken cancellationToken = default);
}
