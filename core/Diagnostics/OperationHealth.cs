namespace Pleiades.Diagnostics;

/// <summary>
/// Rolled-up health of a subsystem derived from its active operation statuses (PEP108), plus lifecycle states a
/// subsystem may force through an override.
/// </summary>
public enum OperationHealth
{
	/// <summary>No active status degrades health.</summary>
	Ok,

	/// <summary>At least one operation is suspended and none has failed.</summary>
	Suspended,

	/// <summary>At least one operation has an error/critical status.</summary>
	Issues,

	/// <summary>The subsystem is stopped/offline (a lifecycle override, never derived from statuses).</summary>
	Offline,
}
