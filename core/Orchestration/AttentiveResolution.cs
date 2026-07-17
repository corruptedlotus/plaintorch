namespace Pleiades.Orchestration;

/// <summary>
/// Defines how an attentive occurrence resolves (PEP100).
/// </summary>
public enum AttentiveResolution
{
	/// <summary>
	/// The attentive has not been resolved yet.
	/// </summary>
	Pending,
	/// <summary>
	/// The attentive was done, granting the decree's predefined Celestron reward.
	/// </summary>
	Done,
	/// <summary>
	/// The attentive was skipped.
	/// </summary>
	Skipped,
}
