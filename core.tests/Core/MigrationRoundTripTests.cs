using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Tests.Harness;
using Pleiades.Vault;
using Pleiades.Vault.Migration;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// PEP092 vault migration: a legacy (v1) objective file is re-canonicalised to the quiet form, preserving its body.
/// </summary>
public sealed class MigrationRoundTripTests : VaultTestBase
{
	[Fact]
	public async Task Migrates_legacy_objective_to_quiet_form_preserving_body()
	{
		// Seed a database row (implicit create writes no file), then place a legacy v1 file for it and downgrade the vault.
		var objective = await Vault.SeedStandaloneObjectiveAsync("Ship it");
		var legacyRelative = $"Objectives/{objective.Id} - Ship it.md";
		Vault.WriteVaultFile(legacyRelative,
			"---" + Environment.NewLine +
			"college: Unspecified" + Environment.NewLine +
			"status: Standby" + Environment.NewLine +
			"starfire: 0" + Environment.NewLine +
			"enduring: False" + Environment.NewLine +
			"---" + Environment.NewLine +
			"Legacy body survives." + Environment.NewLine);

		var settings = VaultSettings.Load(Vault.Layout.SettingsPath);
		settings.SchemaVersion = VaultSchema.BaselineVersion;
		settings.Save(Vault.Layout.SettingsPath);

		await Vault.WithScopeAsync(services => services.GetRequiredService<VaultMigrationRunner>().RunAsync());

		Assert.True(Vault.VaultFileExists("Objectives/Ship it.md"));
		var content = Vault.ReadVaultFile("Objectives/Ship it.md");
		Assert.Contains($"puck: {objective.Id}", content);
		Assert.Contains("Legacy body survives.", content);
		Assert.False(Vault.VaultFileExists(legacyRelative));
		Assert.Equal(VaultSchema.CurrentVersion, VaultSettings.Load(Vault.Layout.SettingsPath).SchemaVersion);
		Assert.True(await Vault.QueryAsync(context => context.VaultMigrationHistory.AnyAsync()));
	}

	[Fact]
	public async Task Runner_is_a_noop_when_the_vault_is_already_current()
	{
		var pending = await Vault.WithScopeAsync(services =>
			Task.FromResult(services.GetRequiredService<VaultMigrationRunner>().GetPendingPlan().Count));

		Assert.Equal(0, pending);
	}
}
