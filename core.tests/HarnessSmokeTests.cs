using Microsoft.EntityFrameworkCore;
using Pleiades.Tests.Harness;
using Pleiades.Vault;
using Xunit;

namespace Pleiades.Tests;

/// <summary>
/// Proves the test harness builds the real DI graph over an isolated vault and initializes it.
/// </summary>
public sealed class HarnessSmokeTests : IAsyncLifetime
{
	private readonly TestVault _vault = new();

	public ValueTask InitializeAsync() => _vault.InitializeAsync();

	public ValueTask DisposeAsync() => _vault.DisposeAsync();

	[Fact]
	public async Task Initializes_vault_layout_and_empty_database()
	{
		Assert.True(Directory.Exists(_vault.Layout.ObjectivesRoot));
		Assert.True(Directory.Exists(_vault.Layout.MetadataRoot));

		var objectiveCount = await _vault.QueryAsync(context => context.Objectives.CountAsync());
		Assert.Equal(0, objectiveCount);
	}

	[Fact]
	public async Task Fresh_vault_is_stamped_at_current_schema_version()
	{
		var stored = VaultSettings.Load(_vault.Layout.SettingsPath).SchemaVersion;
		Assert.Equal(VaultSchema.CurrentVersion, stored);
		await Task.CompletedTask;
	}
}
