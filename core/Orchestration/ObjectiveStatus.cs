namespace Pleiades.Orchestration;

/// <summary>
/// Defines the workflow states available to objectives.
/// </summary>
public enum ObjectiveStatus
{
	/// <summary>
	/// The objective exists but is waiting to be acted on.
	/// </summary>
	Standby,
	/// <summary>
	/// The objective is temporarily blocked.
	/// </summary>
	Blocked,
	/// <summary>
	/// The objective is being pursued during onrush.
	/// </summary>
	Onrush,
	/// <summary>
	/// The objective is currently part of Polaris execution.
	/// </summary>
	Polaris,
	/// <summary>
	/// The objective has been completed.
	/// </summary>
	Done,
	/// <summary>
	/// The objective has been archived.
	/// </summary>
	Archived,
}