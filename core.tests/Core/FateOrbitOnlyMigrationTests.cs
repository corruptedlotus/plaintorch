using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Tests.Harness;
using Pleiades.Vault;
using Pleiades.Vault.Migration;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// v3→v4 vault migration: a fate whose on-disk file predates the orbit-only model (its one-off <c>date</c> keys and
/// no <c>orbit</c>) is re-emitted from the migrated database state so its frontmatter carries the composed
/// <c>Z{…}</c> orbit — otherwise the identity-gated watcher would sync a null orbit back over the migrated one on
/// first activation.
/// </summary>
public sealed class FateOrbitOnlyMigrationTests : VaultTestBase
{
	[Fact]
	public async Task Re_emits_a_legacy_fate_file_under_the_orbit_only_shape_and_survives_a_watcher_round_trip()
	{
		var ct = TestContext.Current.CancellationToken;

		// A one-off fate created through the API folds its date/time into a fixed-datetime literal orbit, and the
		// database carries that composed orbit — the state a real vault reaches after the database migration.
		var fate = await Vault.WithScopeAsync(s => s.GetRequiredService<IDeclarativeApi>()
			.CreateFateAsync(new FatePlan("Solstice", Date: new DateOnly(2026, 7, 20), StartTime: new TimeOnly(9, 0), EndTime: new TimeOnly(10, 30)), ct));
		Assert.Equal("Z{2026/7/20T09:00}=1h30m", fate.Orbit);

		// Materialize its file, then rewrite it in the pre-v4 legacy shape: the one-off date keys, and crucially no
		// `orbit`. This is the file a v3 vault holds on disk.
		await Vault.WithScopeAsync(s => s.GetRequiredService<IDeclarativeApi>().BeginFateBoundaryAsync(fate.Id, ct));
		var file = Vault.MarkdownFilesUnder(Vault.AbsolutePath(string.Empty)).Single(path =>
			Path.GetFileName(path).Contains(fate.Id) || File.ReadAllText(path).Contains(fate.Id) || File.ReadAllText(path).Contains("Solstice"));
		File.WriteAllText(file,
			"---" + Environment.NewLine +
			"status: Active" + Environment.NewLine +
			"calendar: Gregorian" + Environment.NewLine +
			"date: 2026-07-20" + Environment.NewLine +
			"startTime: 09:00:00" + Environment.NewLine +
			"endTime: 10:30:00" + Environment.NewLine +
			"---" + Environment.NewLine +
			"Legacy body survives." + Environment.NewLine);
		Assert.DoesNotContain("orbit:", File.ReadAllText(file));

		// Downgrade the stored version so the runner applies the v3→v4 fate migration (and only that one).
		var settings = VaultSettings.Load(Vault.Layout.SettingsPath);
		settings.SchemaVersion = 3;
		settings.Save(Vault.Layout.SettingsPath);

		// Act
		await Vault.WithScopeAsync(s => s.GetRequiredService<VaultMigrationRunner>().RunAsync());

		// The file is re-emitted from the database state: the orbit-only shape is restored (the notation may be
		// YAML-quoted), the body preserved.
		var migrated = File.ReadAllText(file);
		Assert.Contains("Z{2026/7/20T09:00}=1h30m", migrated);
		Assert.Contains("Legacy body survives.", migrated);
		Assert.Equal(VaultSchema.CurrentVersion, VaultSettings.Load(Vault.Layout.SettingsPath).SchemaVersion);

		// The whole point of the re-emit: the file now round-trips through the watcher without the missing `orbit`
		// wiping the migrated orbit back to null.
		await Vault.ReconcileAsync(file);
		var synced = await Vault.QueryAsync(context => context.Fates.AsNoTracking().FirstAsync(item => item.Id == fate.Id, ct));
		Assert.Equal("Z{2026/7/20T09:00}=1h30m", synced.Orbit);
	}
}
