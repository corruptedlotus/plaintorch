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
	/// existing edge set (for the uniqueness rule).
	/// </summary>
	/// <remarks>
	/// These are the pure rules. Logical possibility — whether the edge contradicts the begin/finish ordering
	/// the existing edges already impose — is a temporal reachability question over the whole graph and is
	/// checked in the application layer against the database (PEP102), which also subsumes the plain
	/// node-level acyclicity this used to enforce: a temporal contradiction is always a node cycle, but a node
	/// cycle is not always a contradiction (a begin gating a begin gating the same node's finish is fine), so
	/// the coarse check would forbid orderings the temporal one correctly allows.
	/// </remarks>
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

		// Same node, whatever occurrence slot each side names: an entity cannot be its own prerequisite.
		if (source.Kind == target.Kind && string.Equals(source.Id, target.Id, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("A dependency cannot link an endpoint to itself.");
		}

		EnsureNotDuplicate(source, target, trigger, constraint, existing);

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
	}

	/// <summary>
	/// The trigger an edge acts on once the omitted default is resolved (finish-triggered), so a null and an
	/// explicit <see cref="DependencyTrigger.OnFinish"/> count as the one relation. A checkpoint source leaves
	/// it empty and has no default.
	/// </summary>
	private static DependencyTrigger? EffectiveTrigger(EndpointRef source, DependencyTrigger? trigger)
	{
		return source.Kind == DependencyEndpointKind.Checkpoint ? null : trigger ?? DependencyTrigger.OnFinish;
	}

	/// <summary>
	/// The constraint an edge gates once the omitted default is resolved (begin-constraining). A checkpoint
	/// target leaves it empty.
	/// </summary>
	private static DependencyConstraint? EffectiveConstraint(EndpointRef target, DependencyConstraint? constraint)
	{
		return target.Kind == DependencyEndpointKind.Checkpoint ? null : constraint ?? DependencyConstraint.ToBegin;
	}

	private static void EnsureNotDuplicate(
		EndpointRef source,
		EndpointRef target,
		DependencyTrigger? trigger,
		DependencyConstraint? constraint,
		IReadOnlyCollection<Dependency> existing)
	{
		// Compared on the resolved trigger and constraint, not the raw nullable columns, so an edge saved with
		// an explicit finish-trigger and one that left it to the default are recognised as the same relation
		// rather than sneaking past as distinct. A SQLite unique index cannot do this itself — it treats each
		// null as distinct — so this is the authority and the index is a backstop for the non-null case.
		var effectiveTrigger = EffectiveTrigger(source, trigger);
		var effectiveConstraint = EffectiveConstraint(target, constraint);
		foreach (var dependency in existing)
		{
			if (dependency.Source == source
				&& dependency.Target == target
				&& EffectiveTrigger(dependency.Source, dependency.Trigger) == effectiveTrigger
				&& EffectiveConstraint(dependency.Target, dependency.Constraint) == effectiveConstraint)
			{
				throw new InvalidOperationException("An identical dependency already exists between these two endpoints.");
			}
		}
	}

	private static void EnsureRecurrenceShape(string side, EndpointRef endpoint)
	{
		if (endpoint.Kind == DependencyEndpointKind.Eventive)
		{
			// An eventive endpoint may carry an occurrence slot, or omit it for a one-off (non-orbit) owner — owner
			// resolution is the service's job. The collapsed RecurrenceId makes a time-without-date shape impossible.
			return;
		}

		if (endpoint.Recurrence is not null)
		{
			throw new InvalidOperationException($"The {side} {endpoint.Kind} endpoint must not carry an occurrence slot; only eventive endpoints do.");
		}
	}

}
