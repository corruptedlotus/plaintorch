using Microsoft.Extensions.DependencyInjection;
using Pleiades.Diagnostics;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Tests.Harness;
using Pleiades.Vault.Watcher;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// Directives are freeform: their note may live anywhere in the vault, not only under the canonical Directives root.
/// These guard two gaps once observed when a directive's file lives outside that root:
///  (1) modifying it through the API must rewrite the real file in place, not spawn a second copy at the canonical
///      location — save-time file location by identity must search the whole freeform territory, as discovery does; and
///  (2) when two files assert the same directive identity, the watcher must flag the ambiguity as an error rather than
///      silently syncing both (directive files were invisible to discovery, so no issue was ever raised).
/// </summary>
public sealed class DirectiveOutOfRootDuplicationTests : VaultTestBase
{
	private Task<StellarDirective> CreateDirectiveAsync(string title)
		=> Vault.WithScopeAsync(services => services.GetRequiredService<IDirectiveApi>()
			.CreateStandaloneAsync(title, cancellationToken: TestContext.Current.CancellationToken));

	private Task<StellarDirective> UpdateAsync(string id, StellarDirectiveUpdate update)
		=> Vault.WithScopeAsync(services => services.GetRequiredService<IDirectiveApi>()
			.UpdateStellarAsync(id, update, TestContext.Current.CancellationToken));

	/// <summary>Counts the markdown files anywhere in the vault (outside metadata) that assert the given PUCK.</summary>
	private int CountFilesAssertingPuck(string puck)
	{
		return Vault.MarkdownFilesUnder(Vault.VaultRoot)
			.Where(path => !path.StartsWith(Vault.Layout.MetadataRoot, StringComparison.OrdinalIgnoreCase))
			.Count(path => File.ReadAllText(path).Contains($"puck: {puck}", StringComparison.Ordinal));
	}

	[Fact]
	public async Task Modifying_an_out_of_root_directive_rewrites_it_in_place_without_duplicating_to_the_default_root()
	{
		var directive = await CreateDirectiveAsync("Campaign");
		Assert.True(Vault.VaultFileExists("Directives/Campaign/Campaign.md"));

		// The user keeps their directive somewhere else in the vault — freeform storage allows a directive to live
		// anywhere that is not another entity's managed root.
		Directory.CreateDirectory(Vault.AbsolutePath("Projects"));
		Directory.Move(Vault.AbsolutePath("Directives/Campaign"), Vault.AbsolutePath("Projects/Campaign"));
		Assert.True(Vault.VaultFileExists("Projects/Campaign/Campaign.md"));
		Assert.False(Vault.VaultFileExists("Directives/Campaign/Campaign.md"));

		// Modify it through the API.
		await UpdateAsync(directive.Id, new StellarDirectiveUpdate(Codename: "OPS"));

		// The edit must rewrite the file where it actually lives — not spawn a second copy in the default root.
		Assert.Equal(1, CountFilesAssertingPuck(directive.Id));
		Assert.False(Vault.VaultFileExists("Directives/Campaign/Campaign.md"));
		Assert.True(Vault.VaultFileExists("Projects/Campaign/Campaign.md"));
		Assert.Contains("codename: OPS", Vault.ReadVaultFile("Projects/Campaign/Campaign.md"));
	}

	[Fact]
	public async Task Two_files_asserting_the_same_directive_identity_are_flagged_by_the_watcher()
	{
		var directive = await CreateDirectiveAsync("Campaign");
		var canonical = Vault.ReadVaultFile("Directives/Campaign/Campaign.md");

		// A second file elsewhere asserting the SAME PUCK: an ambiguous duplicate identity (the aftershock state the
		// duplication bug leaves behind, or a stray copy a user made).
		Vault.WriteVaultFile("Projects/Campaign/Campaign.md", canonical);
		Assert.Equal(2, CountFilesAssertingPuck(directive.Id));

		await Vault.SweepWithIssuesAsync();

		// A duplicate-identity error, keyed on the identity itself (not a file path), naming the conflicting files.
		var issues = Vault.GetSingleton<OperationStatusRegistry>().GetActiveStatuses();
		Assert.Contains(issues, status =>
			status.ReasonCode == WatcherOperations.DuplicateIdentity
			&& status.Severity >= OperationSeverity.Error
			&& string.Equals(status.ScopeKey, directive.Id, StringComparison.Ordinal));
	}
}
