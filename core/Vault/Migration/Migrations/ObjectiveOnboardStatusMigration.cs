using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Markdown;
using Pleiades.Vault.Database;

namespace Pleiades.Vault.Migration.Migrations;

/// <summary>
/// Renames the objective status <c>Onrush</c> to <c>Onboard</c> in objective notes (PEP103, Patch103.3).
/// </summary>
/// <remarks>
/// Advances the vault from v6 to v7. The <c>OnboardStatus</c> database migration has already renamed the stored statuses;
/// this renames them on disk, so the notes read as the core and its clients do. Only the value on the frontmatter
/// <c>status</c> line changes: the rest of each note stays as written, so an edit made while the core was away still
/// reaches the database through the watcher, as it would have without the rename. Only the objectives stored as Onboard are
/// looked at, since finding a note means reading the vault's candidates for it; a note that says Onrush for any other
/// objective was edited while the core was away. An objective without a note is left alone. A note this pass never sees
/// still reads, since <see cref="ObjectiveStatus.Onboard"/> keeps its old name as an alias.
/// </remarks>
public sealed class ObjectiveOnboardStatusMigration(
	PlainfraContext context,
	PlaintorchMarkdownStorageService storageService,
	VaultLayout layout,
	VaultAuditLogService auditLogService,
	ILogger<ObjectiveOnboardStatusMigration> logger) : IVaultMigration
{
	/// <inheritdoc />
	public int FromVersion => 6;

	/// <inheritdoc />
	public int ToVersion => 7;

	/// <inheritdoc />
	public string Id => "20261006_0001_ObjectiveOnboardStatus";

	/// <inheritdoc />
	public string Description => "Rename the objective status Onrush to Onboard in objective notes.";

	/// <inheritdoc />
	public async Task<VaultMigrationOutcome> ApplyAsync(CancellationToken cancellationToken = default)
	{
		var objectives = await context.Objectives
			.IgnoreAutoIncludes()
			.Where(objective => objective.Status == ObjectiveStatus.Onboard)
			.ToListAsync(cancellationToken);
		var rewritten = 0;

		foreach (var objective in objectives)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var relativePath = await storageService.TryResolveEntityRelativePathAsync(objective, cancellationToken);
			if (relativePath is null)
			{
				continue;
			}

			var path = Path.Combine(layout.VaultRoot, relativePath);
			var markdown = await File.ReadAllTextAsync(path, cancellationToken);
			if (!TryRenameStatus(markdown, out var renamed))
			{
				continue;
			}

			await File.WriteAllTextAsync(path, renamed, cancellationToken);
			rewritten++;

			await auditLogService.WriteAsync(
				"migration",
				"objective-onboard-status",
				subjectType: nameof(Objective),
				subjectId: objective.Id,
				subjectTitle: objective.Title,
				details: new { migration = Id, FromVersion, ToVersion, path = relativePath },
				cancellationToken: cancellationToken);
		}

		logger.LogInformation(
			"Vault migration {Migration} renamed the Onrush status to Onboard in {Count} objective note(s).",
			Id,
			rewritten);

		return new VaultMigrationOutcome(objectives.Count, rewritten, 0, 0, 0);
	}

	/// <summary>
	/// Renames the value of a note's frontmatter <c>status</c> line from <c>Onrush</c> to <c>Onboard</c>, keeping the key,
	/// any quotes and anything after the value as they are (<see cref="FrontmatterStatusRename"/>).
	/// </summary>
	internal static bool TryRenameStatus(string markdown, out string renamed)
		=> FrontmatterStatusRename.TryRename(markdown, "Onrush", nameof(ObjectiveStatus.Onboard), out renamed);
}
