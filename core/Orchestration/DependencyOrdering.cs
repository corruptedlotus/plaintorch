namespace Pleiades.Orchestration;

/// <summary>
/// The temporal cycle rule of the dependency system (PEP102): whether the begin/finish ordering a set of edges imposes
/// can still be satisfied once one more is added, and which stored edges an ordering that structure now forbids has to
/// give up (PEP102 patch 9). Pure: it reads edges — stored and derived alike — and touches nothing.
/// </summary>
/// <remarks>
/// An edge asserts <c>source.trigger</c> comes before <c>target.constraint</c>. It is impossible when the graph already
/// orders them the other way: when, walking forward in time from the target's constrained side, the source's
/// triggering side is reachable. The walk lives in a two-sided state space (each node a begin and a finish) over three
/// steps: an edge carries its source's triggering side to its dependant's constrained side; a node's begin precedes its
/// own finish; and the start is the target's constrained side. A checkpoint has no side — its trigger and constraint
/// are empty — so it is a dead end and drops out of the begin→finish step on its own. This is strictly finer than
/// node-level acyclicity: a node cycle whose begins and finishes order out (A.begin → B.begin → A.finish) is allowed.
/// It used to run as a recursive CTE on the connection; it walks in memory now so derived edges take part and the
/// contradicting path can be named.
/// </remarks>
public static class DependencyOrdering
{
	private const int Begin = 0;
	private const int Finish = 1;

	/// <summary>
	/// Finds the ordering a prospective edge would contradict: the edges that already carry the target's constrained
	/// side forward to the source's triggering side, in walk order, or <see langword="null"/> when the edge is
	/// logically possible. A step a node takes from its own begin to its own finish is not an edge and is not listed.
	/// </summary>
	/// <param name="edges">The edges already in force, stored and derived.</param>
	/// <param name="source">The prospective edge's prerequisite endpoint.</param>
	/// <param name="trigger">Its trigger; <see langword="null"/> resolves to finish, and stays empty for a checkpoint.</param>
	/// <param name="target">The prospective edge's dependant endpoint.</param>
	/// <param name="constraint">Its constraint; <see langword="null"/> resolves to begin, and stays empty for a checkpoint.</param>
	public static IReadOnlyList<Dependency>? FindContradiction(
		IReadOnlyCollection<Dependency> edges,
		EndpointRef source,
		DependencyTrigger? trigger,
		EndpointRef target,
		DependencyConstraint? constraint)
	{
		ArgumentNullException.ThrowIfNull(edges);

		var origin = new State(source, TriggerSide(source, trigger));
		var seed = new State(target, ConstraintSide(target, constraint));
		// A checkpoint end has no side to reach or to start from, so the edge cannot close a temporal loop.
		if (origin.Side is null || seed.Side is null)
		{
			return null;
		}

		var outgoing = new Dictionary<State, List<Dependency>>();
		foreach (var edge in edges)
		{
			var from = new State(edge.Source, TriggerSide(edge.Source, edge.Trigger));
			if (from.Side is null)
			{
				continue;
			}

			if (!outgoing.TryGetValue(from, out var list))
			{
				list = [];
				outgoing[from] = list;
			}

			list.Add(edge);
		}

		var via = new Dictionary<State, (State Previous, Dependency? Edge)> { [seed] = (seed, null) };
		var pending = new Queue<State>();
		pending.Enqueue(seed);
		while (pending.Count > 0)
		{
			var current = pending.Dequeue();
			if (current == origin)
			{
				return PathTo(via, seed, origin);
			}

			if (outgoing.TryGetValue(current, out var edgesOut))
			{
				foreach (var edge in edgesOut)
				{
					var next = new State(edge.Target, ConstraintSide(edge.Target, edge.Constraint));
					// A checkpoint target is a dead end: nothing carries on from a side it does not have.
					if (next.Side is not null && via.TryAdd(next, (current, edge)))
					{
						pending.Enqueue(next);
					}
				}
			}

			if (current.Side == Begin)
			{
				var finish = current with { Side = Finish };
				if (via.TryAdd(finish, (current, null)))
				{
					pending.Enqueue(finish);
				}
			}
		}

		return null;
	}

	/// <summary>
	/// Determines whether a prospective edge is logically possible against the edges in force.
	/// </summary>
	public static bool IsPossible(IReadOnlyCollection<Dependency> edges, EndpointRef source, DependencyTrigger? trigger, EndpointRef target, DependencyConstraint? constraint)
		=> FindContradiction(edges, source, trigger, target, constraint) is null;

	/// <summary>
	/// The explicit edges the rest of the graph contradicts — each one closes a loop the begins and finishes cannot
	/// order — with the path that contradicts it. An intrinsic edge is never listed: structure is not up for purging.
	/// </summary>
	public static IReadOnlyList<(Dependency Edge, IReadOnlyList<Dependency> Path)> Contradicted(IReadOnlyCollection<Dependency> edges)
	{
		ArgumentNullException.ThrowIfNull(edges);
		var contradicted = new List<(Dependency, IReadOnlyList<Dependency>)>();
		foreach (var edge in edges)
		{
			if (edge.Origin != DependencyOrigin.Explicit)
			{
				continue;
			}

			var others = edges.Where(other => !ReferenceEquals(other, edge)).ToList();
			if (FindContradiction(others, edge.Source, edge.Trigger, edge.Target, edge.Constraint) is { } path)
			{
				contradicted.Add((edge, path));
			}
		}

		return contradicted;
	}

	/// <summary>
	/// Plans which explicit edges to purge so that nothing in the graph is contradicted any more (PEP102 patch 9). Round
	/// by round: the contradicted edges are found, <paramref name="prefer"/> narrows them to the ones that should give
	/// way first (all of them, when it has no preference), and of those the one whose removal leaves the fewest edges
	/// contradicted goes — the newest on a tie — until the graph orders cleanly. One at a time, because an edge is
	/// often contradicted only through another: purging every contradicted edge at once would take innocent ones with
	/// the loop's true cause. Dependencies are the slave of structure, so it is always a stored edge that gives way,
	/// never a derived one; a loop made of derived edges alone (a hand-edited parent chain that loops) has nothing to
	/// purge and is left as it is.
	/// </summary>
	/// <param name="edges">The edges in force, stored and derived.</param>
	/// <param name="prefer">Narrows the contradicted explicit edges of one round to the ones to purge from. Returning none ends the planning.</param>
	/// <returns>The explicit edges to purge, in the order they were chosen.</returns>
	public static IReadOnlyList<Dependency> PlanPurge(
		IReadOnlyCollection<Dependency> edges,
		Func<IReadOnlyList<(Dependency Edge, IReadOnlyList<Dependency> Path)>, IEnumerable<Dependency>> prefer)
	{
		ArgumentNullException.ThrowIfNull(edges);
		ArgumentNullException.ThrowIfNull(prefer);

		var remaining = edges.ToList();
		var purged = new List<Dependency>();
		while (true)
		{
			var contradicted = Contradicted(remaining);
			if (contradicted.Count == 0)
			{
				return purged;
			}

			var candidates = prefer(contradicted).Where(remaining.Contains).Distinct().OrderByDescending(edge => edge.Id).ToList();
			if (candidates.Count == 0)
			{
				return purged;
			}

			Dependency? chosen = null;
			var fewest = int.MaxValue;
			foreach (var candidate in candidates)
			{
				var left = Contradicted(remaining.Where(edge => !ReferenceEquals(edge, candidate)).ToList()).Count;
				if (left < fewest)
				{
					chosen = candidate;
					fewest = left;
				}
			}

			purged.Add(chosen!);
			remaining.Remove(chosen!);
		}
	}

	/// <summary>
	/// The explicit edges an intrinsic edge makes redundant: the same endpoints and the same resolved trigger and
	/// constraint, so they say what the structure already says (PEP102 patch 9).
	/// </summary>
	public static IReadOnlyList<Dependency> Redundant(IReadOnlyCollection<Dependency> edges)
	{
		ArgumentNullException.ThrowIfNull(edges);
		var intrinsic = edges.Where(edge => edge.Origin == DependencyOrigin.Intrinsic).ToList();
		return edges
			.Where(edge => edge.Origin == DependencyOrigin.Explicit && intrinsic.Any(implied => DependencyRules.SameRelation(implied, edge)))
			.ToList();
	}

	/// <summary>
	/// Describes a contradicting path for a message: each edge as <c>kind:id.side → kind:id.side</c>.
	/// </summary>
	public static string Describe(IReadOnlyList<Dependency> path)
	{
		ArgumentNullException.ThrowIfNull(path);
		return string.Join(", ", path.Select(edge =>
			$"{Describe(edge.Source)}.{SideName(TriggerSide(edge.Source, edge.Trigger))} → {Describe(edge.Target)}.{SideName(ConstraintSide(edge.Target, edge.Constraint))}"
			+ (edge.Origin == DependencyOrigin.Intrinsic ? " (intrinsic)" : string.Empty)));
	}

	private static string Describe(EndpointRef endpoint)
	{
		return endpoint.RecurrenceId is { } slot
			? $"{endpoint.Kind}:{endpoint.Id}@{slot:yyyy-MM-dd}"
			: $"{endpoint.Kind}:{endpoint.Id}";
	}

	private static string SideName(int? side) => side switch
	{
		Begin => "begin",
		Finish => "finish",
		_ => "unlock",
	};

	private static IReadOnlyList<Dependency> PathTo(Dictionary<State, (State Previous, Dependency? Edge)> via, State seed, State end)
	{
		var path = new List<Dependency>();
		for (var current = end; current != seed;)
		{
			var (previous, edge) = via[current];
			if (edge is not null)
			{
				path.Add(edge);
			}

			current = previous;
		}

		path.Reverse();
		return path;
	}

	/// <summary>The side an edge's source triggers on: begin or finish, or none for a checkpoint.</summary>
	private static int? TriggerSide(EndpointRef source, DependencyTrigger? trigger)
	{
		return source.Kind == DependencyEndpointKind.Checkpoint
			? null
			: trigger == DependencyTrigger.OnBegin ? Begin : Finish;
	}

	/// <summary>The side an edge's target is gated on: begin or finish, or none for a checkpoint.</summary>
	private static int? ConstraintSide(EndpointRef target, DependencyConstraint? constraint)
	{
		return target.Kind == DependencyEndpointKind.Checkpoint
			? null
			: constraint == DependencyConstraint.ToFinish ? Finish : Begin;
	}

	private readonly record struct State(EndpointRef Node, int? Side);
}
