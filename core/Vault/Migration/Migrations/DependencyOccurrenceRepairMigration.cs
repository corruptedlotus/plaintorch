using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Materialization;
using Pleiades.Vault.Database;

namespace Pleiades.Vault.Migration.Migrations;

/// <summary>
/// Settles the stored dependencies whose eventive endpoint resolves to no row at exactly its slot (PEP103, I-035).
/// </summary>
/// <remarks>
/// <para>
/// Advances the vault from v8 to v9. Before this version a dependency could name an occurrence that never resolves: the
/// structure phase (SH-17) hardened the day's first occurrence when none started at the named slot, under that
/// occurrence's own slot, while the edge kept the slot it was given; and it quietly hardened nothing for a stopped fate or
/// a day off the fate's orbit. Endpoints match exactly, so such an edge can never be satisfied and gates its target for
/// good. New edges like it are refused now (<c>dependency.occurrence</c>), and this pass settles the stored ones once:
/// </para>
/// <list type="bullet">
/// <item><description>an endpoint whose slot is an exact, live occurrence of a temporally active fate has its row hardened
/// now, as SH-17 would harden it today, and the edge stays;</description></item>
/// <item><description>otherwise the edge is released, since nothing could ever satisfy it.</description></item>
/// </list>
/// <para>
/// Each edge settled leaves an audit row saying which, and why for a release, in the words the judge's refusal uses
/// (<see cref="OccurrenceHardeningService.DescribeUnresolvedReference"/>). A row hardened for an edge that is released anyway
/// (its other endpoint does not resolve) is not kept. Nothing on disk changes — dependencies and eventives live only in the
/// database — but the vault version is what gates the one-time pass.
/// </para>
/// </remarks>
public sealed class DependencyOccurrenceRepairMigration(
	PlainfraContext context,
	OccurrenceHardeningService hardeningService,
	VaultAuditLogService auditLogService,
	ILogger<DependencyOccurrenceRepairMigration> logger) : IVaultMigration
{
	/// <inheritdoc />
	public int FromVersion => 8;

	/// <inheritdoc />
	public int ToVersion => 9;

	/// <inheritdoc />
	public string Id => "20261107_0001_DependencyOccurrenceRepair";

	/// <inheritdoc />
	public string Description => "Harden or release the stored dependencies whose occurrence endpoint never resolves.";

	/// <inheritdoc />
	public async Task<VaultMigrationOutcome> ApplyAsync(CancellationToken cancellationToken = default)
	{
		var edges = await context.Dependencies
			.Where(dependency => dependency.SourceKind == DependencyEndpointKind.Eventive || dependency.TargetKind == DependencyEndpointKind.Eventive)
			.OrderBy(dependency => dependency.Id)
			.ToListAsync(cancellationToken);

		// Each endpoint is resolved once, whichever edges share it; a row it newly hardens is kept only while a kept edge needs it.
		var resolved = new Dictionary<EndpointRef, (Eventive? Row, bool Hardened)>();
		var needed = new HashSet<EndpointRef>();
		var released = 0;
		var repaired = 0;

		foreach (var edge in edges)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var endpoints = new[] { edge.Source, edge.Target }.Where(endpoint => endpoint.Kind == DependencyEndpointKind.Eventive).Distinct().ToList();
			foreach (var endpoint in endpoints.Where(endpoint => !resolved.ContainsKey(endpoint)))
			{
				var row = await hardeningService.EnsureReferencedEventiveAsync(endpoint, cancellationToken);
				resolved[endpoint] = (row, row is not null && context.Entry(row).State == EntityState.Added);
			}

			if (endpoints.Where(endpoint => resolved[endpoint].Row is null).Select(endpoint => (EndpointRef?)endpoint).FirstOrDefault() is { } unresolved)
			{
				var fate = await context.Fates.AsNoTracking().IgnoreAutoIncludes().FirstOrDefaultAsync(item => item.Id == unresolved.Id, cancellationToken);
				var (reason, message) = OccurrenceHardeningService.DescribeUnresolvedReference(fate, unresolved);
				context.Dependencies.Remove(edge);
				released++;
				auditLogService.Stage(
					"migration",
					"dependency.release",
					subjectType: nameof(Dependency),
					subjectId: edge.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
					details: new { migration = Id, FromVersion, ToVersion, reason, message, source = edge.Source, target = edge.Target, edge.Trigger, edge.Constraint });
				continue;
			}

			needed.UnionWith(endpoints);
			var hardened = endpoints.Where(endpoint => resolved[endpoint].Hardened).ToList();
			if (hardened.Count == 0)
			{
				continue;
			}

			repaired++;
			auditLogService.Stage(
				"migration",
				"dependency.repair",
				subjectType: nameof(Dependency),
				subjectId: edge.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
				details: new { migration = Id, FromVersion, ToVersion, reason = "occurrence-hardened", hardened, source = edge.Source, target = edge.Target, edge.Trigger, edge.Constraint });
		}

		var hardenedRows = 0;
		foreach (var (endpoint, (row, hardened)) in resolved)
		{
			if (!hardened || row is null)
			{
				continue;
			}

			if (needed.Contains(endpoint))
			{
				hardenedRows++;
			}
			else
			{
				context.Entry(row).State = EntityState.Detached;
			}
		}

		await context.SaveChangesAsync(cancellationToken);

		logger.LogInformation(
			"Vault migration {Migration} hardened {Hardened} occurrence(s) for {Repaired} dependency edge(s) and released {Released} of {Total} occurrence edge(s).",
			Id,
			hardenedRows,
			repaired,
			released,
			edges.Count);

		return new VaultMigrationOutcome(edges.Count, released, 0, hardenedRows, 0);
	}
}
