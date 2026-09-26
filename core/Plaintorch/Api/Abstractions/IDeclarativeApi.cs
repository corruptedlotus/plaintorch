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
	/// Deletes a decree declarative along with its materialized attentives. Its executives and reflectives in ended Polaris
	/// cycles stay as work records with the reference cleared; those in the active cycle and in planned or forecast cycles
	/// are removed with it.
	/// </summary>
	Task DeleteDecreeAsync(string decreeId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Prompts an implicit decree to materialize its markdown file and begin its synchronization boundary.
	/// </summary>
	Task<Decree> BeginDecreeBoundaryAsync(string decreeId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Lists eventive occurrences, optionally filtered by owning fate or objective.
	/// </summary>
	Task<IReadOnlyList<Eventive>> ListEventivesAsync(string? fateId = null, string? objectiveId = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Lists attentive occurrences, optionally filtered by owning decree.
	/// </summary>
	Task<IReadOnlyList<Attentive>> ListAttentivesAsync(string? decreeId = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Applies a mutable update to an eventive occurrence addressed by its owner and RECURRENCE-ID. A
	/// still-projected occurrence is resolved into the same save, so the materialization and the interaction
	/// persist together and the state-policy pass enforces hardening centrally.
	/// </summary>
	Task<Eventive> UpdateEventiveAsync(EventiveOccurrenceRef occurrence, EventiveUpdate update, CancellationToken cancellationToken = default);

	/// <summary>
	/// Applies a mutable update to an attentive occurrence, honouring the bound/unbound mobility rules. An unbound
	/// occurrence is addressed by its decree + RECURRENCE-ID and resolved into the same save when still projected;
	/// a Polaris-bound occurrence — which has no meaningful recurrence-id — is addressed by its row id.
	/// </summary>
	Task<Attentive> UpdateAttentiveAsync(AttentiveOccurrenceRef occurrence, AttentiveUpdate update, CancellationToken cancellationToken = default);
}
