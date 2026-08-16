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
/// A discriminated (table-per-hierarchy) entity family: an abstract anchor type and its concrete member entities
/// (PEP100's directive and incentive families). Modelling families first-class lets consumers derive family
/// knowledge from the catalog instead of re-encoding it as hand-kept type-name lists or per-consumer switches.
/// </summary>
/// <param name="Anchor">The abstract anchor type the family discriminates over.</param>
/// <param name="Members">The concrete member entities of the family.</param>
public sealed record VaultEntityFamily(Type Anchor, IReadOnlyList<VaultEntityModel> Members);

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
	private readonly Lazy<IReadOnlyList<VaultEntityFamily>> _families;

	/// <summary>
	/// Initializes the catalog.
	/// </summary>
	public VaultEntityModelCatalog()
	{
		_modelsByType = new Lazy<IReadOnlyDictionary<Type, VaultEntityModel>>(
			() => _models.Value.ToDictionary(model => model.EntityType));
		_families = new Lazy<IReadOnlyList<VaultEntityFamily>>(BuildFamilies);
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
	/// Gets every declared discriminated entity family (abstract anchor + concrete members).
	/// </summary>
	public IReadOnlyList<VaultEntityFamily> GetFamilies() => _families.Value;

	/// <summary>
	/// Tries to get the family anchored by an abstract type.
	/// </summary>
	public bool TryGetFamily(Type anchorType, out VaultEntityFamily? family)
	{
		ArgumentNullException.ThrowIfNull(anchorType);
		family = _families.Value.FirstOrDefault(candidate => candidate.Anchor == anchorType);
		return family is not null;
	}

	/// <summary>
	/// Gets the abstract anchor of the family a concrete member belongs to, or <see langword="null"/> when the type
	/// is not a concrete member of a declared family.
	/// </summary>
	public Type? GetFamilyAnchor(Type memberType)
	{
		ArgumentNullException.ThrowIfNull(memberType);
		return _families.Value
			.FirstOrDefault(family => family.Members.Any(member => member.EntityType == memberType))
			?.Anchor;
	}

	/// <summary>
	/// Determines whether an entity type name is the anchor or a concrete member of the family anchored by
	/// <paramref name="anchorType"/>. Replaces hand-kept per-family type-name lists with a catalog query.
	/// </summary>
	public bool IsFamilyMember(Type anchorType, string? entityTypeName)
	{
		ArgumentNullException.ThrowIfNull(anchorType);
		if (string.IsNullOrWhiteSpace(entityTypeName))
		{
			return false;
		}

		if (string.Equals(entityTypeName, anchorType.Name, StringComparison.Ordinal))
		{
			return true;
		}

		return TryGetFamily(anchorType, out var family)
			&& family!.Members.Any(member => string.Equals(member.EntityType.Name, entityTypeName, StringComparison.Ordinal));
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

	private IReadOnlyList<VaultEntityFamily> BuildFamilies()
	{
		var concrete = _models.Value.Where(model => !model.IsAbstract).ToList();

		// A family anchor is an abstract base shared by *some but not all* concrete members. Requiring "not all"
		// drops the common entity root (PuckNamedEntity), leaving the real discriminated hierarchies (Directive,
		// Incentive) whether or not the anchor itself declares entity attributes — Incentive, for instance, carries
		// none and so is not a catalog model in its own right.
		return concrete
			.SelectMany(member => AbstractBaseTypes(member.EntityType))
			.Distinct()
			.Where(anchor =>
				concrete.Any(member => member.EntityType != anchor && anchor.IsAssignableFrom(member.EntityType))
				&& !concrete.All(member => anchor.IsAssignableFrom(member.EntityType)))
			.Select(anchor => new VaultEntityFamily(
				anchor,
				concrete.Where(member => anchor.IsAssignableFrom(member.EntityType)).ToList()))
			.OrderBy(family => family.Anchor.Name, StringComparer.Ordinal)
			.ToList();
	}

	private static IEnumerable<Type> AbstractBaseTypes(Type type)
	{
		for (var current = type.BaseType; current is not null; current = current.BaseType)
		{
			if (current.IsAbstract)
			{
				yield return current;
			}
		}
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
