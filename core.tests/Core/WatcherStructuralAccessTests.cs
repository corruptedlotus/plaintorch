using Pleiades.Vault;
using Pleiades.Vault.Policy;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// The tier-2 signal: whether the vault is structurally reachable. A single missing file is never structural, but the
/// vault root (or an entity root that exists yet cannot be accessed) going away is — that is what the watcher sleeps on.
/// </summary>
public sealed class WatcherStructuralAccessTests
{
	private static VaultWatcherPathPolicy PolicyForRoot(string vaultRoot)
		=> new(new VaultLayout(new VaultOptions { VaultPath = vaultRoot }));

	private static string TempRoot()
		=> Path.Combine(Path.GetTempPath(), "plaintorch-tests", Guid.NewGuid().ToString("N"));

	[Fact]
	public void An_existing_vault_root_is_structurally_accessible()
	{
		var root = TempRoot();
		Directory.CreateDirectory(root);
		try
		{
			Assert.True(PolicyForRoot(root).IsVaultStructurallyAccessible(out var inaccessiblePath));
			Assert.Null(inaccessiblePath);
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[Fact]
	public void A_missing_vault_root_is_not_accessible_and_names_the_root()
	{
		var root = TempRoot(); // never created — simulates an unmounted or removed vault.

		Assert.False(PolicyForRoot(root).IsVaultStructurallyAccessible(out var inaccessiblePath));
		Assert.NotNull(inaccessiblePath);
		Assert.Contains(Path.GetFileName(root), inaccessiblePath!, StringComparison.Ordinal);
	}

	[Fact]
	public void An_entity_root_that_does_not_exist_yet_does_not_fail_the_probe()
	{
		// A not-yet-created entity root is normal, not a failure; only a root that exists but cannot be read is tier-2.
		var root = TempRoot();
		Directory.CreateDirectory(root); // vault root exists, no entity subfolders materialized yet.
		try
		{
			Assert.True(PolicyForRoot(root).IsVaultStructurallyAccessible(out _));
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}
}
