namespace Pleiades.Diagnostics;

/// <summary>
/// The single graded scale used to rank operation-status reasons (PEP108). Ordering is significant: values are
/// compared to detect escalation/de-escalation, and health rolls up from the worst active severity (see
/// <see cref="OperationStatusRegistry.GetHealth"/>). A severity says what an issue means for the subsystem, not where it
/// was noticed: a reason carries one severity wherever it is raised.
/// </summary>
public enum OperationSeverity
{
	/// <summary>
	/// Informational: an issue with no consequence that needs no action. Used very rarely; it never changes health.
	/// </summary>
	Info = 0,

	/// <summary>
	/// No breaking consequence, but best resolved to prevent further conflict — for example a file locked by another
	/// process (retried until it frees up), or content the subsystem already enforced (a reverted edit, a purged file).
	/// Health reads <see cref="OperationHealth.Issues"/>.
	/// </summary>
	Warning = 1,

	/// <summary>
	/// Truly invalid or illegal content the user must resolve; left unresolved, an entity may not sync properly or may
	/// be corrupted — for example invalid frontmatter held in conflict, a foreign or duplicate identity. Health reads
	/// <see cref="OperationHealth.Issues"/>.
	/// </summary>
	Error = 2,

	/// <summary>
	/// No longer a matter of validity but a technical failure that physically prevents the subsystem from doing part of
	/// its job — for example a file it may not read or write, a sync that failed to apply, a root it cannot watch.
	/// Health reads <see cref="OperationHealth.Critical"/>.
	/// </summary>
	Critical = 3,

	/// <summary>
	/// The subsystem cannot run or do its job at all — for example the vault is inaccessible or its startup sweep
	/// failed. These are the conditions that put a watcher to sleep until they clear; health reads
	/// <see cref="OperationHealth.Standby"/>, and a fatal status can never be dismissed.
	/// </summary>
	Fatal = 4,
}
