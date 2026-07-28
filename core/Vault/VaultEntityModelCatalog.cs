using System.Reflection;
using Microsoft.EntityFrameworkCore.Metadata;
using Pleiades.Puck;

namespace Pleiades.Vault;

/// <summary>
/// Describes one application entity as declared through its identity and storage attributes.
/// </summary>
/// <remarks>
/// Storage is a policy assigned to the entity (<see cref="VaultStorageAttribute"/>), never an inherent trait of
/// the model: identity-driven modes such as Freeform and Implicit deliberately allow files to live anywhere in
/// the vault, with the declared location acting only as the canonical default. Consumers must therefore treat
/// <see cref="Storage"/> as the declared policy to interpret, not as a physical constraint.
/// </remarks>
/// <param name="EntityType">The CLR entity type.</param>
/// <param name="IsAbstract">Whether the type is an abstract family anchor rather than an instantiable entity.</param>
/// <param name="Kind">The stable entity kind declared by the type's own <see cref="PuckEntityAttribute"/>, when present.</param>
/// <param name="PuckDeclaration">The PUCK notation declared by the type's own <see cref="PuckFormatAttribute"/>, when present.</param>
/// <param name="Storage">The effective storage policy (declared on the type or inherited from a base), when the entity is vault-backed.</param>
/// <param name="StorageDeclaringType">The type in the hierarchy that declares the effective storage policy.</param>
public sealed record VaultEntityModel(
	Type EntityType,
	bool IsAbstract,
	string? Kind,
	string? PuckDeclaration,
	VaultStorageAttribute? Storage,
	Type? StorageDeclaringType);

/// <summary>
/// Reflects every application entity that participates in PUCK identity or vault storage into one queryable
/// catalog, so services resolve model facts from declarations instead of hardcoding per-type knowledge.
/// </summary>
/// <remarks>
/// The catalog is purely declarative metadata: it carries no filesystem or database state and performs no I/O.
/// <see cref="Validate"/> fails vault activation fast when declarations are structurally incoherent, extending
/// the fail-fast precedent set by <see cref="PuckRuntimeCompilationCatalog"/>.
/// </remarks>
public sealed class VaultEntityModelCatalog
{
	private readonly Lazy<IReadOnlyList<VaultEntityModel>> _models = new(BuildModels);
	private readonly Lazy<IReadOnlyDictionary<Type, VaultEntityModel>> _modelsByType;

	/// <summary>
	/// Initializes the catalog.
	/// </summary>
	public VaultEntityModelCatalog()
	{
		_modelsByType = new Lazy<IReadOnlyDictionary<Type, VaultEntityModel>>(
			() => _models.Value.ToDictionary(model => model.EntityType));
	}

	/// <summary>
	/// Gets every declared entity model.
	/// </summary>
	public IReadOnlyList<VaultEntityModel> GetModels() => _models.Value;

	/// <summary>
	/// Tries to get the declared model for an entity type.
	/// </summary>
	public bool TryGet(Type entityType, out VaultEntityModel? model)
	{
		ArgumentNullException.ThrowIfNull(entityType);
		var found = _modelsByType.Value.TryGetValue(entityType, out var resolved);
		model = resolved;
		return found;
	}

	/// <summary>
	/// Gets the declared model for an entity type, throwing when the type declares no identity or storage.
	/// </summary>
	public VaultEntityModel GetRequired(Type entityType)
	{
		return TryGet(entityType, out var model) && model is not null
			? model
			: throw new InvalidOperationException($"Type '{entityType.Name}' declares no PUCK identity or vault storage and is not part of the entity model catalog.");
	}

	/// <summary>
	/// Validates that the declared entity models are structurally coherent, failing vault activation otherwise.
	/// </summary>
	/// <param name="databaseModel">The EF model used to assert persistence registration, when available.</param>
	public void Validate(IReadOnlyModel? databaseModel = null)
	{
		var models = _models.Value;

		foreach (var model in models)
		{
			if (model.PuckDeclaration is not null && model.Kind is null)
			{
				throw new InvalidOperationException($"Entity '{model.EntityType.Name}' declares a PUCK format but no {nameof(PuckEntityAttribute)} kind.");
			}

			if (model.Storage is not null && !typeof(IPuckNamedEntity).IsAssignableFrom(model.EntityType))
			{
				throw new InvalidOperationException($"Entity '{model.EntityType.Name}' declares vault storage but does not implement {nameof(IPuckNamedEntity)}.");
			}

			if (model.IsAbstract && !models.Any(candidate => !candidate.IsAbstract && model.EntityType.IsAssignableFrom(candidate.EntityType)))
			{
				throw new InvalidOperationException($"Abstract entity '{model.EntityType.Name}' anchors no concrete declared entity.");
			}

			if (databaseModel is not null && !model.IsAbstract && databaseModel.FindEntityType(model.EntityType) is null)
			{
				throw new InvalidOperationException($"Entity '{model.EntityType.Name}' declares identity or storage but is not registered in the database model.");
			}
		}

		var duplicateKinds = models
			.Where(model => model.Kind is not null)
			.GroupBy(model => model.Kind!, StringComparer.OrdinalIgnoreCase)
			.Where(group => group.Count() > 1)
			.Select(group => group.Key)
			.ToArray();

		if (duplicateKinds.Length > 0)
		{
			throw new InvalidOperationException($"Entity kind declarations are ambiguous: {string.Join(", ", duplicateKinds)}.");
		}
	}

	private static IReadOnlyList<VaultEntityModel> BuildModels()
	{
		var entityTypes = AppDomain.CurrentDomain.GetAssemblies()
			.Where(assembly => !assembly.IsDynamic)
			.SelectMany(static assembly =>
			{
				try
				{
					return assembly.GetTypes();
				}
				catch (ReflectionTypeLoadException exception)
				{
					return exception.Types.Where(type => type is not null).Cast<Type>();
				}
			})
			.Where(type => type.IsClass
				&& (type.GetCustomAttribute<PuckEntityAttribute>(inherit: false) is not null
					|| type.GetCustomAttribute<PuckFormatAttribute>(inherit: false) is not null
					|| type.GetCustomAttribute<VaultStorageAttribute>(inherit: false) is not null))
			.Distinct()
			.OrderBy(type => type.FullName, StringComparer.Ordinal);

		return entityTypes
			.Select(static type =>
			{
				var (storage, declaringType) = ResolveEffectiveStorage(type);
				return new VaultEntityModel(
					type,
					type.IsAbstract,
					type.GetCustomAttribute<PuckEntityAttribute>(inherit: false)?.Kind,
					type.GetCustomAttribute<PuckFormatAttribute>(inherit: false)?.Notation,
					storage,
					declaringType);
			})
			.ToArray();
	}

	private static (VaultStorageAttribute? Storage, Type? DeclaringType) ResolveEffectiveStorage(Type entityType)
	{
		for (var current = entityType; current is not null; current = current.BaseType)
		{
			var storage = current.GetCustomAttribute<VaultStorageAttribute>(inherit: false);
			if (storage is not null)
			{
				return (storage, current);
			}
		}

		return (null, null);
	}
}
