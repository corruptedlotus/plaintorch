namespace Pleiades.Diagnostics;

/// <summary>
/// One check evaluated during a single run of an operation (PEP108). An operation reports all of the checks it
/// evaluated — passing and failing — so the registry can raise failing checks and automatically resolve ones
/// that now pass, without the operation tracking any prior state.
/// </summary>
/// <param name="ReasonCode">Stable identifier for the specific condition checked, unique within the operation (for example <c>file-in-use</c>).</param>
/// <param name="Passed">Whether the condition held on this run. A failing check raises/keeps a status; a passing check resolves any matching status.</param>
/// <param name="Severity">The graded severity to record when this check fails. Ignored when <paramref name="Passed"/> is <see langword="true"/>.</param>
/// <param name="Files">The files involved in this condition, if any (for example both sides of a relocation).</param>
/// <param name="EntityId">The PUCK/entity identity involved, when known and path-independent.</param>
/// <param name="Detail">A short human-readable detail for the failure. Presentation only — never matched on.</param>
/// <param name="Fingerprint">
/// A culture-invariant, structural identity of <em>this particular problem</em> (for example the classified concern
/// plus the offending field paths), composed by the reporter from facts rather than prose. An Instance dismissal
/// captures it and stays in force only while it still matches, so a materially different problem on the same key
/// resurfaces. <see langword="null"/> when the failing condition has no finer identity than its reason code.
/// </param>
public sealed record OperationCheck(
	string ReasonCode,
	bool Passed,
	OperationSeverity Severity = OperationSeverity.Error,
	IReadOnlyList<string>? Files = null,
	string? EntityId = null,
	string? Detail = null,
	string? Fingerprint = null)
{
	/// <summary>Creates a passing check for a reason code.</summary>
	public static OperationCheck Pass(string reasonCode) => new(reasonCode, Passed: true);

	/// <summary>Creates a failing check for a reason code with the given severity and involved context.</summary>
	public static OperationCheck Fail(
		string reasonCode,
		OperationSeverity severity,
		string? detail = null,
		IReadOnlyList<string>? files = null,
		string? entityId = null,
		string? fingerprint = null)
		=> new(reasonCode, Passed: false, severity, files, entityId, detail, fingerprint);
}
