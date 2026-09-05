namespace Pleiades.Vault;

/// <summary>
/// Declares the vault schema version constants used by the vault migration system.
/// </summary>
/// <remarks>
/// The vault schema version tracks the on-disk markdown conventions of a vault, independently of the EF Core
/// database schema. It advances whenever a change to storage conventions (filename form, frontmatter keys, folder
/// shape, partitioning, location roots, PUCK grammar) requires existing vault files to be re-canonicalised.
/// </remarks>
public static class VaultSchema
{
	/// <summary>
	/// The version assigned to a vault whose settings predate the vault migration system (no stored version).
	/// This corresponds to the pre-PEP091 storage conventions.
	/// </summary>
	public const int BaselineVersion = 1;

	/// <summary>
	/// The vault schema version the current engine emits. A vault stored below this version is migrated at startup.
	/// Version 2 introduces the PEP091 conventions (quiet objective identity, decoupled PUCK storage form). Version 3
	/// renames the in-planning onrush placeholder from the bare sentinel <c>0</c> to the gate-passing PUCK <c>x0000</c>.
	/// </summary>
	public const int CurrentVersion = 3;
}
