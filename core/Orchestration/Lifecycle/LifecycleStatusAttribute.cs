namespace Pleiades.Orchestration.Lifecycle;

/// <summary>
/// Marks the property that carries an entity's lifecycle status enum (PEP103), so <see cref="EntityLifecycleResolver"/> finds
/// it by reflection without naming any property. The property type must be an enum whose members each declare their
/// <see cref="LifecycleState"/> with <see cref="LifecycleStateAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class LifecycleStatusAttribute : Attribute;
