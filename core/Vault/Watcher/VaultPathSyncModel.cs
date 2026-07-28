using Pleiades.Vault.Database;

namespace Pleiades.Vault.Watcher;

/// <summary>
/// Describes a vault-backed model that can be detected purely from path and governed by its storage attribute.
/// </summary>
/// <param name="EntityType">The CLR entity type associated with this path model. May be abstract when the model
/// anchors a polymorphic family (for example the directive family); path composition instantiates
/// <see cref="ConcreteType"/> instead.</param>
/// <param name="ScanRoots">The root directories that should be scanned for markdown candidates.</param>
/// <param name="Mode">The storage mode that governs allowed reconciliation actions.</param>
/// <param name="Shape">The storage shape expected for discovered files.</param>
/// <param name="IsCandidatePath">A predicate that determines whether a path is a candidate for this model.</param>
/// <param name="LoadKnownIdsAsync">Loads known identifiers currently stored for this entity type.</param>
/// <param name="ConcreteType">The concrete CLR type instantiated when composing a model from a path. Defaults to
/// <see cref="EntityType"/> when omitted; supply a concrete subtype when <see cref="EntityType"/> is abstract.</param>
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
	/// Gets the concrete CLR type to instantiate for path composition, falling back to <see cref="EntityType"/>.
	/// </summary>
	public Type InstantiationType => ConcreteType ?? EntityType;
}