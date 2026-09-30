using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Tests.Harness;
using Pleiades.Vault;
using Pleiades.Vault.Migration;
using Pleiades.Vault.Migration.Migrations;
using Xunit;

namespace Pleiades.Tests.Core;

/// <summary>
/// v4→v5 vault migration (PEP102 patch 9): a vault written before a directive's finish depended on its children may hold
/// stored dependencies the intrinsic edges now contradict, or that merely restate them. The migration purges both —
/// contradicted edges newest first until the graph orders, redundant ones outright — on the record, and touches nothing
/// on disk.
/// </summary>
public sealed class IntrinsicDependencyPurgeMigrationTests : VaultTestBase
{
	[Fact]
	public async Task Purges_the_contradicted_and_redundant_stored_edges_and_keeps_the_rest()
	{
		var ct = TestContext.Current.CancellationToken;
		var alpha = await Vault.WithScopeAsync(s => s.GetRequiredService<IDirectiveApi>().CreateStandaloneAsync("Alpha", cancellationToken: ct));
		var beta = await Vault.WithScopeAsync(s => s.GetRequiredService<IDirectiveApi>().CreateStandaloneAsync("Beta", cancellationToken: ct));
		var a = await Vault.WithScopeAsync(s => s.GetRequiredService<IObjectiveApi>().CreateFromDirectiveAsync(alpha.Id, "A quest", cancellationToken: ct));
		var b = await Vault.WithScopeAsync(s => s.GetRequiredService<IObjectiveApi>().CreateFromDirectiveAsync(beta.Id, "B quest", cancellationToken: ct));

		// Written straight to the table, as a pre-v5 vault holds them — the API would refuse the first three today.
		// The cross-tree pair loops through the two intrinsic edges (older first, so the newer is the one to go); the
		// restatement is redundant; the last is an ordinary edge that must survive.
		var ids = await Vault.QueryAsync(async context =>
		{
			var older = Row(DirectiveRef(alpha.Id), ObjectiveRef(b.Id));
			var newer = Row(DirectiveRef(beta.Id), ObjectiveRef(a.Id));
			var restated = Row(ObjectiveRef(a.Id), DirectiveRef(alpha.Id), DependencyTrigger.OnFinish, DependencyConstraint.ToFinish);
			var ordinary = Row(ObjectiveRef(a.Id), ObjectiveRef(b.Id));
			context.Dependencies.AddRange(older, newer, restated, ordinary);
			await context.SaveChangesAsync(ct);
			return (older.Id, newer.Id, restated.Id, ordinary.Id);
		});
		Assert.Equal(4, await Vault.QueryAsync(context => context.Dependencies.CountAsync(ct)));

		// Downgrade the stored version so the runner applies the v4→v5 purge (and only that one).
		var settings = VaultSettings.Load(Vault.Layout.SettingsPath);
		settings.SchemaVersion = 4;
		settings.Save(Vault.Layout.SettingsPath);

		await Vault.WithScopeAsync(s => s.GetRequiredService<VaultMigrationRunner>().RunAsync());

		var remaining = await Vault.QueryAsync(context => context.Dependencies.AsNoTracking().Select(dependency => dependency.Id).ToListAsync(ct));
		Assert.Equal([ids.Item1, ids.Item4], remaining.OrderBy(id => id));
		Assert.Equal(VaultSchema.CurrentVersion, VaultSettings.Load(Vault.Layout.SettingsPath).SchemaVersion);

		var audit = await Vault.QueryAsync(context => context.AuditLogEntries.AsNoTracking()
			.Where(entry => entry.Category == "migration" && entry.Action == "dependency.purge")
			.ToListAsync(ct));
		Assert.Equal(2, audit.Count);
		Assert.Contains(audit, entry => entry.SubjectId == ids.Item2.ToString(System.Globalization.CultureInfo.InvariantCulture) && entry.DetailsJson!.Contains("contradicted"));
		Assert.Contains(audit, entry => entry.SubjectId == ids.Item3.ToString(System.Globalization.CultureInfo.InvariantCulture) && entry.DetailsJson!.Contains("redundant"));

		// Idempotent: a second pass over the clean graph purges nothing.
		var again = await Vault.WithScopeAsync(s => s.GetServices<IVaultMigration>().OfType<IntrinsicDependencyPurgeMigration>().Single().ApplyAsync(ct));
		Assert.Equal(0, again.Rewritten);
	}

	private static EndpointRef DirectiveRef(string id) => new(DependencyEndpointKind.Directive, id);
	private static EndpointRef ObjectiveRef(string id) => new(DependencyEndpointKind.Objective, id);

	private static Dependency Row(EndpointRef source, EndpointRef target, DependencyTrigger? trigger = null, DependencyConstraint? constraint = null)
		=> new() { SourceKind = source.Kind, SourceId = source.Id, TargetKind = target.Kind, TargetId = target.Id, Trigger = trigger, Constraint = constraint };
}
