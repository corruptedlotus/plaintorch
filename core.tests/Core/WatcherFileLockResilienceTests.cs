using Pleiades.Tests.Harness;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// A markdown file another process holds open must not abort the whole discovery scan. The locked file is skipped
/// (counted as missing) and every other candidate is still discovered — the resilience that keeps the watcher's
/// startup sweep from failing wholesale when an editor has one note open.
/// </summary>
public sealed class WatcherFileLockResilienceTests : VaultTestBase
{
	[Fact]
	public async Task Locked_file_is_skipped_and_scan_still_discovers_the_rest()
	{
		var kept = await Vault.SeedStandaloneObjectiveAsync("Kept Objective");
		await Vault.BeginObjectiveBoundaryAsync(kept.Id);
		var locked = await Vault.SeedStandaloneObjectiveAsync("Locked Objective");
		await Vault.BeginObjectiveBoundaryAsync(locked.Id);

		var lockedPath = Path.Combine(Vault.Layout.ObjectivesRoot, "Locked Objective.md");
		Assert.True(File.Exists(lockedPath));

		// Hold the file open with no sharing, as an editor mid-edit would — every other open of it now fails.
		using (new FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
		{
			var result = await Vault.ScanAsync();

			// The locked file did not abort the sweep: its readable sibling is still discovered...
			Assert.Contains(
				result.Candidates,
				candidate => candidate.VaultRelativePath.Contains("Kept Objective", StringComparison.OrdinalIgnoreCase));
			// ...while the locked file was skipped rather than surfaced as a candidate, and counted as missing.
			Assert.DoesNotContain(
				result.Candidates,
				candidate => candidate.VaultRelativePath.Contains("Locked Objective", StringComparison.OrdinalIgnoreCase));
			Assert.True(result.MissingPaths >= 1);
		}
	}
}
