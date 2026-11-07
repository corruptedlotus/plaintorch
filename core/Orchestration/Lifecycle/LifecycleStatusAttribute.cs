namespace Pleiades.Orchestration.Lifecycle;

/// <summary>
/// Marks the property that carries an entity's lifecycle status enum (PEP103), so <see cref="EntityLifecycleResolver"/> finds
/// it by reflection without naming any property. The property type must be an enum whose members each declare their
/// <see cref="LifecycleState"/> with <see cref="LifecycleStateAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class LifecycleStatusAttribute : Attribute
{
	/// <summary>
	/// Gets or sets whether the kind keeps a history of its states rather than a place on the main line (PEP103 patch 6). A
	/// fate, a decree or a lunar directive is under way from its creation and moves between its states at will, so every
	/// state it enters is recorded anew and no record is ever withdrawn: it may hold several records of one state. A kind
	/// without it records where it stands — one record per state reached, withdrawn when it moves back.
	/// </summary>
	public bool History { get; init; }
}
