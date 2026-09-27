using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Markdown;
using Pleiades.Vault.Database;

namespace Pleiades.Vault.Migration.Migrations;

/// <summary>
/// Folds each fate's legacy one-off date frontmatter into its orbit-only shape on disk (PEP111).
/// </summary>
/// <remarks>
/// Advances the vault from v3 to v4. The database migration has already folded each one-off fate's
/// Date/StartTime/EndTime/EventDuration into a fixed-datetime <c>Z{…}</c> orbit and pinned existing fates to the
/// Gregorian calendar. This re-emits every fate's markdown from that database state, so the on-disk frontmatter
/// carries the composed <c>orbit:</c> (and <c>calendar:</c>) rather than the dropped date keys — otherwise the
/// identity-gated watcher, reading a legacy file that still lacks <c>orbit:</c>, would sync a null orbit back over
/// the migrated one on first activation.
/// </remarks>
public sealed class FateOrbitOnlyMigration(
	PlainfraContext context,
	PlaintorchMarkdownStorageService storageService,
	VaultAuditLogService auditLogService,
	ILogger<FateOrbitOnlyMigration> logger) : IVaultMigration
{
	/// <inheritdoc />
	public int FromVersion => 3;

	/// <inheritdoc />
	public int ToVersion => 4;

	/// <inheritdoc />
	public string Id => "20260922_0001_FateOrbitOnly";

	/// <inheritdoc />
	public string Description => "Fold each fate's legacy one-off date frontmatter into its orbit-only shape on disk.";

	/// <inheritdoc />
	public async Task<VaultMigrationOutcome> ApplyAsync(CancellationToken cancellationToken = default)
	{
		var fates = await context.Fates.IgnoreAutoIncludes().ToListAsync(cancellationToken);
		var rewritten = 0;

		foreach (var fate in fates)
		{
			cancellationToken.ThrowIfCancellationRequested();

			// Re-emit from the database state (orbit already composed by the EF migration), so the file's front
			// matter gains `orbit:`/`calendar:` and no longer relies on the dropped date keys.
			await storageService.SaveFateAsync(fate, cancellationToken: cancellationToken);
			rewritten++;

			await auditLogService.WriteAsync(
				"migration",
				"fate-orbit-only",
				subjectType: nameof(Fate),
				subjectId: fate.Id,
				subjectTitle: fate.Title,
				details: new { migration = Id, FromVersion, ToVersion, fate.Orbit },
				cancellationToken: cancellationToken);
		}

		logger.LogInformation(
			"Vault migration {Migration} re-emitted {Count} fate(s) under the orbit-only shape.",
			Id,
			rewritten);

		return new VaultMigrationOutcome(fates.Count, rewritten, 0, 0, 0);
	}
}
