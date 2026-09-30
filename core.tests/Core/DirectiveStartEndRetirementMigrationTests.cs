using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Orchestration.Lifecycle;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Tests.Harness;
using Pleiades.Vault;
using Pleiades.Vault.Migration;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// v4→v5 vault migration (PEP101): a stellar directive's start and end dates became backlog schedules in the database, so
/// the <c>startDate</c>/<c>endDate</c> keys left in its note are values nothing reads. The migration re-emits every
/// directive note that still carries one, dropping them and keeping everything else; a note without them is left as it is.
/// </summary>
public sealed class DirectiveStartEndRetirementMigrationTests : VaultTestBase
{
	private const string CarryingNote = "Directives/Campaign/Campaign.md";
	private const string CleanNote = "Directives/Quiet Campaign/Quiet Campaign.md";

	[Fact]
	public async Task Drops_the_retired_keys_from_directive_notes_and_survives_a_watcher_round_trip()
	{
		var ct = TestContext.Current.CancellationToken;
		var carrying = await Vault.WithScopeAsync(services => services.GetRequiredService<IDirectiveApi>().CreateStandaloneAsync("Campaign", cancellationToken: ct));
		var clean = await Vault.WithScopeAsync(services => services.GetRequiredService<IDirectiveApi>().CreateStandaloneAsync("Quiet Campaign", cancellationToken: ct));

		// The state the database migration leaves behind: the dates live on as plans.
		await Vault.WithScopeAsync(services => services.GetRequiredService<IBacklogScheduleApi>()
			.AddAsync(carrying.Id, BacklogScheduleType.Plan, LifecyclePhase.Begin, new DateTime(2026, 7, 1), ct));

		// The note a v4 vault holds on disk: the retired keys beside a custom one, and an authored body.
		var current = Vault.ReadVaultFile(CarryingNote);
		Assert.StartsWith("---", current);
		var legacy = "---" + Environment.NewLine
			+ "startDate: 2026-07-01" + Environment.NewLine
			+ "endDate: 2026-09-01" + Environment.NewLine
			+ "mood: bright" + Environment.NewLine
			+ current["---".Length..].TrimStart('\r', '\n')
			+ "Legacy body survives." + Environment.NewLine;
		Vault.WriteVaultFile(CarryingNote, legacy);
		var cleanBefore = Vault.ReadVaultFile(CleanNote);

		// Downgrade the stored version so the runner applies the v4→v5 migration (the later dependency purge touches no
		// note).
		var settings = VaultSettings.Load(Vault.Layout.SettingsPath);
		settings.SchemaVersion = 4;
		settings.Save(Vault.Layout.SettingsPath);

		// Act
		await Vault.WithScopeAsync(services => services.GetRequiredService<VaultMigrationRunner>().RunAsync(ct));

		var migrated = Vault.ReadVaultFile(CarryingNote);
		Assert.DoesNotContain("startDate", migrated);
		Assert.DoesNotContain("endDate", migrated);
		Assert.Contains("mood: bright", migrated);
		Assert.Contains($"puck: {carrying.Id}", migrated);
		Assert.Contains("status: Planned", migrated);
		Assert.Contains("Legacy body survives.", migrated);
		Assert.Equal(cleanBefore, Vault.ReadVaultFile(CleanNote));
		Assert.Equal(VaultSchema.CurrentVersion, VaultSettings.Load(Vault.Layout.SettingsPath).SchemaVersion);

		var audited = await Vault.QueryAsync(context => context.AuditLogEntries
			.Where(entry => entry.Action == "directive-start-end-retirement")
			.Select(entry => entry.SubjectId)
			.ToListAsync(ct));
		Assert.Equal([carrying.Id], audited);

		// The rewritten note round-trips through the watcher: the directive and its plan are untouched.
		await Vault.ReconcileAsync(Vault.AbsolutePath(CarryingNote));
		var synced = await Vault.QueryAsync(context => context.Directives.AsNoTracking().SingleAsync(item => item.Id == carrying.Id, ct));
		Assert.Equal("Campaign", synced.Title);
		var plan = await Vault.QueryAsync(context => context.BacklogSchedules.AsNoTracking().SingleAsync(item => item.DirectiveId == carrying.Id, ct));
		Assert.Equal(new DateTime(2026, 7, 1), plan.Time);
		Assert.Equal(clean.Id, (await Vault.QueryAsync(context => context.Directives.AsNoTracking().SingleAsync(item => item.Id == clean.Id, ct))).Id);
	}
}
