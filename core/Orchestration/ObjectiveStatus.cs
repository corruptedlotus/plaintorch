namespace Pleiades.Orchestration;

/// <summary>
/// Defines the workflow states available to objectives.
/// </summary>
public enum ObjectiveStatus
{
	/// <summary>
	/// The objective exists but is waiting to be acted on.
	/// </summary>
	Standby = 0,
	/// <summary>
	/// The objective is temporarily blocked.
	/// </summary>
	Blocked = 1,
	/// <summary>
	/// The objective is being pursued during onrush.
	/// </summary>
	Onrush = 2,
	/// <summary>
	/// The objective is currently part of Polaris execution.
	/// </summary>
	Polaris = 3,
	/// <summary>
	/// The objective has been completed.
	/// </summary>
	Done = 4,
	/// <summary>
	/// The objective has been archived.
	/// </summary>
	Archived = 5,
}