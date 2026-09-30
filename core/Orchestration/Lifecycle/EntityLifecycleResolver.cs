using System.Collections.Concurrent;
using System.Reflection;

namespace Pleiades.Orchestration.Lifecycle;

/// <summary>
/// Resolves whether an entity has begun or finished (PEP101) without hardcoding per-entity status knowledge.
/// A registered <see cref="ILifecyclePhaseSource"/> is consulted first (for kinds whose phases are not a plain
/// status value, such as eventives and checkpoints); otherwise the entity's <see cref="LifecycleStatusAttribute"/>
/// property is read and its current value's <see cref="LifecyclePhaseAttribute"/> phases are applied.
/// </summary>
public sealed class EntityLifecycleResolver
{
	private static readonly IReadOnlySet<LifecyclePhase> NoPhases = new HashSet<LifecyclePhase>();

	private readonly IReadOnlyList<ILifecyclePhaseSource> _sources;
	private readonly ConcurrentDictionary<Type, PropertyInfo?> _statusProperties = new();
	private readonly ConcurrentDictionary<Type, IReadOnlyDictionary<string, PhaseMembership>> _enumPhases = new();
	private readonly ConcurrentDictionary<Type, IReadOnlySet<LifecyclePhase>> _kindPhases = new();

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
	/// Determines whether the entity has failed (its status is a <see cref="LifecyclePhase.Failed"/> member). A phase
	/// source never reports failure: an eventive's passing is temporal and has no failed state.
	/// </summary>
	public bool HasFailed(object entity) => Resolve(entity).Failed;

	/// <summary>
	/// Determines whether a status value is a <see cref="LifecyclePhase.Failed"/> member.
	/// </summary>
	public bool IsFailedStatus(Enum statusValue)
	{
		ArgumentNullException.ThrowIfNull(statusValue);
		return GetEnumPhases(statusValue.GetType()).GetValueOrDefault(statusValue.ToString(), PhaseMembership.None).Failed;
	}

	/// <summary>
	/// Determines whether the entity kind participates in lifecycle evaluation at all (has a status property or
	/// a phase source). Lunar directives, decrees and other kinds with no lifecycle return <see langword="false"/>.
	/// </summary>
	public bool IsLifecycleKind(Type entityType)
	{
		ArgumentNullException.ThrowIfNull(entityType);
		return FindSource(entityType) is not null || _statusProperties.GetOrAdd(entityType, FindStatusProperty) is not null;
	}

	/// <summary>
	/// Gets the lifecycle phases an entity kind passes through: begin and finish for a kind that spans time,
	/// <see cref="LifecyclePhase.Finish"/> alone for a one-shot kind that cannot be started (a checkpoint), and none for
	/// a kind with no lifecycle. A status-driven kind passes through the phases its status members declare; a kind with
	/// a phase source, through those its source declares. <see cref="LifecyclePhase.Failed"/> is never among them: a
	/// failed entity has not begun, so failing is leaving the begin phase, not entering one of its own.
	/// </summary>
	public IReadOnlySet<LifecyclePhase> GetPhases(Type entityType)
	{
		ArgumentNullException.ThrowIfNull(entityType);
		return _kindPhases.GetOrAdd(entityType, type =>
		{
			if (FindSource(type) is { } source)
			{
				return source.Phases;
			}

			if (_statusProperties.GetOrAdd(type, FindStatusProperty) is not { } property)
			{
				return NoPhases;
			}

			var memberships = GetEnumPhases(property.PropertyType).Values.ToList();
			var phases = new HashSet<LifecyclePhase>();
			if (memberships.Any(membership => membership.Begin))
			{
				phases.Add(LifecyclePhase.Begin);
			}

			if (memberships.Any(membership => membership.Finish))
			{
				phases.Add(LifecyclePhase.Finish);
			}

			return phases;
		});
	}

	private (bool Begun, bool Finished, bool Failed) Resolve(object entity)
	{
		ArgumentNullException.ThrowIfNull(entity);
		var type = entity.GetType();

		var source = FindSource(type);
		if (source is not null)
		{
			return (source.HasBegun(entity), source.HasFinished(entity), false);
		}

		var property = _statusProperties.GetOrAdd(type, FindStatusProperty)
			?? throw new InvalidOperationException($"Type '{type.Name}' declares no [LifecycleStatus] property and no lifecycle phase source handles it.");
		var value = property.GetValue(entity)
			?? throw new InvalidOperationException($"Lifecycle status of '{type.Name}' is null.");
		var membership = GetEnumPhases(property.PropertyType).GetValueOrDefault(value.ToString()!, PhaseMembership.None);
		return (membership.Begin || membership.Finish, membership.Finish, membership.Failed);
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
			if (phases.Contains(LifecyclePhase.Failed) && (phases.Contains(LifecyclePhase.Begin) || phases.Contains(LifecyclePhase.Finish)))
			{
				throw new InvalidOperationException($"Lifecycle status member '{enumType.Name}.{field.Name}' cannot be both failed and begun or finished.");
			}

			map[field.Name] = new PhaseMembership(phases.Contains(LifecyclePhase.Begin), phases.Contains(LifecyclePhase.Finish), phases.Contains(LifecyclePhase.Failed));
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

	private readonly record struct PhaseMembership(bool Begin, bool Finish, bool Failed)
	{
		public static readonly PhaseMembership None = new(false, false, false);
	}
}
