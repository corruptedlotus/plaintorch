using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
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
