namespace Pleiades.Plaintorch.Enforcement;

/// <summary>
/// How firmly the core holds the backlog to its lifecycle rules when a requested change would break one (PEP103): the
/// backlog state enforcement level (BSEL), set by the <c>backlog.state-enforcement</c> preference.
/// </summary>
/// <remarks>
/// The level governs only lifecycle conflicts, the ones moving other items could resolve (<see cref="StateConflictKind.Lifecycle"/>).
/// Systemic violations — a reference to nothing, a structure the model cannot hold — are refused at every level.
/// <see cref="StateDecisionService"/> is the one place the level is applied.
/// </remarks>
public enum BacklogStateEnforcementLevel
{
	/// <summary>
	/// Lifecycle conflicts are let through untouched; only systemic violations are refused.
	/// </summary>
	Ignore,

	/// <summary>
	/// The core compensates: it moves the other items the requested state needs, and the change goes ahead.
	/// </summary>
	Auto,

	/// <summary>
	/// The core asks first. The change is held back with the compensations it would need; the user then cancels it, or
	/// repeats it forcing those compensations (<see cref="Api.PlaintorchRequestFlags.EnforcementHeader"/>). A change nobody
	/// can be asked about — a note edit, a background pass — is refused. The default.
	/// </summary>
	Warn,

	/// <summary>
	/// A change that breaks a lifecycle rule is refused.
	/// </summary>
	Block,
}
