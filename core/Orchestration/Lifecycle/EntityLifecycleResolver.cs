using System.Collections.Concurrent;
using System.Reflection;

namespace Pleiades.Orchestration.Lifecycle;

/// <summary>
/// Resolves whether an entity has begun or finished (PEP101) without hardcoding per-entity status knowledge.
/// A registered <see cref="ILifecyclePhaseSource"/> is consulted first (for kinds whose phases are not a plain
/// status value, such as eventives); otherwise the entity's <see cref="LifecycleStatusAttribute"/> property is
/// read and its current value's <see cref="LifecyclePhaseAttribute"/> phases are applied.
/// </summary>
public sealed class EntityLifecycleResolver
{
	private readonly IReadOnlyList<ILifecyclePhaseSource> _sources;
	private readonly ConcurrentDictionary<Type, PropertyInfo?> _statusProperties = new();
	private readonly ConcurrentDictionary<Type, IReadOnlyDictionary<string, PhaseMembership>> _enumPhases = new();

	/// <summary>
	/// Initializes the resolver with the registered non-status phase sources.
	/// </summary>
	public EntityLifecycleResolver(IEnumerable<ILifecyclePhaseSource> sources)
	{
		ArgumentNullException.ThrowIfNull(sources);
		_sources = sources.ToList();
	}

	/// <summary>
	/// Determines whether the entity has begun (its status is a begin-or-finish phase, or a source says so).
	/// </summary>
	public bool HasBegun(object entity) => Resolve(entity).Begun;

	/// <summary>
	/// Determines whether the entity has finished (its status is a finish phase, or a source says so).
	/// </summary>
	public bool HasFinished(object entity) => Resolve(entity).Finished;

	/// <summary>
	/// Determines whether the entity kind participates in lifecycle evaluation at all (has a status property or
	/// a phase source). Checkpoints and other non-lifecycle kinds return <see langword="false"/>.
	/// </summary>
	public bool IsLifecycleKind(Type entityType)
	{
		ArgumentNullException.ThrowIfNull(entityType);
		return FindSource(entityType) is not null || _statusProperties.GetOrAdd(entityType, FindStatusProperty) is not null;
	}

	private (bool Begun, bool Finished) Resolve(object entity)
	{
		ArgumentNullException.ThrowIfNull(entity);
		var type = entity.GetType();

		var source = FindSource(type);
		if (source is not null)
		{
			return (source.HasBegun(entity), source.HasFinished(entity));
		}

		var property = _statusProperties.GetOrAdd(type, FindStatusProperty)
			?? throw new InvalidOperationException($"Type '{type.Name}' declares no [LifecycleStatus] property and no lifecycle phase source handles it.");
		var value = property.GetValue(entity)
			?? throw new InvalidOperationException($"Lifecycle status of '{type.Name}' is null.");
		var membership = GetEnumPhases(property.PropertyType).GetValueOrDefault(value.ToString()!, PhaseMembership.None);
		return (membership.Begin || membership.Finish, membership.Finish);
	}

	/// <summary>
	/// Classifies a status enum value into its declared lifecycle phases, used to decide whether a workflow
	/// shift is a begin or finish transition.
	/// </summary>
	public (bool Begin, bool Finish) ClassifyStatus(Enum statusValue)
	{
		ArgumentNullException.ThrowIfNull(statusValue);
		var membership = GetEnumPhases(statusValue.GetType()).GetValueOrDefault(statusValue.ToString(), PhaseMembership.None);
		return (membership.Begin, membership.Finish);
	}

	private ILifecyclePhaseSource? FindSource(Type type) => _sources.FirstOrDefault(source => source.Handles(type));

	private static PropertyInfo? FindStatusProperty(Type type)
	{
		return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.FirstOrDefault(property => property.GetCustomAttribute<LifecycleStatusAttribute>() is not null);
	}

	private IReadOnlyDictionary<string, PhaseMembership> GetEnumPhases(Type enumType) => _enumPhases.GetOrAdd(enumType, BuildEnumPhases);

	private static IReadOnlyDictionary<string, PhaseMembership> BuildEnumPhases(Type enumType)
	{
		if (!enumType.IsEnum)
		{
			throw new InvalidOperationException($"Lifecycle status type '{enumType.Name}' must be an enum.");
		}

		var map = new Dictionary<string, PhaseMembership>(StringComparer.Ordinal);
		foreach (var field in enumType.GetFields(BindingFlags.Public | BindingFlags.Static))
		{
			var phases = field.GetCustomAttributes<LifecyclePhaseAttribute>().Select(attribute => attribute.Phase).ToArray();
			map[field.Name] = new PhaseMembership(phases.Contains(LifecyclePhase.Begin), phases.Contains(LifecyclePhase.Finish));
		}

		return map;
	}

	/// <summary>
	/// Validates, at vault activation, that every status-driven participant declares a coherent lifecycle: its
	/// <see cref="LifecycleStatusAttribute"/> enum carries at least one <see cref="LifecyclePhase.Finish"/>
	/// member, and at least one <see cref="LifecyclePhase.Begin"/> member unless a phase source supplies begin.
	/// </summary>
	public void Validate()
	{
		foreach (var type in typeof(EntityLifecycleResolver).Assembly.GetTypes())
		{
			var property = FindStatusProperty(type);
			if (property is null)
			{
				continue;
			}

			var phases = GetEnumPhases(property.PropertyType);
			if (!phases.Values.Any(membership => membership.Finish))
			{
				throw new InvalidOperationException($"Lifecycle status '{property.PropertyType.Name}' on '{type.Name}' declares no {nameof(LifecyclePhase)}.{nameof(LifecyclePhase.Finish)} member.");
			}

			if (!phases.Values.Any(membership => membership.Begin) && FindSource(type) is null)
			{
				throw new InvalidOperationException($"Lifecycle status '{property.PropertyType.Name}' on '{type.Name}' declares no {nameof(LifecyclePhase)}.{nameof(LifecyclePhase.Begin)} member and no phase source supplies begin.");
			}
		}
	}

	private readonly record struct PhaseMembership(bool Begin, bool Finish)
	{
		public static readonly PhaseMembership None = new(false, false);
	}
}
