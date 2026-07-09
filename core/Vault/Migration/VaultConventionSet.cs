namespace Pleiades.Vault.Migration;

/// <summary>
/// A version-frozen description of the vault storage conventions for every vault-backed entity type.
/// </summary>
/// <remarks>
/// A convention set is the declarative "loader" record for a vault schema version: given the set for the version a
/// vault is stored at, a generic loader can read the vault back into entity state, and the current engine can then
/// re-emit that state under the current set.
/// </remarks>
public sealed class VaultConventionSet
{
	private readonly IReadOnlyDictionary<Type, VaultEntityConvention> _byType;

	/// <summary>
	/// Initializes a new convention set for a specific vault schema version.
	/// </summary>
	/// <param name="version">The vault schema version this set describes.</param>
	/// <param name="conventions">The per-entity conventions.</param>
	public VaultConventionSet(int version, IEnumerable<VaultEntityConvention> conventions)
	{
		ArgumentNullException.ThrowIfNull(conventions);
		Version = version;
		_byType = conventions.ToDictionary(convention => convention.EntityType);
	}

	/// <summary>
	/// Gets the vault schema version this set describes.
	/// </summary>
	public int Version { get; }

	/// <summary>
	/// Gets all entity conventions in the set.
	/// </summary>
	public IReadOnlyCollection<VaultEntityConvention> Entities => (IReadOnlyCollection<VaultEntityConvention>)_byType.Values;

	/// <summary>
	/// Resolves the convention for an entity type.
	/// </summary>
	public VaultEntityConvention For(Type entityType)
	{
		ArgumentNullException.ThrowIfNull(entityType);
		return _byType.TryGetValue(entityType, out var convention)
			? convention
			: throw new InvalidOperationException($"Convention set v{Version} has no convention for entity type '{entityType.Name}'.");
	}

	/// <summary>
	/// Tries to resolve the convention for an entity type.
	/// </summary>
	public bool TryGet(Type entityType, out VaultEntityConvention convention)
	{
		ArgumentNullException.ThrowIfNull(entityType);
		return _byType.TryGetValue(entityType, out convention!);
	}
}
