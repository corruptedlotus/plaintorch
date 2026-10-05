using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Pleiades.Orchestration;
using Pleiades.Orchestration.Lifecycle;
using Pleiades.Puck;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.State;

/// <summary>
/// The lifecycle of one dependency endpoint's entity (PEP101, PEP103).
/// </summary>
/// <param name="State">The entity's lifecycle state.</param>
/// <param name="NextGated">
/// The next of Started and Finished its kind reaches above its state — the transition a dependency into it can hold back
/// now — or <see langword="null"/> when none is left.
/// </param>
public readonly record struct EndpointLifecycle(LifecycleState State, LifecycleState? NextGated)
{
	/// <summary>
	/// Gets whether the entity has started: it is Started or Finished. A deferred entity has not.
	/// </summary>
	public bool Started => State >= LifecycleState.Started;

	/// <summary>
	/// Gets whether the entity has finished.
	/// </summary>
	public bool Finished => State == LifecycleState.Finished;

	/// <summary>
	/// Gets whether the entity is set aside (PEP102 patch 9's "failed").
	/// </summary>
	public bool Deferred => State == LifecycleState.Deferred;
}

/// <summary>
/// Resolves loosely-referenced dependency <see cref="EndpointRef"/>s to their live entities (PEP101). Lookups are
/// change-tracker aware — an entity added or modified in the current unit of work is seen before the database —
/// so reconciliation reacts to state shifts happening in the same save.
/// </summary>
public static class DependencyEndpoints
{
	/// <summary>
	/// Resolves the entity an endpoint refers to, or <see langword="null"/> when it does not exist (yet).
	/// </summary>
	public static Task<object?> ResolveEntityAsync(PlainfraContext context, EndpointRef endpoint, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(context);
		return endpoint.Kind switch
		{
			DependencyEndpointKind.Directive => FindPuckAsync<StellarDirective>(context, endpoint.Id, cancellationToken),
			DependencyEndpointKind.Objective => FindPuckAsync<Objective>(context, endpoint.Id, cancellationToken),
			DependencyEndpointKind.Fate => FindPuckAsync<Fate>(context, endpoint.Id, cancellationToken),
			DependencyEndpointKind.Checkpoint => FindPuckAsync<Checkpoint>(context, endpoint.Id, cancellationToken),
			DependencyEndpointKind.Eventive => FindEventiveAsync(context, endpoint, cancellationToken),
			_ => Task.FromResult<object?>(null),
		};
	}

	/// <summary>
	/// Resolves the lifecycle state of many endpoints at once: one query per endpoint kind rather than one per
	/// endpoint (PEP102 patch 9). An endpoint with no entity behind it — an occurrence still projected from its
	/// schedule, an id that names nothing — is absent from the result. Checkpoints have no lifecycle and are skipped.
	/// </summary>
	public static async Task<IReadOnlyDictionary<EndpointRef, EndpointLifecycle>> ResolveLifecyclesAsync(
		PlainfraContext context,
		EntityLifecycleResolver lifecycleResolver,
		IEnumerable<EndpointRef> endpoints,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(lifecycleResolver);
		ArgumentNullException.ThrowIfNull(endpoints);

		var result = new Dictionary<EndpointRef, EndpointLifecycle>();
		foreach (var group in endpoints.Distinct().GroupBy(endpoint => endpoint.Kind))
		{
			switch (group.Key)
			{
				case DependencyEndpointKind.Directive:
					await ResolvePuckKindAsync<StellarDirective>(context, lifecycleResolver, group, result, cancellationToken);
					break;
				case DependencyEndpointKind.Objective:
					await ResolvePuckKindAsync<Objective>(context, lifecycleResolver, group, result, cancellationToken);
					break;
				case DependencyEndpointKind.Fate:
					await ResolvePuckKindAsync<Fate>(context, lifecycleResolver, group, result, cancellationToken);
					break;
				case DependencyEndpointKind.Eventive:
					await ResolveEventivesAsync(context, lifecycleResolver, group, result, cancellationToken);
					break;
				default:
					break;
			}
		}

		return result;
	}

	private static async Task ResolvePuckKindAsync<TEntity>(
		PlainfraContext context,
		EntityLifecycleResolver lifecycleResolver,
		IEnumerable<EndpointRef> endpoints,
		Dictionary<EndpointRef, EndpointLifecycle> result,
		CancellationToken cancellationToken)
		where TEntity : class, IPuckNamedEntity
	{
		var wanted = endpoints.ToDictionary(endpoint => endpoint.Id, StringComparer.OrdinalIgnoreCase);
		// An entity added in this unit of work is not in the store yet; the tracker is the only place it exists.
		foreach (var entry in context.ChangeTracker.Entries<TEntity>().Where(entry => entry.State == EntityState.Added))
		{
			if (wanted.Remove(entry.Entity.Id, out var endpoint))
			{
				result[endpoint] = Of(lifecycleResolver, entry.Entity);
			}
		}

		if (wanted.Count == 0)
		{
			return;
		}

		// A tracking query resolves to the tracked instance where one exists, so a modification pending in this
		// unit of work is read rather than the stored row.
		var ids = wanted.Keys.ToList();
		var entities = await context.Set<TEntity>()
			.IgnoreAutoIncludes()
			.Where(entity => ids.Contains(entity.Id))
			.ToListAsync(cancellationToken);
		foreach (var entity in entities)
		{
			if (wanted.TryGetValue(entity.Id, out var endpoint))
			{
				result[endpoint] = Of(lifecycleResolver, entity);
			}
		}
	}

	private static async Task ResolveEventivesAsync(
		PlainfraContext context,
		EntityLifecycleResolver lifecycleResolver,
		IEnumerable<EndpointRef> endpoints,
		Dictionary<EndpointRef, EndpointLifecycle> result,
		CancellationToken cancellationToken)
	{
		var wanted = endpoints.ToList();
		var owners = wanted.Select(endpoint => endpoint.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		var candidates = context.ChangeTracker.Entries<Eventive>()
			.Where(entry => entry.State == EntityState.Added)
			.Select(entry => entry.Entity)
			.ToList();
		candidates.AddRange(await context.Eventives
			.Where(item => (item.FateId != null && owners.Contains(item.FateId)) || (item.ObjectiveId != null && owners.Contains(item.ObjectiveId)))
			.ToListAsync(cancellationToken));

		foreach (var endpoint in wanted)
		{
			var eventive = candidates.FirstOrDefault(candidate => MatchesSlot(candidate, endpoint));
			if (eventive is not null)
			{
				result[endpoint] = Of(lifecycleResolver, eventive);
			}
		}
	}

	private static EndpointLifecycle Of(EntityLifecycleResolver lifecycleResolver, object entity)
	{
		var state = lifecycleResolver.StateOf(entity);
		return new EndpointLifecycle(state, lifecycleResolver.NextGatedState(entity.GetType(), state));
	}

	private static async Task<object?> FindPuckAsync<TEntity>(PlainfraContext context, string id, CancellationToken cancellationToken)
		where TEntity : class, IPuckNamedEntity
	{
		var added = context.ChangeTracker.Entries<TEntity>()
			.FirstOrDefault(entry => entry.State == EntityState.Added && string.Equals(entry.Entity.Id, id, StringComparison.OrdinalIgnoreCase))?.Entity;
		if (added is not null)
		{
			return added;
		}

		return await context.Set<TEntity>().FirstOrDefaultAsync(entity => entity.Id == id, cancellationToken);
	}

	private static async Task<object?> FindEventiveAsync(PlainfraContext context, EndpointRef endpoint, CancellationToken cancellationToken)
	{
		var added = context.ChangeTracker.Entries<Eventive>()
			.FirstOrDefault(entry => entry.State == EntityState.Added && MatchesSlot(entry.Entity, endpoint))?.Entity;
		if (added is not null)
		{
			return added;
		}

		var query = context.Eventives.Where(item => item.FateId == endpoint.Id || item.ObjectiveId == endpoint.Id);
		if (endpoint.RecurrenceId is { } slot)
		{
			query = query.Where(item => item.RecurrenceId == slot);
		}

		return await query.FirstOrDefaultAsync(cancellationToken);
	}

	private static bool MatchesSlot(Eventive eventive, EndpointRef endpoint)
	{
		if (!string.Equals(eventive.FateId, endpoint.Id, StringComparison.OrdinalIgnoreCase)
			&& !string.Equals(eventive.ObjectiveId, endpoint.Id, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		return endpoint.RecurrenceId is not { } slot || eventive.RecurrenceId == slot;
	}
}
