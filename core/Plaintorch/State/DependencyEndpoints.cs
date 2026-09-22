using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Pleiades.Orchestration;
using Pleiades.Puck;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.State;

/// <summary>
/// Resolves a loosely-referenced dependency <see cref="EndpointRef"/> to its live entity (PEP101). Lookups are
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
		if (endpoint.Recurrence is { } recurrence)
		{
			var date = recurrence.Date;
			query = recurrence.Time is TimeOnly time
				? query.Where(item => item.RecurrenceDate == date && item.RecurrenceTime == time)
				: query.Where(item => item.RecurrenceDate == date && item.RecurrenceTime == null);
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

		return endpoint.Recurrence is not { } recurrence
			|| (eventive.RecurrenceDate == recurrence.Date && eventive.RecurrenceTime == recurrence.Time);
	}
}
