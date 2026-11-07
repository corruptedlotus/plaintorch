using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pleiades.Orchestration;
using Pleiades.Plaintorch.Markdown;
using Pleiades.Vault.Database;
using Pleiades.Vault.Markdown;

namespace Pleiades.Vault.Migration.Migrations;

/// <summary>
/// Drops the retired <c>startDate</c>/<c>endDate</c> frontmatter keys from stellar directive notes (PEP101).
/// </summary>
/// <remarks>
/// Advances the vault from v4 to v5. The database migration has already carried each directive's start and end dates
/// into its planned begin and finish (backlog schedules) and dropped the columns, so the keys left in the notes are
/// values nothing reads any more. <see cref="StellarDirective"/> retires them
/// (<see cref="RetiredMarkdownFieldAttribute"/>), so any rewrite of a note drops them; this re-emits, from the database
/// state, every directive note that still carries one, so the vault does not wait for each directive's next edit. A
/// directive whose note is missing is left alone rather than given a new note.
/// </remarks>
public sealed class DirectiveStartEndRetirementMigration(
	PlainfraContext context,
	PlaintorchMarkdownStorageService storageService,
	MarkdownFrontMatterSerializer markdownSerializer,
	VaultLayout layout,
	VaultAuditLogService auditLogService,
	ILogger<DirectiveStartEndRetirementMigration> logger) : IVaultMigration
{
	/// <inheritdoc />
	public int FromVersion => 4;

	/// <inheritdoc />
	public int ToVersion => 5;

	/// <inheritdoc />
	public string Id => "20260930_0001_DirectiveStartEndRetirement";

	/// <inheritdoc />
	public string Description => "Drop the retired startDate/endDate frontmatter keys from stellar directive notes.";

	/// <inheritdoc />
	public async Task<VaultMigrationOutcome> ApplyAsync(CancellationToken cancellationToken = default)
	{
		var retiredKeys = MarkdownFrontMatterSerializer.GetRetiredFieldNames(typeof(StellarDirective));
		var directives = await context.Directives
			.OfType<StellarDirective>()
			.IgnoreAutoIncludes()
			.ToListAsync(cancellationToken);
		var rewritten = 0;

		foreach (var directive in directives)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var relativePath = await storageService.TryResolveEntityRelativePathAsync(directive, cancellationToken);
			if (relativePath is null)
			{
				continue;
			}

			var markdown = await File.ReadAllTextAsync(Path.Combine(layout.VaultRoot, relativePath), cancellationToken);
			var carried = markdownSerializer.ParseFrontMatter(markdown).Keys.Where(retiredKeys.Contains).ToList();
			if (carried.Count == 0)
			{
				continue;
			}

			await storageService.SaveEntityAsync(directive, cancellationToken: cancellationToken);
			rewritten++;

			await auditLogService.WriteAsync(
				"migration",
				"directive-start-end-retirement",
				subjectType: nameof(StellarDirective),
				subjectId: directive.Id,
				subjectTitle: directive.Title,
				details: new { migration = Id, FromVersion, ToVersion, dropped = carried },
				cancellationToken: cancellationToken);
		}

		logger.LogInformation(
			"Vault migration {Migration} dropped the retired start/end keys from {Count} directive note(s).",
			Id,
			rewritten);

		return new VaultMigrationOutcome(directives.Count, rewritten, 0, 0, 0);
	}
}
