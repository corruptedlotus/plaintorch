namespace Pleiades.Orchestration;

/// <summary>
/// Defines the states available to decree declaratives (PEP100).
/// </summary>
public enum DecreeStatus
{
	/// <summary>
	/// The decree is active and its routine materializes attentives.
	/// </summary>
	Active,
	/// <summary>
	/// The decree has been abandoned and no longer materializes attentives.
	/// </summary>
	Abandoned,
}
