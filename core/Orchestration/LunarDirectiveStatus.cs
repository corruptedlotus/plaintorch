namespace Pleiades.Orchestration;

/// <summary>
/// Defines the moonlight states available to lunar directives (PEP100).
/// </summary>
public enum LunarDirectiveStatus
{
	/// <summary>
	/// The lunar directive exists but is not currently enforced.
	/// </summary>
	OnHold,
	/// <summary>
	/// The lunar directive is actively enforced as a pillar of Project Moonlight.
	/// </summary>
	Active,
	/// <summary>
	/// The lunar directive has gone stale and needs attention or retirement.
	/// </summary>
	Stale,
}
