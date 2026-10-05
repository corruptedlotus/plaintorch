using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;

namespace Pleiades.Orchestration.Lifecycle;

/// <summary>
/// Reads the lifecycle declarations of status enums (PEP103): the <see cref="LifecycleState"/> each member stands for, the
/// states an enum covers, and the member the core sets for a state of its own accord. Every rule that needs to know what a
/// status means reads it here, so no status is named outside its declaration. Each enum is read once and cached.
/// </summary>
public static class LifecycleDeclarations
{
	private static readonly ConcurrentDictionary<Type, EnumLifecycle> Enums = new();

	/// <summary>
	/// Gets the state a status member declares.
	/// </summary>
	/// <exception cref="InvalidOperationException">The member declares no state, or is not a member of its enum.</exception>
	public static LifecycleState StateOf(Enum status)
	{
		ArgumentNullException.ThrowIfNull(status);
		return Of(status.GetType()).States.TryGetValue(status.ToString(), out var state)
			? state
			: throw new InvalidOperationException($"Lifecycle status '{status.GetType().Name}.{status}' declares no {nameof(LifecycleState)}.");
	}

	/// <summary>
	/// Gets the states the members of a status enum declare between them.
	/// </summary>
	public static IReadOnlySet<LifecycleState> StatesOf(Type statusType) => Of(statusType).Covered;

	/// <summary>
	/// Gets the status the core sets when it moves an item to <paramref name="state"/> of its own accord: the member with the
	/// lowest value among those that declare the state, which for these enums is the first one declared. Returns
	/// <see langword="null"/> when no member declares the state.
	/// </summary>
	public static Enum? DefaultStatus(Type statusType, LifecycleState state)
		=> Of(statusType).Defaults.GetValueOrDefault(state);

	/// <inheritdoc cref="DefaultStatus(Type, LifecycleState)"/>
	public static TStatus? DefaultStatus<TStatus>(LifecycleState state)
		where TStatus : struct, Enum
		=> DefaultStatus(typeof(TStatus), state) is TStatus status ? status : null;

	/// <summary>
	/// Validates a lifecycle status enum: every member declares a state, and at least one member is on the main line, since a
	/// kind that can only be deferred has no lifecycle to defer from.
	/// </summary>
	/// <exception cref="InvalidOperationException">The enum is incomplete or covers no main-line state.</exception>
	public static void Validate(Type statusType)
	{
		var lifecycle = Of(statusType);
		if (lifecycle.Undeclared.Count > 0)
		{
			throw new InvalidOperationException(
				$"Lifecycle status '{statusType.Name}' declares no {nameof(LifecycleState)} for {string.Join(", ", lifecycle.Undeclared.Select(member => $"'{member}'"))}.");
		}

		if (!lifecycle.Covered.Any(state => state != LifecycleState.Deferred))
		{
			throw new InvalidOperationException($"Lifecycle status '{statusType.Name}' declares no {nameof(LifecycleState.Open)}, {nameof(LifecycleState.Started)} or {nameof(LifecycleState.Finished)} member.");
		}
	}

	private static EnumLifecycle Of(Type statusType)
	{
		ArgumentNullException.ThrowIfNull(statusType);
		return Enums.GetOrAdd(statusType, Build);
	}

	private static EnumLifecycle Build(Type enumType)
	{
		if (!enumType.IsEnum)
		{
			throw new InvalidOperationException($"Lifecycle status type '{enumType.Name}' must be an enum.");
		}

		var states = new Dictionary<string, LifecycleState>(StringComparer.Ordinal);
		var defaults = new Dictionary<LifecycleState, Enum>();
		var undeclared = new List<string>();
		// Read in value order, so the first member met for a state is its default.
		var fields = enumType.GetFields(BindingFlags.Public | BindingFlags.Static)
			.OrderBy(field => Convert.ToInt64(field.GetRawConstantValue(), CultureInfo.InvariantCulture));
		foreach (var field in fields)
		{
			if (field.GetCustomAttribute<LifecycleStateAttribute>() is not { } declaration)
			{
				undeclared.Add(field.Name);
				continue;
			}

			states[field.Name] = declaration.State;
			defaults.TryAdd(declaration.State, (Enum)field.GetValue(null)!);
		}

		return new EnumLifecycle(states, states.Values.ToHashSet(), defaults, undeclared);
	}

	private sealed record EnumLifecycle(
		IReadOnlyDictionary<string, LifecycleState> States,
		IReadOnlySet<LifecycleState> Covered,
		IReadOnlyDictionary<LifecycleState, Enum> Defaults,
		IReadOnlyList<string> Undeclared);
}
