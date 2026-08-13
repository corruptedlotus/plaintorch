namespace Pleiades.Diagnostics;

/// <summary>
/// The kind of change a status underwent on a single ingest (PEP108). One durable log row is written per
/// transition; a same-severity re-observation is not a transition (it only bumps the live occurrence count).
/// </summary>
public enum OperationStatusTransitionKind
{
	/// <summary>A previously-unflagged condition began failing.</summary>
	Raised,

	/// <summary>An already-flagged condition rose to a higher severity.</summary>
	Escalated,

	/// <summary>An already-flagged condition fell to a lower severity.</summary>
	Deescalated,

	/// <summary>A flagged condition now passes and the status was cleared.</summary>
	Resolved,
}

/// <summary>
/// A single change to an operation status, emitted by <see cref="OperationStatusRegistry.Ingest"/> and forwarded
/// to the durable sink. Carries a snapshot of the status at the moment of the transition.
/// </summary>
/// <param name="Kind">The kind of change.</param>
/// <param name="OperationId">The operation the status belongs to.</param>
/// <param name="ScopeKey">The scope the status applies to.</param>
/// <param name="ReasonCode">The failing condition.</param>
/// <param name="Severity">The severity after the transition (for <see cref="OperationStatusTransitionKind.Resolved"/>, the severity it held while active).</param>
/// <param name="PreviousSeverity">The severity before the transition, when it changed.</param>
/// <param name="Files">The files involved.</param>
/// <param name="EntityId">The entity identity involved, when known.</param>
/// <param name="Detail">The human-readable detail at the moment of the transition.</param>
/// <param name="OccurrenceCount">The occurrence count at the moment of the transition.</param>
/// <param name="OccurredUtc">When the transition occurred.</param>
public sealed record OperationStatusTransition(
	OperationStatusTransitionKind Kind,
	string OperationId,
	string ScopeKey,
	string ReasonCode,
	OperationSeverity Severity,
	OperationSeverity? PreviousSeverity,
	IReadOnlyList<string> Files,
	string? EntityId,
	string? Detail,
	int OccurrenceCount,
	DateTimeOffset OccurredUtc)
{
	/// <summary>Gets the composite identity key of the status this transition applies to.</summary>
	public string Key => OperationStatusKey.Compose(OperationId, ScopeKey, ReasonCode);
}
