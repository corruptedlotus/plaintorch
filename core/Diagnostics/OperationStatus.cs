namespace Pleiades.Diagnostics;

/// <summary>
/// A live, currently-flagged operation status (PEP108), keyed by <c>(OperationId, ScopeKey, ReasonCode)</c>.
/// Re-observation of the same key refreshes detail/severity and bumps <see cref="OccurrenceCount"/> rather than
/// creating a duplicate.
/// </summary>
/// <param name="OperationId">The operation that raised the status.</param>
/// <param name="ScopeKey">The scope the status applies to.</param>
/// <param name="ReasonCode">The specific failing condition.</param>
/// <param name="Severity">The current severity.</param>
/// <param name="Files">The files involved.</param>
/// <param name="EntityId">The entity identity involved, when known.</param>
/// <param name="Detail">The latest human-readable detail (presentation only).</param>
/// <param name="Fingerprint">The structural identity of the current problem, as reported by the last failing check; what an Instance dismissal matches on.</param>
/// <param name="OccurrenceCount">How many times the failing condition has been observed since it was first raised.</param>
/// <param name="FirstRaisedUtc">When the status was first raised.</param>
/// <param name="LastObservedUtc">When the status was most recently observed.</param>
public sealed record OperationStatus(
	string OperationId,
	string ScopeKey,
	string ReasonCode,
	OperationSeverity Severity,
	IReadOnlyList<string> Files,
	string? EntityId,
	string? Detail,
	string? Fingerprint,
	int OccurrenceCount,
	DateTimeOffset FirstRaisedUtc,
	DateTimeOffset LastObservedUtc)
{
	/// <summary>Gets the composite identity key for this status.</summary>
	public string Key => OperationStatusKey.Compose(OperationId, ScopeKey, ReasonCode);
}

/// <summary>
/// Composes the composite identity key for an operation status from its parts.
/// </summary>
public static class OperationStatusKey
{
	// Unit Separator (U+001F): does not appear in operation ids, paths, or PUCK ids.
	private const char Separator = (char)0x1F;

	/// <summary>Composes a stable composite key from an operation id, scope key, and reason code.</summary>
	public static string Compose(string operationId, string scopeKey, string reasonCode)
		=> string.Concat(operationId, Separator, scopeKey, Separator, reasonCode);
}
