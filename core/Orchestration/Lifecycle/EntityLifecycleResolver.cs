using System.Collections.Concurrent;
using System.Reflection;

namespace Pleiades.Orchestration.Lifecycle;

/// <summary>
/// The one place the core reads an entity's lifecycle (PEP103): its current <see cref="LifecycleState"/>, the states its
/// kind passes through, the state it moves to next, how a move between two states reads, and which status stands for a
/// state when the core sets one of its own accord.
/// </summary>
/// <remarks>
/// A registered <see cref="ILifecycleStateSource"/> answers for the kinds whose state is not a plain status value (eventives
/// and checkpoints). Every other lifecycle kind marks its status property with <see cref="LifecycleStatusAttribute"/>, and
/// the members of that enum declare their states with <see cref="LifecycleStateAttribute"/>
/// (<see cref="LifecycleDeclarations"/>). A kind with neither has no lifecycle.
/// </remarks>
public sealed class EntityLifecycleResolver
{
	private static readonly IReadOnlySet<LifecycleState> NoStates = new HashSet<LifecycleState>();
	private static readonly LifecycleState[] Gated = [LifecycleState.Started, LifecycleState.Finished];

	private readonly IReadOnlyList<ILifecycleStateSource> _sources;
	private readonly ConcurrentDictionary<Type, KindLifecycle?> _kinds = new();

	/// <summary>
	/// Initializes the resolver with the registered state sources.
	/// </summary>
	public EntityLifecycleResolver(IEnumerable<ILifecycleStateSource> sources)
	{
		ArgumentNullException.ThrowIfNull(sources);
		_sources = sources.ToList();
	}

	/// <summary>
	/// Determines whether the entity kind takes part in the lifecycle: it has a lifecycle status property or a state source.
	/// </summary>
	public bool IsLifecycleKind(Type entityType) => Kind(entityType) is not null;

	/// <summary>
	/// Gets the states an entity kind passes through, or none for a kind with no lifecycle.
	/// </summary>
	public IReadOnlySet<LifecycleState> StatesOf(Type entityType) => Kind(entityType)?.States ?? NoStates;

	/// <summary>
	/// Gets an entity's current lifecycle state.
	/// </summary>
	/// <exception cref="InvalidOperationException">The entity's kind has no lifecycle.</exception>
	public LifecycleState StateOf(object entity)
	{
		ArgumentNullException.ThrowIfNull(entity);
		var type = entity.GetType();
		var kind = Kind(type)
			?? throw new InvalidOperationException($"Type '{type.Name}' declares no [LifecycleStatus] property and no lifecycle state source handles it.");
		if (kind.Source is { } source)
		{
			return source.StateOf(entity);
		}

		var status = kind.StatusProperty!.GetValue(entity) as Enum
			?? throw new InvalidOperationException($"Lifecycle status of '{type.Name}' is null.");
		return LifecycleDeclarations.StateOf(status);
	}

	/// <summary>
	/// Gets the state a status value stands for.
	/// </summary>
	public LifecycleState StateOf(Enum status) => LifecycleDeclarations.StateOf(status);

	/// <summary>
	/// Gets whether a status value closes its item (<see cref="LifecycleStateAttribute.Closed"/>, PEP103 patch 5).
	/// </summary>
	public bool IsClosed(Enum status) => LifecycleDeclarations.IsClosed(status);

	/// <summary>
	/// Gets whether an entity stands closed: its kind has a status, and the status it holds closes it. A kind whose state comes
	/// from a source (a checkpoint, an occurrence) never closes.
	/// </summary>
	public bool IsClosed(object entity)
	{
		ArgumentNullException.ThrowIfNull(entity);
		return StatusPropertyOf(entity.GetType())?.GetValue(entity) is Enum status && LifecycleDeclarations.IsClosed(status);
	}

	/// <summary>
	/// Gets the state an item of the kind reaches next going up the order from <paramref name="current"/> — Open for a
	/// deferred kind that has an Open state, then Started, then Finished, skipping any the kind does not pass through — or
	/// <see langword="null"/> when nothing lies ahead.
	/// </summary>
	public LifecycleState? NextState(Type entityType, LifecycleState current)
	{
		var states = StatesOf(entityType);
		return states.Any(state => state > current) ? states.Where(state => state > current).Min() : null;
	}

	/// <summary>
	/// Gets the next state ahead of <paramref name="current"/> that a dependency can hold back — the first of Started and
	/// Finished the kind passes through above it (a deferred item counts as before Open) — or <see langword="null"/> when
	/// none is left. A checkpoint, which never starts, always has its finish ahead until it unlocks.
	/// </summary>
	public LifecycleState? NextGatedState(Type entityType, LifecycleState current)
	{
		var states = StatesOf(entityType);
		foreach (var state in Gated)
		{
			if (states.Contains(state) && state > current)
			{
				return state;
			}
		}

		return null;
	}

	/// <summary>
	/// Reads a move between two states of an entity kind.
	/// </summary>
	public LifecycleTransition Transition(Type entityType, LifecycleState from, LifecycleState to) => new(from, to, StatesOf(entityType));

	/// <summary>
	/// Reads a move between two statuses of one lifecycle status enum. The kind passes through the states the enum declares.
	/// </summary>
	public LifecycleTransition Transition(Enum from, Enum to)
	{
		ArgumentNullException.ThrowIfNull(from);
		ArgumentNullException.ThrowIfNull(to);
		if (from.GetType() != to.GetType())
		{
			throw new ArgumentException($"A transition is read between two statuses of one enum, not '{from.GetType().Name}' and '{to.GetType().Name}'.", nameof(to));
		}

		return new LifecycleTransition(StateOf(from), StateOf(to), LifecycleDeclarations.StatesOf(from.GetType()));
	}

	/// <summary>
	/// Gets the status the core sets when it moves an entity of the kind to <paramref name="state"/> of its own accord: the
	/// lowest-valued status of the kind that declares the state. Returns <see langword="null"/> when the kind has no status for
	/// the state — it does not pass through it, or reaches it by time or by unlocking rather than by a status.
	/// </summary>
	public Enum? DefaultStatus(Type entityType, LifecycleState state)
		=> Kind(entityType)?.StatusType is { } statusType ? LifecycleDeclarations.DefaultStatus(statusType, state) : null;

	/// <summary>
	/// Gets the property that holds a kind's lifecycle status (the one marked <see cref="LifecycleStatusAttribute"/>), or
	/// <see langword="null"/> for a kind whose state comes from a source or that has no lifecycle.
	/// </summary>
	public PropertyInfo? StatusPropertyOf(Type entityType) => Kind(entityType)?.StatusProperty;

	/// <summary>
	/// Gets whether a kind keeps a history of its states (<see cref="LifecycleStatusAttribute.History"/>, PEP103 patch 6):
	/// every state it enters is recorded anew and none withdrawn, as a fate, a decree and a lunar directive are.
	/// </summary>
	public bool KeepsHistory(Type entityType)
		=> Kind(entityType)?.StatusProperty?.GetCustomAttribute<LifecycleStatusAttribute>(inherit: true)?.History == true;

	/// <summary>
	/// Gets the status enum a kind is read through: its status property's type, or the type a state source sets (an eventive's
	/// resolution), or <see langword="null"/> for a kind with no status (a checkpoint) or no lifecycle.
	/// </summary>
	public Type? StatusTypeOf(Type entityType) => Kind(entityType)?.StatusType;

	/// <summary>
	/// Sets an entity's lifecycle status, as the core does when it moves the entity to another state of its own accord.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// The entity's kind has no status property (its state comes from a source, or it has no lifecycle), or
	/// <paramref name="status"/> is not a value of that property's enum.
	/// </exception>
	public void SetStatus(object entity, Enum status)
	{
		ArgumentNullException.ThrowIfNull(entity);
		ArgumentNullException.ThrowIfNull(status);
		var type = entity.GetType();
		var property = StatusPropertyOf(type)
			?? throw new InvalidOperationException($"'{type.Name}' has no lifecycle status the core can set.");
		if (property.PropertyType != status.GetType())
		{
			throw new InvalidOperationException($"'{status.GetType().Name}.{status}' is not a status of '{type.Name}'.");
		}

		property.SetValue(entity, status);
	}

	/// <summary>
	/// Validates, at vault activation, that every lifecycle declaration is coherent:
	/// <list type="bullet">
	/// <item><description>every member of a lifecycle status enum declares its state, and the enum has a main-line state;</description></item>
	/// <item><description>every state source passes through a main-line state, and the status enum it sets declares only states the source passes through;</description></item>
	/// <item><description>no kind has both a lifecycle status property and a state source.</description></item>
	/// </list>
	/// </summary>
	/// <exception cref="InvalidOperationException">A declaration is incoherent.</exception>
	public void Validate()
	{
		foreach (var source in _sources)
		{
			if (!source.States.Any(state => state != LifecycleState.Deferred))
			{
				throw new InvalidOperationException($"Lifecycle state source '{source.GetType().Name}' passes through no {nameof(LifecycleState.Open)}, {nameof(LifecycleState.Started)} or {nameof(LifecycleState.Finished)} state.");
			}

			if (source.StatusType is { } statusType)
			{
				LifecycleDeclarations.Validate(statusType);
				var stray = LifecycleDeclarations.StatesOf(statusType).Except(source.States).ToList();
				if (stray.Count > 0)
				{
					throw new InvalidOperationException($"Status '{statusType.Name}' of '{source.GetType().Name}' declares {string.Join(", ", stray)}, which the source never passes through.");
				}
			}
		}

		foreach (var type in typeof(EntityLifecycleResolver).Assembly.GetTypes())
		{
			if (FindStatusProperty(type) is not { } property)
			{
				continue;
			}

			if (FindSource(type) is { } source)
			{
				throw new InvalidOperationException($"'{type.Name}' has both a lifecycle status property and the state source '{source.GetType().Name}'; its lifecycle must have one origin.");
			}

			LifecycleDeclarations.Validate(property.PropertyType);
		}
	}

	private KindLifecycle? Kind(Type entityType)
	{
		ArgumentNullException.ThrowIfNull(entityType);
		return _kinds.GetOrAdd(entityType, type =>
		{
			if (FindSource(type) is { } source)
			{
				return new KindLifecycle(null, source, source.States, source.StatusType);
			}

			return FindStatusProperty(type) is { } property
				? new KindLifecycle(property, null, LifecycleDeclarations.StatesOf(property.PropertyType), property.PropertyType)
				: null;
		});
	}

	private ILifecycleStateSource? FindSource(Type type) => _sources.FirstOrDefault(source => source.Handles(type));

	private static PropertyInfo? FindStatusProperty(Type type)
	{
		return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.FirstOrDefault(property => property.GetCustomAttribute<LifecycleStatusAttribute>() is not null);
	}

	/// <summary>
	/// How one kind's lifecycle is read: from its status property or from its state source.
	/// </summary>
	private sealed record KindLifecycle(PropertyInfo? StatusProperty, ILifecycleStateSource? Source, IReadOnlySet<LifecycleState> States, Type? StatusType);
}
