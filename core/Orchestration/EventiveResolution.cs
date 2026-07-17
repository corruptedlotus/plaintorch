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
}
