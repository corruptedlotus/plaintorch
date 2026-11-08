using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Markdown;
using Pleiades.Vault.Database;

namespace Pleiades.Vault.Migration.Migrations;

/// <summary>
/// Renames the fate status <c>Cancelled</c> to <c>Stopped</c> in fate notes (PEP103, Patch103.6).
/// </summary>
/// <remarks>
/// Advances the vault from v7 to v8. The <c>FateStoppedStatus</c> database migration has already renamed the stored statuses;
/// this renames them on disk, so the notes read as the core and its clients do. Only the value on the frontmatter
/// <c>status</c> line changes: the rest of each note stays as written, so an edit made while the core was away still reaches
/// the database through the watcher, as it would have without the rename. Only the fates stored as Stopped are looked at,
/// since finding a note means reading the vault's candidates for it; a note that says Cancelled for any other fate was edited
/// while the core was away. A note this pass never sees still reads, since <see cref="FateStatus.Stopped"/> keeps its old
/// name as an alias.
/// </remarks>
public sealed class FateStoppedStatusMigration(
	PlainfraContext context,
	PlaintorchMarkdownStorageService storageService,
	VaultLayout layout,
	VaultAuditLogService auditLogService,
	ILogger<FateStoppedStatusMigration> logger) : IVaultMigration
{
	/// <inheritdoc />
	public int FromVersion => 7;

	/// <inheritdoc />
	public int ToVersion => 8;

	/// <inheritdoc />
	public string Id => "20261011_0001_FateStoppedStatus";

	/// <inheritdoc />
	public string Description => "Rename the fate status Cancelled to Stopped in fate notes.";

	/// <inheritdoc />
	public async Task<VaultMigrationOutcome> ApplyAsync(CancellationToken cancellationToken = default)
	{
		var fates = await context.Fates
			.IgnoreAutoIncludes()
			.Where(fate => fate.Status == FateStatus.Stopped)
			.ToListAsync(cancellationToken);
		var rewritten = 0;

		foreach (var fate in fates)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var relativePath = await storageService.TryResolveEntityRelativePathAsync(fate, cancellationToken);
			if (relativePath is null)
			{
				continue;
			}

			var path = Path.Combine(layout.VaultRoot, relativePath);
			var markdown = await File.ReadAllTextAsync(path, cancellationToken);
			if (!FrontmatterStatusRename.TryRename(markdown, "Cancelled", nameof(FateStatus.Stopped), out var renamed))
			{
				continue;
			}

			await File.WriteAllTextAsync(path, renamed, cancellationToken);
			rewritten++;

			await auditLogService.WriteAsync(
				"migration",
				"fate-stopped-status",
				subjectType: nameof(Fate),
				subjectId: fate.Id,
				subjectTitle: fate.Title,
				details: new { migration = Id, FromVersion, ToVersion, path = relativePath },
				cancellationToken: cancellationToken);
		}

		logger.LogInformation(
			"Vault migration {Migration} renamed the Cancelled status to Stopped in {Count} fate note(s).",
			Id,
			rewritten);

		return new VaultMigrationOutcome(fates.Count, rewritten, 0, 0, 0);
	}
}
