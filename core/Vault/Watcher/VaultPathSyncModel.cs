using Pleiades.Vault.Database;

namespace Pleiades.Vault.Watcher;

/// <summary>
/// Describes a vault-backed model that can be detected purely from path and governed by its storage attribute.
/// </summary>
/// <param name="EntityType">The CLR entity type associated with this path model. May be abstract when the model
/// anchors a polymorphic family (for example the directive family); path composition instantiates the concrete
/// member selected by the file's identity, falling back to <see cref="ConcreteType"/>.</param>
/// <param name="ScanRoots">The root directories that should be scanned for markdown candidates.</param>
/// <param name="Mode">The storage mode that governs allowed reconciliation actions.</param>
/// <param name="Shape">The storage shape expected for discovered files.</param>
/// <param name="IsCandidatePath">A predicate that determines whether a path is a candidate for this model.</param>
/// <param name="LoadKnownIdsAsync">Loads known identifiers currently stored for this entity type.</param>
/// <param name="ConcreteType">The concrete CLR type to instantiate when the file's identity selects no specific
/// family member — a brand-new, as-yet-unidentified file. Defaults to <see cref="EntityType"/> when omitted; supply
/// a concrete subtype as the family's fallback member when <see cref="EntityType"/> is abstract. Identity-driven
/// selection (see <c>VaultFamilyInstantiationResolver</c>) takes precedence when an identity is present.</param>
public sealed record VaultPathSyncModel(
	Type EntityType,
	IReadOnlyList<string> ScanRoots,
	VaultStorageMode Mode,
	VaultStorageShape Shape,
	Func<string, bool> IsCandidatePath,
	Func<PlainfraContext, CancellationToken, Task<HashSet<string>>> LoadKnownIdsAsync,
	Type? ConcreteType = null)
{
	/// <summary>
	/// Gets the CLR entity name.
	/// </summary>
	public string EntityName => EntityType.Name;

	/// <summary>
	/// Gets the fallback concrete CLR type for path composition when the file's identity selects no specific family
	/// member, falling back in turn to <see cref="EntityType"/>. Identity-driven member selection takes precedence.
	/// </summary>
	public Type InstantiationType => ConcreteType ?? EntityType;
}