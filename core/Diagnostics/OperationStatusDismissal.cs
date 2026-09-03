namespace Pleiades.Diagnostics;

/// <summary>
/// The breadth a status dismissal applies to (PEP108 dismiss feature). Only <see cref="Instance"/> is surfaced in the
/// UI today; <see cref="File"/> and <see cref="Reason"/> are the reserved architectural framework for the future
/// "always ignore this file" / "always ignore this issue" broader-snooze options, so adding them later is a UI +
/// endpoint change rather than a schema or matching-logic change.
/// </summary>
public enum OperationStatusDismissalScope
{
	/// <summary>
	/// Dismisses one exact status — a single <c>(operation, scope, reason)</c> whose detail fingerprint still matches.
	/// It self-expires when a <em>different</em> problem arises on that key (the detail changes) or the status
	/// resolves and later re-raises, so the snooze never permanently blinds the user to a new problem.
	/// </summary>
	Instance,

	/// <summary>Dismisses every status on one file path, regardless of reason. Standing until explicitly restored.</summary>
	File,

	/// <summary>Dismisses every status with one reason code, regardless of file. Standing until explicitly restored.</summary>
	Reason,
}

/// <summary>
/// A durable, user-made decision to suppress an operation status (PEP108 dismiss feature). Matching is a pure function
/// of the dismissal and a live status, so a dismissal needs no runtime bookkeeping to "expire": an
/// <see cref="OperationStatusDismissalScope.Instance"/> dismissal stops matching the moment the status's detail
/// fingerprint changes (a different problem) or the status resolves and re-raises. File and Reason dismissals are
/// standing. Because matching is by value, a dismissal restored from the database on the next run applies to the
/// re-raised status automatically.
/// </summary>
/// <param name="Scope">The breadth the dismissal applies to.</param>
/// <param name="OperationId">The operation id (used by <see cref="OperationStatusDismissalScope.Instance"/>).</param>
/// <param name="ScopeKey">The status scope key — typically a file path (Instance and File scope).</param>
/// <param name="ReasonCode">The failing condition's reason code (Instance and Reason scope).</param>
/// <param name="Fingerprint">
/// The status detail captured at dismiss time. An Instance dismissal matches only while the live status's detail still
/// equals it, so a materially different problem on the same key resurfaces. Null for File/Reason scope.
/// </param>
/// <param name="DismissedUtc">When the dismissal was made.</param>
public sealed record OperationStatusDismissal(
	OperationStatusDismissalScope Scope,
	string OperationId,
	string ScopeKey,
	string ReasonCode,
	string? Fingerprint,
	DateTimeOffset DismissedUtc)
{
	/// <summary>The stable natural key of this dismissal, used for idempotent storage and restore.</summary>
	public string Key => OperationStatusDismissalKey.Compose(Scope, OperationId, ScopeKey, ReasonCode);

	/// <summary>Determines whether this dismissal currently suppresses the supplied live status.</summary>
	public bool Matches(OperationStatus status)
	{
		ArgumentNullException.ThrowIfNull(status);
		return Scope switch
		{
			OperationStatusDismissalScope.Instance =>
				string.Equals(OperationId, status.OperationId, StringComparison.Ordinal)
				&& string.Equals(ScopeKey, status.ScopeKey, StringComparison.Ordinal)
				&& string.Equals(ReasonCode, status.ReasonCode, StringComparison.Ordinal)
				&& string.Equals(Fingerprint, status.Detail, StringComparison.Ordinal),
			OperationStatusDismissalScope.File =>
				string.Equals(ScopeKey, status.ScopeKey, StringComparison.Ordinal),
			OperationStatusDismissalScope.Reason =>
				string.Equals(ReasonCode, status.ReasonCode, StringComparison.Ordinal),
			_ => false,
		};
	}
}

/// <summary>Composes the stable natural key of a dismissal from its scope and identity parts.</summary>
public static class OperationStatusDismissalKey
{
	// Unit Separator (U+001F): does not appear in operation ids, paths, or reason codes.
	private const char Separator = (char)0x1F;

	/// <summary>
	/// Composes a dismissal's natural key. Only the parts the scope actually matches on participate, so re-dismissing
	/// the same file (or reason) is idempotent regardless of which status first triggered it.
	/// </summary>
	public static string Compose(OperationStatusDismissalScope scope, string operationId, string scopeKey, string reasonCode)
		=> scope switch
		{
			OperationStatusDismissalScope.Instance => string.Concat("I", Separator, operationId, Separator, scopeKey, Separator, reasonCode),
			OperationStatusDismissalScope.File => string.Concat("F", Separator, scopeKey),
			OperationStatusDismissalScope.Reason => string.Concat("R", Separator, reasonCode),
			_ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown dismissal scope."),
		};
}
