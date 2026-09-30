using Pleiades.Orchestration;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// The temporal cycle rule as a pure walk (PEP102 patch 9): the same two-sided begin/finish state space the recursive
/// CTE used to walk on the connection, now in memory so derived edges take part, the contradicting path is named, and
/// the purge can ask it repeatedly.
/// </summary>
public sealed class DependencyOrderingTests
{
	private static EndpointRef D(string id) => new(DependencyEndpointKind.Directive, id);
	private static EndpointRef O(string id) => new(DependencyEndpointKind.Objective, id);
	private static EndpointRef C(string id) => new(DependencyEndpointKind.Checkpoint, id);

	private static Dependency Edge(long id, EndpointRef source, EndpointRef target, DependencyTrigger? trigger = null, DependencyConstraint? constraint = null)
		=> new() { Id = id, SourceKind = source.Kind, SourceId = source.Id, TargetKind = target.Kind, TargetId = target.Id, Trigger = trigger, Constraint = constraint };

	[Fact]
	public void An_orderable_begin_finish_loop_is_possible()
	{
		// A.begin < B.begin exists; asking B.begin < A.finish closes a node loop that orders cleanly.
		var edges = new[] { Edge(1, D("A"), D("B"), DependencyTrigger.OnBegin, DependencyConstraint.ToBegin) };
		Assert.Null(DependencyOrdering.FindContradiction(edges, D("B"), DependencyTrigger.OnBegin, D("A"), DependencyConstraint.ToFinish));
	}

	[Fact]
	public void An_unorderable_loop_is_named_by_its_path()
	{
		// A.finish < B.begin exists; asking B.finish < A.begin is impossible, and the path runs through the one edge
		// (the step from B.begin to B.finish is the node's own, not an edge).
		var edge = Edge(1, D("A"), D("B"));
		var path = DependencyOrdering.FindContradiction([edge], D("B"), DependencyTrigger.OnFinish, D("A"), DependencyConstraint.ToBegin);
		Assert.NotNull(path);
		Assert.Same(edge, Assert.Single(path));
		Assert.Contains("Directive:A.finish → Directive:B.begin", DependencyOrdering.Describe(path));
	}

	[Fact]
	public void A_checkpoint_is_a_dead_end()
	{
		var edges = new[]
		{
			Edge(1, D("A"), C("gate")),
			Edge(2, C("gate"), D("B")),
		};
		// A → gate → B carries no orderable constraint across the gate, so B.finish < A.begin is not contradicted.
		Assert.Null(DependencyOrdering.FindContradiction(edges, D("B"), null, D("A"), null));
		// And an edge onto or out of a checkpoint can never close a loop itself.
		Assert.Null(DependencyOrdering.FindContradiction(edges, D("B"), null, C("gate"), null));
	}

	[Fact]
	public void An_intrinsic_edge_takes_part_in_the_walk()
	{
		// x's finish intrinsically gates A's finish; A.finish < x.begin would loop: x.begin < x.finish < A.finish < x.begin.
		var intrinsic = Dependency.Intrinsic(O("x"), D("A"), satisfied: false);
		var path = DependencyOrdering.FindContradiction([intrinsic], D("A"), null, O("x"), null);
		Assert.NotNull(path);
		Assert.Same(intrinsic, Assert.Single(path));
		Assert.EndsWith("(intrinsic)", DependencyOrdering.Describe(path));
		// The natural parent-first ordering — A.begin < x.begin — is fine.
		Assert.Null(DependencyOrdering.FindContradiction([intrinsic], D("A"), DependencyTrigger.OnBegin, O("x"), null));
	}

	[Fact]
	public void The_purge_gives_up_explicit_edges_only_and_stops_when_the_graph_orders()
	{
		// Cross-tree: a under A, b under B. A.finish < b.begin and B.finish < a.begin loop through the two intrinsic edges.
		var aUnderA = Dependency.Intrinsic(O("a"), D("A"), false);
		var bUnderB = Dependency.Intrinsic(O("b"), D("B"), false);
		var older = Edge(1, D("A"), O("b"));
		var newer = Edge(2, D("B"), O("a"));

		var contradicted = DependencyOrdering.Contradicted([aUnderA, bUnderB, older, newer]);
		Assert.Equal([older, newer], contradicted.Select(candidate => candidate.Edge));
		Assert.All(contradicted, candidate => Assert.Contains(candidate.Path, edge => edge.Origin == DependencyOrigin.Intrinsic));

		// Both resolve the loop alike, so the newer edge goes and the older survives.
		var purged = DependencyOrdering.PlanPurge([aUnderA, bUnderB, older, newer], candidates => candidates.Select(candidate => candidate.Edge));
		Assert.Same(newer, Assert.Single(purged));

		// An edge contradicted only through the loop's true cause is innocent: a.finish < b.begin is fine on its own,
		// and although it is the newest, removing it resolves less than removing the edge that closes the loop.
		var innocent = Edge(3, O("a"), O("b"));
		purged = DependencyOrdering.PlanPurge([aUnderA, bUnderB, older, newer, innocent], candidates => candidates.Select(candidate => candidate.Edge));
		Assert.Same(newer, Assert.Single(purged));

		// A preference narrows the candidates: asked to prefer the older edge, the plan gives that one up instead.
		purged = DependencyOrdering.PlanPurge([aUnderA, bUnderB, older, newer], candidates => candidates.Select(candidate => candidate.Edge).Where(edge => edge.Id == 1));
		Assert.Same(older, Assert.Single(purged));

		// A loop of intrinsic edges alone has nothing to give up.
		var loopA = Dependency.Intrinsic(D("A"), D("B"), false);
		var loopB = Dependency.Intrinsic(D("B"), D("A"), false);
		Assert.Empty(DependencyOrdering.PlanPurge([loopA, loopB], candidates => candidates.Select(candidate => candidate.Edge)));
	}

	[Fact]
	public void An_explicit_edge_restating_an_intrinsic_one_is_redundant()
	{
		var intrinsic = Dependency.Intrinsic(O("x"), D("A"), false);
		var restated = Edge(1, O("x"), D("A"), DependencyTrigger.OnFinish, DependencyConstraint.ToFinish);
		var different = Edge(2, O("x"), D("A"), DependencyTrigger.OnBegin, DependencyConstraint.ToFinish);
		Assert.Same(restated, Assert.Single(DependencyOrdering.Redundant([intrinsic, restated, different])));
	}
}
