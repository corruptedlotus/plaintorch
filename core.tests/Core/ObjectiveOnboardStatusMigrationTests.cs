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
/// v6→v7 vault migration (PEP103 Patch103.3): an objective note that still reads <c>status: Onrush</c> is renamed to
/// <c>Onboard</c> in place, the rest of the note kept as written; and a note the migration never sees still reads, the old
/// name standing as an alias.
/// </summary>
public sealed class ObjectiveOnboardStatusMigrationTests : VaultTestBase
{
	private const string Note = "Objectives/Quest.md";

	private static CancellationToken Ct => TestContext.Current.CancellationToken;

	private Task<ObjectiveStatus> StatusAsync(string id)
		=> Vault.QueryAsync(context => context.Objectives.AsNoTracking().IgnoreAutoIncludes()
			.Where(objective => objective.Id == id).Select(objective => objective.Status).SingleAsync(Ct));

	[Fact]
	public async Task Renames_the_status_of_a_legacy_note_in_place_and_survives_a_watcher_round_trip()
	{
		var objective = await Vault.SeedStandaloneObjectiveAsync("Quest");
		await Vault.BeginObjectiveBoundaryAsync(objective.Id);
		await Vault.WithScopeAsync(services => services.GetRequiredService<IObjectiveApi>()
			.ShiftWorkflowAsync(objective.Id, new ObjectiveWorkflowShift(ObjectiveStatus.Onboard), Ct));

		// The note as a v6 vault holds it, written by hand: the old name quoted and in another case, a comment, a body.
		var legacy = Vault.ReadVaultFile(Note).Replace("status: Onboard", "status: \"onrush\" # sprint work", StringComparison.Ordinal)
			+ "Legacy body survives." + Environment.NewLine;
		Vault.WriteVaultFile(Note, legacy);

		// Downgrade the stored version so the runner applies the v6→v7 migration (and only that one).
		var settings = VaultSettings.Load(Vault.Layout.SettingsPath);
		settings.SchemaVersion = 6;
		settings.Save(Vault.Layout.SettingsPath);

		await Vault.WithScopeAsync(services => services.GetRequiredService<VaultMigrationRunner>().RunAsync());

		// Only the value changed.
		Assert.Equal(legacy.Replace("\"onrush\"", "\"Onboard\"", StringComparison.Ordinal), Vault.ReadVaultFile(Note));
		Assert.Equal(VaultSchema.CurrentVersion, VaultSettings.Load(Vault.Layout.SettingsPath).SchemaVersion);

		await Vault.ReconcileAsync(Vault.AbsolutePath(Note));
		Assert.Equal(ObjectiveStatus.Onboard, await StatusAsync(objective.Id));
	}

	[Fact]
	public async Task A_note_the_migration_never_saw_still_reads_its_old_status()
	{
		var objective = await Vault.SeedStandaloneObjectiveAsync("Quest");
		await Vault.BeginObjectiveBoundaryAsync(objective.Id);

		// A note brought back after the migration ran (a template, a restored copy) still names the status by its old name.
		Vault.WriteVaultFile(Note, Vault.ReadVaultFile(Note).Replace("status: Standby", "status: Onrush", StringComparison.Ordinal));
		await Vault.ReconcileWithIssuesAsync(Vault.AbsolutePath(Note));

		Assert.Equal(ObjectiveStatus.Onboard, await StatusAsync(objective.Id));
		Assert.DoesNotContain(Vault.GetSingleton<OperationStatusRegistry>().GetActiveStatuses(), status =>
			string.Equals(status.ScopeKey, Vault.AbsolutePath(Note), StringComparison.OrdinalIgnoreCase));
	}
}
