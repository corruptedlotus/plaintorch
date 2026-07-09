namespace Pleiades.Vault.Migration;

/// <summary>
/// Defines a single, ordered vault migration that advances the vault schema from one version to the next.
/// </summary>
/// <remarks>
/// A migration reads the vault under the conventions of <see cref="FromVersion"/> and re-emits it under the current
/// engine's conventions, advancing the stored version to <see cref="ToVersion"/>. Most migrations derive from
/// <see cref="VaultRecanonicalizationMigration"/>; bespoke implementations are the escape hatch for changes that a
/// declarative convention set cannot express.
/// </remarks>
public interface IVaultMigration
{
	/// <summary>
	/// Gets the vault schema version this migration reads from.
	/// </summary>
	int FromVersion { get; }

	/// <summary>
	/// Gets the vault schema version this migration advances to.
	/// </summary>
	int ToVersion { get; }

	/// <summary>
	/// Gets the stable, sortable migration identifier.
	/// </summary>
	string Id { get; }

	/// <summary>
	/// Gets a human-readable description of the migration.
	/// </summary>
	string Description { get; }

	/// <summary>
	/// Applies the migration.
	/// </summary>
	Task<VaultMigrationOutcome> ApplyAsync(CancellationToken cancellationToken = default);
}
