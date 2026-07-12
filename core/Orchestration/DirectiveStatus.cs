namespace Pleiades.Orchestration;

/// <summary>
/// Defines the workflow states available to directives.
/// </summary>
public enum DirectiveStatus
{
	/// <summary>
	/// The directive has been defined but not yet committed.
	/// </summary>
	Planned,
	/// <summary>
	/// The directive has been committed to active work.
	/// </summary>
	Committed,
	/// <summary>
	/// The directive is currently active.
	/// </summary>
	Active,
	/// <summary>
	/// The directive has been fulfilled.
	/// </summary>
	Fulfilled,
	/// <summary>
	/// The directive has concluded and is over.
	/// </summary>
	Over,
	/// <summary>
	/// The directive has concluded unsuccessfully.
	/// </summary>
	Failed,
}