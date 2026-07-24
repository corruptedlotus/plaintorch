namespace Pleiades.Orchestration;

/// <summary>
/// Enforces the structural rules of the dependency system (PEP101), mirroring how
/// <see cref="IncentiveParenting"/> guards the incentive parent graph. Endpoint existence and kind validity
/// (which require resolving loose references) are enforced in the application layer; these rules are pure.
/// </summary>
public static class DependencyRules
{
	/// <summary>
	/// Validates a prospective dependency edge (source blocks target) against the structural rules and the
	/// existing edge set (for cycle detection).
	/// </summary>
	/// <param name="source">The source (blocking/prerequisite) endpoint.</param>
	/// <param name="target">The target (blocked/dependant) endpoint.</param>
	/// <param name="trigger">The source-side trigger, or <see langword="null"/>.</param>
	/// <param name="constraint">The target-side constraint, or <see langword="null"/>.</param>
	/// <param name="existing">The existing dependency edges.</param>
	public static void EnsureValid(
		EndpointRef source,
		EndpointRef target,
		DependencyTrigger? trigger,
		DependencyConstraint? constraint,
		IReadOnlyCollection<Dependency> existing)
	{
		ArgumentNullException.ThrowIfNull(existing);

		if (source == target)
		{
			throw new InvalidOperationException("A dependency cannot link an endpoint to itself.");
		}

		if (source.Kind == DependencyEndpointKind.Checkpoint && trigger is not null)
		{
			throw new InvalidOperationException("A checkpoint source has no begin/finish, so its trigger must be empty.");
		}

		if (target.Kind == DependencyEndpointKind.Checkpoint && constraint is not null)
		{
			throw new InvalidOperationException("A checkpoint target has no begin/finish, so its constraint must be empty.");
		}

		EnsureRecurrenceShape("source", source);
		EnsureRecurrenceShape("target", target);
		EnsureNoCycle(source, target, existing);
	}

	private static void EnsureRecurrenceShape(string side, EndpointRef endpoint)
	{
		if (endpoint.Kind == DependencyEndpointKind.Eventive)
		{
			// The recurrence slot may be omitted only for a one-off (non-orbit) owner; that requires resolving
			// the owner and is enforced by the service. Here we only forbid a time without a date.
			if (endpoint.RecurrenceDate is null && endpoint.RecurrenceTime is not null)
			{
				throw new InvalidOperationException($"The {side} eventive endpoint has an occurrence time without a date.");
			}

			return;
		}

		if (endpoint.RecurrenceDate is not null || endpoint.RecurrenceTime is not null)
		{
			throw new InvalidOperationException($"The {side} {endpoint.Kind} endpoint must not carry an occurrence slot; only eventive endpoints do.");
		}
	}

	private static void EnsureNoCycle(EndpointRef source, EndpointRef target, IReadOnlyCollection<Dependency> existing)
	{
		// Model "X depends on Y" as an edge X -> Y (dependant -> prerequisite). The new edge is target -> source.
		// A cycle forms if `source` can already reach `target` through existing depends-on edges.
		var adjacency = existing
			.GroupBy(dependency => dependency.Target)
			.ToDictionary(group => group.Key, group => group.Select(dependency => dependency.Source).ToList());

		var visited = new HashSet<EndpointRef>();
		var stack = new Stack<EndpointRef>();
		stack.Push(source);
		while (stack.Count > 0)
		{
			var node = stack.Pop();
			if (!visited.Add(node))
			{
				continue;
			}

			if (node == target)
			{
				throw new InvalidOperationException("Adding this dependency would create a cycle in the dependency graph.");
			}

			if (adjacency.TryGetValue(node, out var prerequisites))
			{
				foreach (var prerequisite in prerequisites)
				{
					stack.Push(prerequisite);
				}
			}
		}
	}
}
