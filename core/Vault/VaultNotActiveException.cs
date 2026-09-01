namespace Pleiades.Vault;

/// <summary>
/// Thrown when vault-scoped work is attempted while the PLAINTORCH core is idle and no vault is bound.
/// </summary>
/// <remarks>
/// The hosted core runs in daemon mode without a vault until one is activated through user settings.
/// While idle, resolving vault-relative paths is meaningless, so <see cref="VaultLayout"/> raises this
/// exception instead of returning a location computed against a non-existent vault root.
/// </remarks>
public sealed class VaultNotActiveException : InvalidOperationException
{
	/// <summary>
	/// Initializes a new instance of the <see cref="VaultNotActiveException"/> class.
	/// </summary>
	public VaultNotActiveException()
		: base("No PLAINTORCH vault is currently active. The core is idle; activate a vault before performing vault-scoped work.")
	{
	}
}
