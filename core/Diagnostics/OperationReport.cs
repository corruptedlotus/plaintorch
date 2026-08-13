namespace Pleiades.Diagnostics;

/// <summary>
/// The full result of a single operation run against one scope (PEP108): the operation identity, the scope it ran
/// against, and every check it evaluated. This is the sole input to the status registry — the operation reports
/// facts and the registry derives raise/resolve transitions from them.
/// </summary>
/// <param name="OperationId">Stable dotted identifier for the operation, for example <c>watcher.sync</c>.</param>
/// <param name="ScopeKey">The instance the run acted on — typically a normalized file path or an entity id.</param>
/// <param name="Checks">Every check evaluated on this run, passing and failing.</param>
public sealed record OperationReport(
	string OperationId,
	string ScopeKey,
	IReadOnlyList<OperationCheck> Checks)
{
	/// <summary>Creates a report for a single check (the common one-condition case).</summary>
	public static OperationReport ForCheck(string operationId, string scopeKey, OperationCheck check)
		=> new(operationId, scopeKey, [check]);
}
