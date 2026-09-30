using Microsoft.EntityFrameworkCore;
using Pleiades.Orchestration;
using Pleiades.Orchestration.Lifecycle;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.State;

/// <summary>
/// Derives the dependency edges a part of the backlog's structure implies (PEP102 patch 9). Intrinsic edges are never
/// stored: a rule reads the structure as the current unit of work leaves it — tracked entities as they stand, so a
/// pending reparent or status shift already counts — and yields <see cref="DependencyOrigin.Intrinsic"/> edges with
/// their satisfaction computed.
/// </summary>
public interface IIntrinsicDependencyRule
{
	/// <summary>
	/// Derives the rule's edges for the whole vault.
	/// </summary>
	Task<IReadOnlyList<Dependency>> DeriveAsync(PlainfraContext context, CancellationToken cancellationToken);
}

/// <summary>
/// The first intrinsic rule: finishing a stellar directive depends on finishing each of its direct children — its
/// stellar subdirectives and its objectives (PEP102 patch 9). One <c>child.finish → parent.finish</c> edge per child,
/// satisfied once the child has finished. A failed end switches the edge off: a failed child neither holds its parent
/// back nor counts as done, and a failed parent is held back by nothing. Only stellar directives take part — a lunar
/// directive has no finish, so nothing hangs off one, and fates and decrees are never a directive's work.
/// </summary>
public sealed class DirectiveChildrenIntrinsicRule(EntityLifecycleResolver lifecycleResolver) : IIntrinsicDependencyRule
{
	/// <inheritdoc />
	public async Task<IReadOnlyList<Dependency>> DeriveAsync(PlainfraContext context, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(context);

		// Two projections carry the whole structure and the state of both ends of every edge, so deriving costs no
		// further lookups. The tracker is overlaid afterwards: an entity this unit of work adds, moves, shifts or
		// deletes is read as it stands, not as it is stored.
		var directives = (await context.Directives
				.OfType<StellarDirective>()
				.AsNoTracking()
				.IgnoreAutoIncludes()
				.Select(directive => new { directive.Id, directive.ParentDirectiveId, directive.Status })
				.ToListAsync(cancellationToken))
			.ToDictionary(directive => directive.Id, directive => Describe(directive.ParentDirectiveId, directive.Status), StringComparer.Ordinal);
		foreach (var entry in context.ChangeTracker.Entries<StellarDirective>())
		{
			if (entry.State == EntityState.Deleted)
			{
				directives.Remove(entry.Entity.Id);
			}
			else if (entry.State != EntityState.Detached)
			{
				directives[entry.Entity.Id] = Describe(entry.Entity.ParentDirectiveId, entry.Entity.Status);
			}
		}

		var objectives = (await context.Objectives
				.AsNoTracking()
				.IgnoreAutoIncludes()
				.Select(objective => new { objective.Id, objective.DirectiveId, objective.Status })
				.ToListAsync(cancellationToken))
			.ToDictionary(objective => objective.Id, objective => Describe(objective.DirectiveId, objective.Status), StringComparer.Ordinal);
		foreach (var entry in context.ChangeTracker.Entries<Objective>())
		{
			if (entry.State == EntityState.Deleted)
			{
				objectives.Remove(entry.Entity.Id);
			}
			else if (entry.State != EntityState.Detached)
			{
				objectives[entry.Entity.Id] = Describe(entry.Entity.DirectiveId, entry.Entity.Status);
			}
		}

		var edges = new List<Dependency>();
		foreach (var (id, child) in directives)
		{
			AddEdge(edges, directives, new EndpointRef(DependencyEndpointKind.Directive, id), child);
		}

		foreach (var (id, child) in objectives)
		{
			AddEdge(edges, directives, new EndpointRef(DependencyEndpointKind.Objective, id), child);
		}

		return edges;
	}

	private static void AddEdge(List<Dependency> edges, Dictionary<string, Child> directives, EndpointRef source, Child child)
	{
		// The parent must be a stellar directive: an objective under a lunar directive, or a hand-edited parent that
		// points nowhere, implies nothing.
		if (child.ParentId is null || !directives.TryGetValue(child.ParentId, out var parent) || child.Failed || parent.Failed)
		{
			return;
		}

		edges.Add(Dependency.Intrinsic(source, new EndpointRef(DependencyEndpointKind.Directive, child.ParentId), child.Finished));
	}

	private Child Describe(string? parentId, Enum status)
	{
		var (_, finish) = lifecycleResolver.ClassifyStatus(status);
		return new Child(parentId, finish, lifecycleResolver.IsFailedStatus(status));
	}

	private readonly record struct Child(string? ParentId, bool Finished, bool Failed);
}

/// <summary>
/// One reading of the whole dependency graph: the stored edges as the current unit of work leaves them and the edges
/// the structure implies (PEP102 patch 9).
/// </summary>
/// <param name="Explicit">The stored edges, tracked, less those this unit of work deletes and plus those it adds.</param>
/// <param name="Intrinsic">The derived edges, with their satisfaction computed.</param>
public sealed record DependencyGraphSnapshot(IReadOnlyList<Dependency> Explicit, IReadOnlyList<Dependency> Intrinsic)
{
	private IReadOnlyList<Dependency>? _all;

	/// <summary>
	/// Gets every edge in force, stored first.
	/// </summary>
	public IReadOnlyList<Dependency> All => _all ??= [.. Explicit, .. Intrinsic];
}

/// <summary>
/// Reads the dependency graph — stored edges and the intrinsic edges every registered rule derives — for the gate, the
/// reconciler, the API and the purge alike (PEP102 patch 9), so they all see the one graph.
/// </summary>
public sealed class DependencyGraphService(PlainfraContext context, IEnumerable<IIntrinsicDependencyRule> rules)
{
	private readonly IReadOnlyList<IIntrinsicDependencyRule> _rules = rules.ToList();

	/// <summary>
	/// Derives every intrinsic edge the structure currently implies.
	/// </summary>
	public async Task<IReadOnlyList<Dependency>> DeriveIntrinsicAsync(CancellationToken cancellationToken)
	{
		var edges = new List<Dependency>();
		foreach (var rule in _rules)
		{
			edges.AddRange(await rule.DeriveAsync(context, cancellationToken));
		}

		return edges;
	}

	/// <summary>
	/// Reads the whole graph. The stored edges come back tracked, so a caller inside a save can remove one.
	/// </summary>
	public async Task<DependencyGraphSnapshot> LoadGraphAsync(CancellationToken cancellationToken)
	{
		var stored = await context.Dependencies.ToListAsync(cancellationToken);
		var explicitEdges = stored
			.Where(dependency => context.Entry(dependency).State != EntityState.Deleted)
			.Concat(context.ChangeTracker.Entries<Dependency>()
				.Where(entry => entry.State == EntityState.Added)
				.Select(entry => entry.Entity))
			.Distinct()
			.ToList();
		return new DependencyGraphSnapshot(explicitEdges, await DeriveIntrinsicAsync(cancellationToken));
	}
}
