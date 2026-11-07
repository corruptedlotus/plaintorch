using Pleiades.Orchestration;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// The temporal cycle rule as a pure walk (PEP102 patch 9): the same two-sided begin/finish state space the recursive
/// CTE used to walk on the connection, now in memory so derived edges take part, the contradicting path is named, and
/// the purge can ask it repeatedly. A checkpoint is one point in it, its unlock, which edges both enter and leave (I-034).
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
	public void A_loop_through_a_checkpoint_is_refused(/* I-034 */)
	{
		// X.finish < C is in force; C < X.begin would put X's begin after its own finish, so X could never start.
		var into = Edge(1, D("X"), C("C"));
		var path = DependencyOrdering.FindContradiction([into], C("C"), null, D("X"), null);
		Assert.NotNull(path);
		Assert.Same(into, Assert.Single(path));
		Assert.Equal("Directive:X.finish → Checkpoint:C.unlock", DependencyOrdering.Describe(path));

		// The same loop asked from the other end.
		var outOf = Edge(2, C("C"), D("X"));
		Assert.Same(outOf, Assert.Single(DependencyOrdering.FindContradiction([outOf], D("X"), null, C("C"), null)!));

		// A chain through the checkpoint orders its ends like a chain through a work item: A.finish < C < B.begin forbids
		// B.finish < A.begin, and B.finish < C closes the loop at the checkpoint itself.
		var chain = new[] { Edge(3, D("A"), C("C")), Edge(4, C("C"), D("B")) };
		Assert.Equal(2, DependencyOrdering.FindContradiction(chain, D("B"), null, D("A"), null)!.Count);
		Assert.NotNull(DependencyOrdering.FindContradiction(chain, D("B"), null, C("C"), null));
	}

	[Fact]
	public void A_loop_through_two_checkpoints_is_refused(/* I-034 */)
	{
		// Checkpoints chained to each other: X.finish < C1 < C2, so C2 < X.begin loops.
		var checkpoints = new[] { Edge(1, D("X"), C("C1")), Edge(2, C("C1"), C("C2")) };
		Assert.Equal(2, DependencyOrdering.FindContradiction(checkpoints, C("C2"), null, D("X"), null)!.Count);

		// With a work item between them: X.finish < C1 < Y.begin < Y.finish < C2, so C2 < X.begin loops as well.
		var spanned = new[] { Edge(1, D("X"), C("C1")), Edge(2, C("C1"), O("Y")), Edge(3, O("Y"), C("C2")) };
		var path = DependencyOrdering.FindContradiction(spanned, C("C2"), null, D("X"), null);
		Assert.NotNull(path);
		Assert.Equal(spanned, path);

		// Checkpoints alone: C1 < C2 forbids C2 < C1, which would hold both locked for good.
		Assert.NotNull(DependencyOrdering.FindContradiction([Edge(1, C("C1"), C("C2"))], C("C2"), null, C("C1"), null));
	}

	[Fact]
	public void A_checkpoint_is_one_point_that_orders_cleanly_when_nothing_loops(/* I-034 */)
	{
		// C < X.begin and Y.finish < C: Y.finish < C < X.begin is a plain chain.
		Assert.Null(DependencyOrdering.FindContradiction([Edge(1, C("C"), D("X"))], O("Y"), null, C("C"), null));

		// X.begin < C < X.finish fits inside X's own span: the checkpoint is a point, not a vector, but X is one.
		var inside = new[] { Edge(1, D("X"), C("C"), DependencyTrigger.OnBegin) };
		Assert.Null(DependencyOrdering.FindContradiction(inside, C("C"), null, D("X"), DependencyConstraint.ToFinish));

		// Two items sharing one checkpoint impose nothing on each other beyond what runs through it: A.finish < C and
		// B.finish < C leave A and B free, so A.finish < B.begin and B.begin < A.finish are each possible.
		var shared = new[] { Edge(1, D("A"), C("C")), Edge(2, D("B"), C("C")) };
		Assert.Null(DependencyOrdering.FindContradiction(shared, D("A"), null, D("B"), null));
		Assert.Null(DependencyOrdering.FindContradiction(shared, D("B"), DependencyTrigger.OnBegin, D("A"), DependencyConstraint.ToFinish));
	}

	[Fact]
	public void A_stored_loop_through_a_checkpoint_is_contradicted_and_planned_for_the_purge(/* I-034 */)
	{
		var older = Edge(1, D("X"), C("C"));
		var newer = Edge(2, C("C"), D("X"));
		var bystander = Edge(3, C("C"), D("B"));

		Assert.Equal([older, newer], DependencyOrdering.Contradicted([older, newer, bystander]).Select(candidate => candidate.Edge));
		Assert.Same(newer, Assert.Single(DependencyOrdering.PlanPurge([older, newer, bystander], candidates => candidates.Select(candidate => candidate.Edge))));
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
