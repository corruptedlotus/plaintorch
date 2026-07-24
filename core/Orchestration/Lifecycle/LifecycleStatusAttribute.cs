namespace Pleiades.Orchestration.Lifecycle;

/// <summary>
/// Marks the property that carries an entity's lifecycle status enum (PEP101), so the dependency lifecycle
/// resolver can find it by reflection without hardcoding property names. The property type must be an enum
/// whose members are annotated with <see cref="LifecyclePhaseAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class LifecycleStatusAttribute : Attribute;
