namespace Pleiades.Orchestration;

/// <summary>
/// Defines the states available to fate declaratives (PEP100).
/// </summary>
public enum FateStatus
{
	/// <summary>
	/// The fate is active and materializes eventives.
	/// </summary>
	Active,
	/// <summary>
	/// The fate has been opted out of and no longer materializes eventives.
	/// </summary>
	OptOut,
	/// <summary>
	/// The fate has been cancelled.
	/// </summary>
	Cancelled,
}
