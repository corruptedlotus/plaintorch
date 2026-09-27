using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Pleiades.Orchestration;
using Pleiades.Puck;

namespace Pleiades.Vault.Database;

/// <summary>
/// Provides generic, declaration-driven data access for PUCK-named entities, replacing per-type
/// <c>typeof</c>/DbSet dispatch chains in resolution and watcher glue.
/// </summary>
/// <remarks>
/// Lookups run over <see cref="DbContext.Set{TEntity}()"/> for the requested CLR type, so abstract family
/// anchors (for example the directive base) query their whole discriminated family. Identity discipline comes
/// from PUCK tokenization upstream: a declaration only ever reaches the gateway with ids it can actually mint,
/// and cross-declaration ambiguity is rejected by the resolution layer.
/// </remarks>
public sealed class VaultEntityGateway(PlainfraContext context)
{
	private static readonly MethodInfo FindByIdCoreMethod = typeof(VaultEntityGateway)
		.GetMethod(nameof(FindByIdCoreAsync), BindingFlags.NonPublic | BindingFlags.Instance)!;

	private static readonly MethodInfo LoadKnownIdsCoreMethod = typeof(VaultEntityGateway)
		.GetMethod(nameof(LoadKnownIdsCoreAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

	private static readonly ConcurrentDictionary<Type, MethodInfo> ClosedFindByIdMethods = new();
	private static readonly ConcurrentDictionary<Type, MethodInfo> ClosedLoadKnownIdsMethods = new();

	/// <summary>
	/// Finds a PUCK-named entity of the given CLR type by identifier.
	/// </summary>
	/// <param name="entityType">The entity CLR type to query; an abstract anchor queries its whole family.</param>
	/// <param name="id">The PUCK identifier.</param>
	/// <param name="track">
	/// When <see langword="true"/>, the entity is returned tracked with default query behaviour (auto-includes
	/// apply) for mutation flows; otherwise the lookup is no-tracking and ignores auto-includes, matching
	/// read-only resolution payload semantics.
	/// </param>
	/// <param name="cancellationToken">A token used to cancel the lookup.</param>
	/// <returns>The entity instance, or <see langword="null"/> when not found.</returns>
	public Task<object?> FindByIdAsync(Type entityType, string id, bool track = false, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(entityType);
		ArgumentException.ThrowIfNullOrWhiteSpace(id);
		EnsurePuckNamed(entityType);

		var closed = ClosedFindByIdMethods.GetOrAdd(entityType, static type => FindByIdCoreMethod.MakeGenericMethod(type));
		return (Task<object?>)closed.Invoke(this, [id, track, cancellationToken])!;
	}

	/// <summary>
	/// Loads the set of known identifiers stored for an entity type; an abstract anchor spans its whole family.
	/// </summary>
	/// <remarks>
	/// Static so singleton catalogs can bind it into per-scope delegates that receive their own context.
	/// </remarks>
	public static Task<HashSet<string>> LoadKnownIdsAsync(PlainfraContext context, Type entityType, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(entityType);
		EnsurePuckNamed(entityType);

		var closed = ClosedLoadKnownIdsMethods.GetOrAdd(entityType, static type => LoadKnownIdsCoreMethod.MakeGenericMethod(type));
		return (Task<HashSet<string>>)closed.Invoke(null, [context, cancellationToken])!;
	}

	// Restricting relationships a save-time state rule releases in the very save that deletes the principal, so they never
	// reach the database: an incentive's work records, its executives and a decree's reflectives (kept with the reference
	// cleared in ended cycles, removed elsewhere; PlaintorchStatePolicyProcessor).
	private static readonly HashSet<(Type Dependent, string Property)> ReleasedOnDelete =
	[
		(typeof(Executive), nameof(Executive.IncentiveId)),
		(typeof(Reflective), nameof(Reflective.DecreeId)),
	];

	/// <summary>
	/// Finds what would make the database refuse to delete an entity: rows that still reference it through a restricting
	/// relationship — a foreign key that neither cascades nor nulls on delete (a directive's subdirectives, an incentive's
	/// child incentives). A caller that removes an entity without loading its dependents (the watcher's file-driven
	/// delete) asks this first, so a removal the database would reject is reported as blocked instead of attempted. It
	/// reads the model's own relationship metadata, so a new restricting relationship is covered without a per-type rule
	/// list; only the relationships a save-time rule releases in the deleting save itself are skipped.
	/// </summary>
	/// <param name="entity">The entity about to be removed; it does not need to be tracked.</param>
	/// <param name="cancellationToken">A token used to cancel the lookup.</param>
	/// <returns>One blocker per restricting relationship that still has referencing rows; empty when nothing blocks the delete.</returns>
	public async Task<IReadOnlyList<VaultEntityDeleteBlocker>> FindDeleteBlockersAsync(object entity, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(entity);
		var entityType = context.Model.FindEntityType(entity.GetType())
			?? throw new InvalidOperationException($"Type '{entity.GetType().Name}' is not part of the database model.");

		var keyValue = entityType.FindPrimaryKey() is { Properties: [var keyProperty] }
			? keyProperty.PropertyInfo?.GetValue(entity)
			: null;
		if (keyValue is null)
		{
			return [];
		}

		var sqlHelper = context.GetService<ISqlGenerationHelper>();
		var blockers = new List<VaultEntityDeleteBlocker>();
		foreach (var foreignKey in entityType.GetReferencingForeignKeys())
		{
			// Cascade and set-null are carried out by the database itself; only a restricting key can refuse the delete.
			if (foreignKey.IsOwnership
				|| foreignKey.DeleteBehavior is DeleteBehavior.Cascade or DeleteBehavior.SetNull
				|| foreignKey.Properties is not [var foreignKeyProperty]
				|| ReleasedOnDelete.Contains((foreignKey.DeclaringEntityType.ClrType, foreignKeyProperty.Name)))
			{
				continue;
			}

			var dependent = foreignKey.DeclaringEntityType;
			if (StoreObjectIdentifier.Create(dependent, StoreObjectType.Table) is not { } table
				|| foreignKeyProperty.GetColumnName(table) is not { } column)
			{
				continue;
			}

			var sql = $"SELECT COUNT(*) AS \"Value\" FROM {sqlHelper.DelimitIdentifier(table.Name, table.Schema)} WHERE {sqlHelper.DelimitIdentifier(column)} = {{0}}";
			var count = (await context.Database.SqlQueryRaw<int>(sql, keyValue).ToListAsync(cancellationToken)).Single();
			if (count > 0)
			{
				blockers.Add(new VaultEntityDeleteBlocker(dependent.ClrType.Name, foreignKeyProperty.Name, count));
			}
		}

		return blockers;
	}

	/// <summary>
	/// Creates a detached snapshot of an entity's mapped scalar properties and owned single references
	/// (regular navigations and shadow state excluded), used as the previous-state input for diff-aware
	/// canonical rewrites and audit details.
	/// </summary>
	/// <remarks>
	/// Reference-typed converted scalars (such as tag lists) are copied by reference; callers replace such
	/// properties by assignment rather than mutating them in place, which keeps snapshots stable. Owned
	/// references (such as a Polaris forecast) are recursively snapshotted into fresh instances.
	/// </remarks>
	/// <param name="entity">The entity instance to snapshot; it does not need to be tracked.</param>
	/// <returns>A new instance of the entity's runtime type carrying the copied scalar values.</returns>
	public object CloneScalars(object entity)
	{
		ArgumentNullException.ThrowIfNull(entity);
		var runtimeType = entity.GetType();
		var entityType = context.Model.FindEntityType(runtimeType)
			?? throw new InvalidOperationException($"Type '{runtimeType.Name}' is not part of the database model and cannot be snapshotted.");

		return CloneScalarsCore(entity, entityType);
	}

	private static object CloneScalarsCore(object entity, IEntityType entityType)
	{
		var clone = Activator.CreateInstance(entityType.ClrType)
			?? throw new InvalidOperationException($"Could not construct a snapshot instance of '{entityType.ClrType.Name}'.");

		foreach (var property in entityType.GetProperties())
		{
			var member = property.PropertyInfo;
			if (member?.SetMethod is null || member.GetMethod is null)
			{
				continue;
			}

			member.SetValue(clone, member.GetValue(entity));
		}

		foreach (var navigation in entityType.GetNavigations())
		{
			if (navigation.IsCollection || !navigation.TargetEntityType.IsOwned())
			{
				continue;
			}

			var member = navigation.PropertyInfo;
			if (member?.SetMethod is null || member.GetMethod is null)
			{
				continue;
			}

			var owned = member.GetValue(entity);
			member.SetValue(clone, owned is null ? null : CloneScalarsCore(owned, navigation.TargetEntityType));
		}

		return clone;
	}

	private static void EnsurePuckNamed(Type entityType)
	{
		if (!typeof(IPuckNamedEntity).IsAssignableFrom(entityType))
		{
			throw new InvalidOperationException($"Type '{entityType.Name}' is not a PUCK-named entity and cannot be queried by identifier.");
		}
	}

	private async Task<object?> FindByIdCoreAsync<TEntity>(string id, bool track, CancellationToken cancellationToken)
		where TEntity : class, IPuckNamedEntity
	{
		IQueryable<TEntity> query = context.Set<TEntity>();
		if (!track)
		{
			query = query.AsNoTracking().IgnoreAutoIncludes();
		}

		return await query.FirstOrDefaultAsync(entity => entity.Id == id, cancellationToken);
	}

	private static Task<HashSet<string>> LoadKnownIdsCoreAsync<TEntity>(PlainfraContext context, CancellationToken cancellationToken)
		where TEntity : class, IPuckNamedEntity
	{
		return context.Set<TEntity>()
			.AsNoTracking()
			.Select(entity => entity.Id)
			.ToHashSetAsync(StringComparer.OrdinalIgnoreCase, cancellationToken);
	}
}
