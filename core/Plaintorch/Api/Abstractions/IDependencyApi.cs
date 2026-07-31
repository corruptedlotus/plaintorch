using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Contracts;

namespace Pleiades.Plaintorch.Api.Abstractions;

/// <summary>
/// Defines the dependency-facing application actions exposed by PLAINTORCH (PEP101): directed blocking edges
/// between directives, objectives, fates (whole or per-occurrence), and checkpoints, plus checkpoint tolls and
/// external conditions.
/// </summary>
public interface IDependencyApi
{
	/// <summary>
	/// Creates a dependency edge where <paramref name="source"/> (prerequisite) blocks <paramref name="target"/>
	/// (dependant). Trigger/constraint default to finish-triggered/begin-constraining and are empty for
	/// checkpoint endpoints.
	/// </summary>
	Task<Dependency> CreateAsync(EndpointRef source, EndpointRef target, DependencyTrigger? trigger = null, DependencyConstraint? constraint = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Lists dependency edges, optionally filtered to those touching a given endpoint id (as source or target).
	/// </summary>
	Task<IReadOnlyList<Dependency>> ListAsync(string? entityId = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Deletes a dependency edge.
	/// </summary>
	Task DeleteAsync(long dependencyId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Gets the emitted dependency lock for an entity id (its unsatisfied incoming dependencies), separate from
	/// the entity's status.
	/// </summary>
	Task<DependencyLockView> GetLockAsync(string entityId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Creates a checkpoint.
	/// </summary>
	Task<Checkpoint> CreateCheckpointAsync(string title, string? requestedId = null, int? celestronToll = null, bool? externalCondition = null, string? onrushSprintId = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Gets a checkpoint by id.
	/// </summary>
	Task<Checkpoint?> GetCheckpointAsync(string checkpointId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Lists checkpoints.
	/// </summary>
	Task<IReadOnlyList<Checkpoint>> ListCheckpointsAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Deletes a checkpoint and the dependency edges touching it.
	/// </summary>
	Task DeleteCheckpointAsync(string checkpointId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Pays a checkpoint's Celestron toll from the banked balance (order-independent). Idempotent once paid.
	/// </summary>
	Task<Checkpoint> PayTollAsync(string checkpointId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Sets a checkpoint's external condition switch.
	/// </summary>
	Task<Checkpoint> SetExternalConditionAsync(string checkpointId, bool met, CancellationToken cancellationToken = default);
}
