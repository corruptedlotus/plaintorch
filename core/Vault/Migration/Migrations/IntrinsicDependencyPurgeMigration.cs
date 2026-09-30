using Microsoft.Extensions.Logging;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.State;
using Pleiades.Vault.Database;

namespace Pleiades.Vault.Migration.Migrations;

/// <summary>
/// Purges the stored dependencies the intrinsic directive-children edges contradict or make redundant (PEP102 patch 9).
/// </summary>
/// <remarks>
/// Advances the vault from v4 to v5. Before this version a directive's finish did not depend on its children, so a vault
/// may hold edges the new intrinsic ones now forbid — a directive's finish gating the begin of one of its own
/// objectives, say — or edges that merely restate what the structure implies (a child's finish gating its parent's
/// finish). Dependencies are the slave of structure, so it is the stored edges that give way: of the contradicted
/// ones, the one whose removal resolves the most (the newest on a tie), round by round until the graph orders
/// cleanly; the redundant ones all at once. Nothing on disk changes — dependencies live only in the database — but
/// the vault version is what gates the one-time pass.
/// </remarks>
public sealed class IntrinsicDependencyPurgeMigration(
	PlainfraContext context,
	DependencyGraphService graphService,
	VaultAuditLogService auditLogService,
	ILogger<IntrinsicDependencyPurgeMigration> logger) : IVaultMigration
{
	/// <inheritdoc />
	public int FromVersion => 4;

	/// <inheritdoc />
	public int ToVersion => 5;

	/// <inheritdoc />
	public string Id => "20260930_0001_IntrinsicDependencyPurge";

	/// <inheritdoc />
	public string Description => "Purge the stored dependencies the intrinsic directive-children edges contradict or make redundant.";

	/// <inheritdoc />
	public async Task<VaultMigrationOutcome> ApplyAsync(CancellationToken cancellationToken = default)
	{
		var graph = await graphService.LoadGraphAsync(cancellationToken);
		// No preference among the contradicted edges: the most resolving goes each round, the newest on a tie.
		var contradicted = DependencyOrdering.PlanPurge(graph.All, candidates => candidates.Select(candidate => candidate.Edge));
		var redundant = DependencyOrdering.Redundant(graph.All).Except(contradicted).ToList();

		foreach (var (edge, reason) in contradicted.Select(edge => (edge, "contradicted")).Concat(redundant.Select(edge => (edge, "redundant"))))
		{
			cancellationToken.ThrowIfCancellationRequested();
			context.Dependencies.Remove(edge);
			auditLogService.Stage(
				"migration",
				"dependency.purge",
				subjectType: nameof(Dependency),
				subjectId: edge.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
				details: new { migration = Id, FromVersion, ToVersion, reason, source = edge.Source, target = edge.Target, edge.Trigger, edge.Constraint });
		}

		await context.SaveChangesAsync(cancellationToken);

		logger.LogInformation(
			"Vault migration {Migration} purged {Contradicted} contradicted and {Redundant} redundant dependency edge(s) of {Total}.",
			Id,
			contradicted.Count,
			redundant.Count,
			graph.Explicit.Count);

		return new VaultMigrationOutcome(graph.Explicit.Count, contradicted.Count + redundant.Count, 0, 0, 0);
	}
}
