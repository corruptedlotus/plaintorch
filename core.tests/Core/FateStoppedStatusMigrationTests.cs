using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Diagnostics;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Tests.Harness;
using Pleiades.Vault;
using Pleiades.Vault.Migration;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// v7→v8 vault migration (PEP103 Patch103.6): a fate note that still reads <c>status: Cancelled</c> is renamed to
/// <c>Stopped</c> in place, the rest of the note kept as written; and a note the migration never sees still reads, the old
/// name standing as an alias.
/// </summary>
public sealed class FateStoppedStatusMigrationTests : VaultTestBase
{
	private const string Note = "Fates/Solstice.md";

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	private Task<FateStatus> StatusAsync(string id)
		=> Vault.QueryAsync(context => context.Fates.AsNoTracking().IgnoreAutoIncludes()
			.Where(fate => fate.Id == id).Select(fate => fate.Status).SingleAsync(Ct));

	/// <summary>Creates a fate and materializes its note (a fate's note starts its boundary on request).</summary>
	private Task<Fate> CreateFateAsync() => Vault.WithScopeAsync(async services =>
	{
		var api = services.GetRequiredService<IDeclarativeApi>();
		var fate = await api.CreateFateAsync(new FatePlan("Solstice", Date: new DateOnly(2026, 12, 21)), Ct);
		return await api.BeginFateBoundaryAsync(fate.Id, Ct);
	});

	[Fact]
	public async Task Renames_the_status_of_a_legacy_note_in_place_and_survives_a_watcher_round_trip()
	{
		var fate = await CreateFateAsync();
		await Vault.WithScopeAsync(services => services.GetRequiredService<IDeclarativeApi>()
			.UpdateFateAsync(fate.Id, new FateUpdate(Status: FateStatus.Stopped), Ct));

		// The note as a v7 vault holds it, written by hand: the old name quoted and in another case, a comment, a body.
		var legacy = Vault.ReadVaultFile(Note).Replace("status: Stopped", "status: \"cancelled\" # paused", StringComparison.Ordinal)
			+ "Legacy body survives." + Environment.NewLine;
		Assert.Contains("\"cancelled\"", legacy);
		Vault.WriteVaultFile(Note, legacy);

		// Downgrade the stored version so the runner applies the v7→v8 migration (and only that one).
		var settings = VaultSettings.Load(Vault.Layout.SettingsPath);
		settings.SchemaVersion = 7;
		settings.Save(Vault.Layout.SettingsPath);

		await Vault.WithScopeAsync(services => services.GetRequiredService<VaultMigrationRunner>().RunAsync());

		// Only the value changed.
		Assert.Equal(legacy.Replace("\"cancelled\"", "\"Stopped\"", StringComparison.Ordinal), Vault.ReadVaultFile(Note));
		Assert.Equal(VaultSchema.CurrentVersion, VaultSettings.Load(Vault.Layout.SettingsPath).SchemaVersion);

		await Vault.ReconcileAsync(Vault.AbsolutePath(Note));
		Assert.Equal(FateStatus.Stopped, await StatusAsync(fate.Id));
	}

	[Fact]
	public async Task A_note_the_migration_never_saw_still_reads_its_old_status()
	{
		var fate = await CreateFateAsync();

		// A note brought back after the migration ran (a template, a restored copy) still names the status by its old name.
		Vault.WriteVaultFile(Note, Vault.ReadVaultFile(Note).Replace("status: Active", "status: Cancelled", StringComparison.Ordinal));
		await Vault.ReconcileWithIssuesAsync(Vault.AbsolutePath(Note));

		Assert.Equal(FateStatus.Stopped, await StatusAsync(fate.Id));
		Assert.DoesNotContain(Vault.GetSingleton<OperationStatusRegistry>().GetActiveStatuses(), status =>
			string.Equals(status.ScopeKey, Vault.AbsolutePath(Note), StringComparison.OrdinalIgnoreCase));
	}
}
