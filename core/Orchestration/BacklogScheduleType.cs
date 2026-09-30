namespace Pleiades.Orchestration;

/// <summary>
/// Defines where a <see cref="BacklogSchedule"/> comes from (PEP101): the user's planning, or how things actually
/// turned out. Stored by name, so members may be appended as new kinds of schedule arrive.
/// </summary>
public enum BacklogScheduleType
{
	/// <summary>
	/// The user's plan: when the item is meant to begin or finish. Set through the schedule API.
	/// </summary>
	Plan,

	/// <summary>
	/// The reality: when the item began or finished, standing for as long as it stays begun or finished. Written and
	/// withdrawn by the core as the item's lifecycle changes, never by hand.
	/// </summary>
	Record,
}
