using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Pleiades.Orchestration;
using Pleiades.Puck;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.Api.Changes;

/// <summary>
/// Publishes entity changes to the change feed as they are saved.
/// </summary>
/// <remarks>
/// Sits on the EF save hook rather than on the API endpoints, following the same reasoning as
/// <c>StateInterceptor</c>: it is the one place every write pathway passes through, so the
/// watcher syncing a markdown edit, the CLI and the scheduler are covered alongside the API. Publishing per
/// endpoint would catch only the writes a client already knows it made.
/// </remarks>
public sealed class PlaintorchChangeFeedInterceptor(PlaintorchChangeBroker broker) : SaveChangesInterceptor
{
	/// <summary>The type a user-preference write is announced under, keyed by the preference key (PEP116).</summary>
	public const string UserPreferenceChangeType = "UserPreference";

	private readonly ConcurrentDictionary<Guid, List<EntityChange>> _pendingByContextId = new();

	/// <inheritdoc />
	public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
		DbContextEventData eventData,
		InterceptionResult<int> result,
		CancellationToken cancellationToken = default)
	{
		if (eventData.Context is PlainfraContext context)
		{
			// Collected before the save, because afterwards every entry has reverted to Unchanged and the
			// record of what happened is gone.
			_pendingByContextId[context.ContextId.InstanceId] = await CollectAsync(context, cancellationToken);
		}

		return await base.SavingChangesAsync(eventData, result, cancellationToken);
	}

	/// <inheritdoc />
	public override ValueTask<int> SavedChangesAsync(
		SaveChangesCompletedEventData eventData,
		int result,
		CancellationToken cancellationToken = default)
	{
		if (eventData.Context is PlainfraContext context
			&& _pendingByContextId.TryRemove(context.ContextId.InstanceId, out var changes))
		{
			broker.Publish(changes);
		}

		return base.SavedChangesAsync(eventData, result, cancellationToken);
	}

	/// <inheritdoc />
	public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
	{
		if (eventData.Context is PlainfraContext context)
		{
			_pendingByContextId.TryRemove(context.ContextId.InstanceId, out _);
		}

		return base.SaveChangesFailedAsync(eventData, cancellationToken);
	}

	private static async Task<List<EntityChange>> CollectAsync(PlainfraContext context, CancellationToken cancellationToken)
	{
		var changes = new List<EntityChange>();
		var seen = new HashSet<(string Type, string Id)>();
		// Read once per save: whatever decided this work was authoritative did so several layers above.
		var criticalScope = PlaintorchChangeOrigin.IsCritical;
		// A snapshot, so resolving an owner among the tracked entities never enumerates the tracker while it is walked.
		var entries = context.ChangeTracker.Entries().ToList();
		var ownerTypes = new Dictionary<(IEntityType Principal, string Id), string>();

		foreach (var entry in entries)
		{
			var operation = ToOperation(entry.State);
			if (operation is null)
			{
				continue;
			}

			// A removal is authoritative on its own terms: there is nothing left for a local edit to be
			// about, so a client must not hold it off.
			var critical = criticalScope || operation.Value == EntityChangeOperation.Deleted;

			if (entry.Entity is IPuckNamedEntity named)
			{
				// The runtime type, so a polymorphic entity is announced as what it actually is — a fate
				// reports Fate, not Incentive — matching the @type the API serializes.
				Add(changes, seen, entry.Entity.GetType().Name, named.Id, operation.Value, critical);
				continue;
			}

			if (entry.Entity is Dependency dependency)
			{
				// An edge has no identity on the far side and, by design, no foreign key for OwnersOf to walk,
				// so neither branch above reaches it. What a client observes is the entities it joins, so it is
				// announced as a change to both endpoints. An eventive endpoint names its owner rather than a
				// tracked type, which still carries: any announcement revalidates the observed listings.
				Add(changes, seen, dependency.Source.Kind.ToString(), dependency.Source.Id, EntityChangeOperation.Modified, critical);
				Add(changes, seen, dependency.Target.Kind.ToString(), dependency.Target.Id, EntityChangeOperation.Modified, critical);
				continue;
			}

			if (entry.Entity is UserPreferenceRecord preference)
			{
				// A preference shapes how a client draws (the preferred calendar an orbit is read on) but has neither
				// an identity a client tracks nor an owner to walk to, so it is announced under its own type, keyed by
				// its preference key. A client matches it to no entity; the announcement only revalidates the observed
				// records, the preference listing among them.
				Add(changes, seen, UserPreferenceChangeType, preference.Key, operation.Value, critical);
				continue;
			}

			// A child record has no identity of its own on the far side, so it is announced as a change to
			// whichever entity owns it.
			foreach (var owner in OwnersOf(entry))
			{
				if (!ownerTypes.TryGetValue(owner, out var type))
				{
					type = await ResolveOwnerTypeAsync(context, entries, owner.Principal, owner.Id, cancellationToken);
					ownerTypes[owner] = type;
				}

				Add(changes, seen, type, owner.Id, EntityChangeOperation.Modified, critical);
			}
		}

		return changes;
	}

	/// <summary>
	/// Resolves the owning entities of a child record from EF's own relationship metadata.
	/// </summary>
	/// <remarks>
	/// Derived from the model rather than from hardcoded property names, so a record added later is
	/// covered without this having to learn about it.
	/// </remarks>
	private static IEnumerable<(IEntityType Principal, string Id)> OwnersOf(EntityEntry entry)
	{
		foreach (var foreignKey in entry.Metadata.GetForeignKeys())
		{
			var principal = foreignKey.PrincipalEntityType;
			if (!typeof(IPuckNamedEntity).IsAssignableFrom(principal.ClrType) || foreignKey.Properties.Count != 1)
			{
				continue;
			}

			var property = entry.Property(foreignKey.Properties[0].Name);
			// A deletion has already cleared the current value in some configurations; the original still
			// names the owner that needs to hear about it.
			var value = (property.CurrentValue ?? property.OriginalValue) as string;
			if (!string.IsNullOrWhiteSpace(value))
			{
				yield return (principal, value);
			}
		}
	}

	/// <summary>
	/// Names an owner by the type the API serializes it as. A foreign key into a discriminated family (an incentive or a
	/// directive) only knows the family's abstract anchor, which no client tracks anything under, so the owner's concrete
	/// type is taken from the owner itself when this save tracks it, and otherwise from its stored discriminator.
	/// </summary>
	private static async Task<string> ResolveOwnerTypeAsync(
		PlainfraContext context,
		IReadOnlyList<EntityEntry> entries,
		IEntityType principal,
		string id,
		CancellationToken cancellationToken)
	{
		if (!principal.ClrType.IsAbstract)
		{
			return principal.ClrType.Name;
		}

		var tracked = entries
			.Select(entry => entry.Entity)
			.FirstOrDefault(entity => principal.ClrType.IsInstanceOfType(entity)
				&& string.Equals(((IPuckNamedEntity)entity).Id, id, StringComparison.OrdinalIgnoreCase));
		if (tracked is not null)
		{
			return tracked.GetType().Name;
		}

		if (principal.FindDiscriminatorProperty() is not { } discriminator
			|| principal.GetTableName() is not { } table
			|| principal.FindPrimaryKey() is not { Properties: [var key] })
		{
			return principal.ClrType.Name;
		}

		var sql = context.GetService<ISqlGenerationHelper>();
		var query = $"SELECT {sql.DelimitIdentifier(discriminator.GetColumnName())} AS \"Value\" FROM {sql.DelimitIdentifier(table, principal.GetSchema())} WHERE {sql.DelimitIdentifier(key.GetColumnName())} = {{0}}";
		var stored = (await context.Database.SqlQueryRaw<string>(query, id).ToListAsync(cancellationToken)).FirstOrDefault();
		return principal.GetDerivedTypesInclusive()
			.FirstOrDefault(candidate => !candidate.IsAbstract() && Equals(candidate.GetDiscriminatorValue(), stored))
			?.ClrType.Name
			?? principal.ClrType.Name;
	}

	private static void Add(
		List<EntityChange> changes,
		HashSet<(string Type, string Id)> seen,
		string type,
		string id,
		EntityChangeOperation operation,
		bool critical)
	{
		if (!string.IsNullOrWhiteSpace(id) && seen.Add((type, id)))
		{
			changes.Add(new EntityChange(type, id, operation, critical));
		}
	}

	private static EntityChangeOperation? ToOperation(EntityState state) => state switch
	{
		EntityState.Added => EntityChangeOperation.Added,
		EntityState.Modified => EntityChangeOperation.Modified,
		EntityState.Deleted => EntityChangeOperation.Deleted,
		_ => null,
	};
}
