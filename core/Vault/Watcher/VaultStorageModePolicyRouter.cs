namespace Pleiades.Vault.Watcher;

/// <summary>
/// Resolves storage-mode policy services.
/// </summary>
public sealed class VaultStorageModePolicyRouter(IEnumerable<IVaultStorageModePolicyService> policies)
{
	private readonly IReadOnlyDictionary<VaultStorageMode, IVaultStorageModePolicyService> _policies = policies
		.GroupBy(policy => policy.Mode)
		.ToDictionary(group => group.Key, group => group.Last());

	/// <summary>
	/// Gets the policy service for a specific storage mode.
	/// </summary>
	public IVaultStorageModePolicyService Resolve(VaultStorageMode mode)
	{
		if (_policies.TryGetValue(mode, out var policy))
		{
			return policy;
		}

		throw new InvalidOperationException($"No storage-mode policy service is registered for mode '{mode}'.");
	}
}
