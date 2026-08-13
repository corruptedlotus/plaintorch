namespace Pleiades.Diagnostics;

/// <summary>
/// The single graded scale used to rank operation-status reasons (PEP108). Ordering is significant: values are
/// compared to detect escalation/de-escalation, and health rolls up from the worst active severity.
/// </summary>
public enum OperationSeverity
{
	/// <summary>Informational; visible but does not affect health.</summary>
	Info = 0,

	/// <summary>Advisory problem; visible but does not, on its own, degrade health.</summary>
	Warning = 1,

	/// <summary>The operation is paused/blocked and expected to retry (for example, a file locked by another process).</summary>
	Suspended = 2,

	/// <summary>The operation failed for this scope and needs attention.</summary>
	Error = 3,

	/// <summary>A failure that compromises the subsystem's integrity or halts it.</summary>
	Critical = 4,
}
