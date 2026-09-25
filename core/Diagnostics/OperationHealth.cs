namespace Pleiades.Diagnostics;

/// <summary>
/// Rolled-up health of a subsystem derived from its active operation statuses (PEP108), plus lifecycle states a
/// subsystem may force through an override. Each derived state answers to a band of <see cref="OperationSeverity"/>.
/// </summary>
public enum OperationHealth
{
	/// <summary>No active status affects health (none at all, or informational ones only).</summary>
	Ok,

	/// <summary>The worst active status is a <see cref="OperationSeverity.Warning"/> or an <see cref="OperationSeverity.Error"/>.</summary>
	Issues,

	/// <summary>The worst active status is <see cref="OperationSeverity.Critical"/>: part of the subsystem's job is blocked.</summary>
	Critical,

	/// <summary>
	/// The subsystem is not doing its job: a <see cref="OperationSeverity.Fatal"/> status is active, or a lifecycle
	/// override says it is asleep or has nothing to serve.
	/// </summary>
	Standby,

	/// <summary>The subsystem is stopped/offline (a lifecycle override, never derived from statuses).</summary>
	Offline,
}
