namespace Pleiades.Orchestration;

/// <summary>
/// Defines how an eventive occurrence resolves (PEP100). An occurrence that simply happens keeps its
/// pending resolution; passing is temporal, not stateful.
/// </summary>
public enum EventiveResolution
{
	/// <summary>
	/// The occurrence is pending or has simply happened.
	/// </summary>
	Pending,
	/// <summary>
	/// The occurrence was missed.
	/// </summary>
	Missed,
	/// <summary>
	/// The occurrence was cancelled.
	/// </summary>
	Cancelled,

	/// <summary>
	/// The owning fate is opted out of: the occurrence still spawns (so it can be opted back in by setting it
	/// <see cref="Pending"/>) but is hidden from the agenda and not hardened by time passage. Inherited from the
	/// fate's OptOut status at spawn (PEP100/PEP111).
	/// </summary>
	OptOut,
}
