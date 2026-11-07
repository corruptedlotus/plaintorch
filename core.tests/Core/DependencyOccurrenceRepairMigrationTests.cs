using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Tests.Harness;
using Pleiades.Vault;
using Pleiades.Vault.Migration;
using Pleiades.Vault.Migration.Migrations;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// v8→v9 vault migration (PEP103 I-035): a vault written before dependencies were held to exact occurrence slots may hold
/// edges whose eventive endpoint never resolves. The migration hardens the row where the slot is an exact, live occurrence
/// of an active fate, releases every other such edge, records each on the audit log, and touches nothing on disk.
/// </summary>
public sealed class DependencyOccurrenceRepairMigrationTests : VaultTestBase
{
	private CancellationToken Ct => TestContext.Current.CancellationToken;

	private Task<T> Declarative<T>(Func<IDeclarativeApi, Task<T>> action) => Vault.WithScopeAsync(services => action(services.GetRequiredService<IDeclarativeApi>()));

	private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);
	private static DateTime At(DateOnly day, int hour) => day.ToDateTime(new TimeOnly(hour, 0));

	[Fact]
	public async Task Hardens_the_exact_live_slots_and_releases_the_edges_that_never_resolve()
	{
		var downstream = await Vault.WithScopeAsync(s => s.GetRequiredService<IDirectiveApi>().CreateStandaloneAsync("Downstream", cancellationToken: Ct));
		var upstream = await Vault.WithScopeAsync(s => s.GetRequiredService<IDirectiveApi>().CreateStandaloneAsync("Upstream", cancellationToken: Ct));
		var daily = await Declarative(api => api.CreateFateAsync(new FatePlan("Review", Orbit: "d[h{15}]", EventDuration: 30), Ct));
		var stopped = await Declarative(api => api.CreateFateAsync(new FatePlan("Retired", Orbit: "d[h{15}]", EventDuration: 30), Ct));
		var launchDay = Today.AddDays(10);
		var oneOff = await Declarative(api => api.CreateFateAsync(new FatePlan("Launch", Date: launchDay), Ct));

		// A slot hardened before the version change: its edge resolves already and must be left as it is.
		var alreadyDay = Today.AddDays(7);
		var already = await Declarative(api => api.UpdateEventiveAsync(new EventiveOccurrenceRef(daily.Id, At(alreadyDay, 15)), new EventiveUpdate(), Ct));
		await Declarative(api => api.UpdateFateAsync(stopped.Id, new FateUpdate(Status: FateStatus.Stopped), Ct));

		// Written straight to the table, as a pre-v9 vault holds them: the engine refuses the last three today, and it never
		// hardened the first, which an inexact sibling's snap would have left unresolved.
		var exactDay = Today.AddDays(5);
		var ids = new Dictionary<string, long>
		{
			["exact"] = await InsertEventiveEdgeAsync(daily.Id, At(exactDay, 15), downstream.Id),
			["already"] = await InsertEventiveEdgeAsync(daily.Id, At(alreadyDay, 15), downstream.Id),
			["inexact"] = await InsertEventiveEdgeAsync(daily.Id, At(Today.AddDays(6), 14), downstream.Id),
			["stopped"] = await InsertEventiveEdgeAsync(stopped.Id, At(exactDay, 15), downstream.Id),
			["off-orbit"] = await InsertEventiveEdgeAsync(oneOff.Id, launchDay.AddDays(1).ToDateTime(TimeOnly.MinValue), downstream.Id),
		};
		var ordinary = await Vault.WithScopeAsync(s => s.GetRequiredService<IDependencyApi>().CreateAsync(
			new EndpointRef(DependencyEndpointKind.Directive, upstream.Id),
			new EndpointRef(DependencyEndpointKind.Directive, downstream.Id),
			cancellationToken: Ct));
		Assert.Equal(6, await Vault.QueryAsync(context => context.Dependencies.CountAsync(Ct)));
		Assert.Equal(0, await Vault.QueryAsync(context => context.Eventives.CountAsync(item => item.FateId == daily.Id && item.RecurrenceId == At(exactDay, 15), Ct)));
		var disk = SnapshotNotes();

		// Downgrade the stored version so the runner applies the v8→v9 pass (and only that one).
		var settings = VaultSettings.Load(Vault.Layout.SettingsPath);
		settings.SchemaVersion = 8;
		settings.Save(Vault.Layout.SettingsPath);

		await Vault.WithScopeAsync(s => s.GetRequiredService<VaultMigrationRunner>().RunAsync());

		Assert.Equal(VaultSchema.CurrentVersion, VaultSettings.Load(Vault.Layout.SettingsPath).SchemaVersion);
		var remaining = await Vault.QueryAsync(context => context.Dependencies.AsNoTracking().Select(dependency => dependency.Id).ToListAsync(Ct));
		Assert.Equal(new[] { ids["exact"], ids["already"], ordinary.Id }.Order(), remaining.Order());

		// The exact slot is a durable row now, at its own slot, and the edge on it gates its target as any pending one does.
		var hardened = await Vault.QueryAsync(context => context.Eventives.AsNoTracking().SingleAsync(item => item.FateId == daily.Id && item.RecurrenceId == At(exactDay, 15), Ct));
		Assert.Equal(EventiveResolution.Pending, hardened.Resolution);
		Assert.Equal(2, await Vault.QueryAsync(context => context.Eventives.CountAsync(item => item.FateId == daily.Id, Ct)));
		Assert.Equal(already.Id, await Vault.QueryAsync(context => context.Eventives.Where(item => item.FateId == daily.Id && item.RecurrenceId == At(alreadyDay, 15)).Select(item => item.Id).SingleAsync(Ct)));
		Assert.Equal(0, await Vault.QueryAsync(context => context.Eventives.CountAsync(item => item.FateId == stopped.Id || item.FateId == oneOff.Id, Ct)));

		var audit = await Vault.QueryAsync(context => context.AuditLogEntries.AsNoTracking()
			.Where(entry => entry.Category == "migration" && (entry.Action == "dependency.release" || entry.Action == "dependency.repair"))
			.ToListAsync(Ct));
		Assert.Equal(4, audit.Count);
		AssertAudit(audit, "dependency.repair", ids["exact"], "occurrence-hardened");
		AssertAudit(audit, "dependency.release", ids["inexact"], "no-occurrence");
		AssertAudit(audit, "dependency.release", ids["stopped"], "fate-stopped");
		AssertAudit(audit, "dependency.release", ids["off-orbit"], "no-occurrence");

		// Nothing on disk changes: dependencies and eventives live only in the database.
		Assert.Equal(disk, SnapshotNotes());

		// Idempotent: a second pass over the settled edges hardens and releases nothing.
		var again = await Vault.WithScopeAsync(s => s.GetServices<IVaultMigration>().OfType<DependencyOccurrenceRepairMigration>().Single().ApplyAsync(Ct));
		Assert.Equal(0, again.Rewritten);
		Assert.Equal(0, again.Imported);
	}

	[Fact]
	public async Task Keeps_no_row_hardened_for_an_edge_it_releases()
	{
		var daily = await Declarative(api => api.CreateFateAsync(new FatePlan("Review", Orbit: "d[h{15}]", EventDuration: 30), Ct));
		var day = Today.AddDays(5);

		// An occurrence-to-occurrence edge whose source is exact and whose target is not: the source would harden, but the
		// edge goes, so its row must not stay behind.
		var id = await Vault.QueryAsync(context => context.Database.ExecuteSqlAsync($"""
			INSERT INTO Dependencies (SourceKind, SourceId, SourceRecurrenceId, TargetKind, TargetId, TargetRecurrenceId, Satisfied)
			VALUES ('Eventive', {daily.Id}, {At(day, 15)}, 'Eventive', {daily.Id}, {At(day.AddDays(1), 14)}, 0)
			""", Ct));
		Assert.Equal(1, id);

		var settings = VaultSettings.Load(Vault.Layout.SettingsPath);
		settings.SchemaVersion = 8;
		settings.Save(Vault.Layout.SettingsPath);
		await Vault.WithScopeAsync(s => s.GetRequiredService<VaultMigrationRunner>().RunAsync());

		Assert.Equal(0, await Vault.QueryAsync(context => context.Dependencies.CountAsync(Ct)));
		Assert.Equal(0, await Vault.QueryAsync(context => context.Eventives.CountAsync(item => item.FateId == daily.Id, Ct)));
	}

	private static void AssertAudit(IReadOnlyList<Pleiades.Vault.Database.AuditLogEntry> audit, string action, long edgeId, string reason)
		=> Assert.Contains(audit, entry => entry.Action == action
			&& entry.SubjectId == edgeId.ToString(CultureInfo.InvariantCulture)
			&& entry.DetailsJson!.Contains($"\"{reason}\"", StringComparison.Ordinal));

	// Bypasses the engine, which would refuse most of these edges today: a raw insert is what a pre-v9 vault holds.
	private Task<long> InsertEventiveEdgeAsync(string fateId, DateTime slot, string directiveId) => Vault.QueryAsync(async context =>
	{
		await context.Database.ExecuteSqlAsync($"""
			INSERT INTO Dependencies (SourceKind, SourceId, SourceRecurrenceId, TargetKind, TargetId, TargetRecurrenceId, Satisfied)
			VALUES ('Eventive', {fateId}, {slot}, 'Directive', {directiveId}, NULL, 0)
			""", Ct);
		return await context.Dependencies.AsNoTracking().MaxAsync(dependency => dependency.Id, Ct);
	});

	private Dictionary<string, string> SnapshotNotes()
		=> Directory.EnumerateFiles(Vault.Layout.VaultRoot, "*.md", SearchOption.AllDirectories)
			.ToDictionary(path => path, File.ReadAllText, StringComparer.OrdinalIgnoreCase);
}
