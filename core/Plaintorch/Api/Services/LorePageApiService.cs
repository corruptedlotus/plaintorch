using Microsoft.EntityFrameworkCore;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.Markdown;
using Pleiades.Puck;
using Pleiades.Saga;
using Pleiades.Vault;
using Pleiades.Vault.Database;
using Pleiades.Vault.Markdown;

namespace Pleiades.Plaintorch.Api.Services;

/// <summary>
/// Implements the lore page-facing PLAINTORCH application API.
/// </summary>
public sealed class LorePageApiService(
	PlainfraContext context,
	PlaintorchMarkdownStorageService markdownStorageService,
	MarkdownFileLocator fileLocator,
	VaultLayout layout,
	VaultTemporalDataService temporalDataService,
	VaultAuditLogService auditLogService,
	VaultEntityGateway entityGateway) : ILorePageApi
{
	/// <inheritdoc />
	public async Task<LorePage?> GetAsync(string puck, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(puck);
		var lorePage = await context.LorePages
			.Include(x => x.Parent)
			.AsNoTracking()
			.FirstOrDefaultAsync(item => item.Id == puck, cancellationToken);

		return lorePage is null ? null : lorePage;
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<LorePageRecord>> ListAsync(CancellationToken cancellationToken = default)
	{
		var lorePages = await context.LorePages
			.AsNoTracking()
			.OrderBy(item => item.Id)
			.ToListAsync(cancellationToken);
		return lorePages.Select(Map).ToList();
	}

	/// <inheritdoc />
	public async Task<LorePageRecord> CreateAsync(LorePageCreateRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		ArgumentException.ThrowIfNullOrWhiteSpace(request.Title);

		// The level, discriminator, and which narrative index a new page carries are all a function of the parent:
		// no parent creates a top-level Era, and each level below reuses its parent's static child discriminator.
		LorePage? parent = null;
		if (!string.IsNullOrWhiteSpace(request.ParentPuck))
		{
			parent = await context.LorePages.AsNoTracking().FirstOrDefaultAsync(item => item.Id == request.ParentPuck, cancellationToken)
				?? throw new InvalidOperationException($"Parent lore page '{request.ParentPuck}' was not found.");
		}

		var (level, discriminator) = ResolveChildLevel(parent);
		var nextIndex = await ResolveNextSiblingIndexAsync(parent, level, cancellationToken);
		var id = parent is null ? $"{discriminator}{nextIndex}" : $"{parent.Id}/{discriminator}{nextIndex}";

		var lorePage = new LorePage
		{
			Id = id,
			Title = request.Title.Trim(),
			Beginning = request.Beginning,
			ParentId = parent?.Id,
			Level = level,
			// Narrative indices are inherited from the parent's chain, then the new level's own index is stamped.
			Era = parent?.Era,
			Chapter = parent?.Chapter,
			Act = parent?.Act,
			Phase = parent?.Phase,
			IndexedUtc = DateTimeOffset.UtcNow,
		};

		StampOwnIndex(lorePage, level, nextIndex);
		// Lore pages are stored as nested self-named directories, and the path locator reads the leaf placement from
		// RelativePath (rooting it when absent). Composing it here nests the new folder inside its parent's folder so
		// the file is materialized in the right place rather than at the saga root.
		lorePage.RelativePath = ComposeRelativePath(lorePage, parent);

		context.LorePages.Add(lorePage);
		await context.SaveChangesAsync(cancellationToken);
		await markdownStorageService.SaveLorePageAsync(lorePage, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync("api", "lore.create", subject: lorePage, details: new { parent = parent?.Id, level }, cancellationToken: cancellationToken);
		return Map(lorePage);
	}

	/// <inheritdoc />
	public async Task<LorePageRecord?> UpdateAsync(string puck, LorePageUpdate update, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(puck);
		ArgumentNullException.ThrowIfNull(update);

		var lorePage = await context.LorePages.FirstOrDefaultAsync(item => item.Id == puck, cancellationToken);
		if (lorePage is null)
		{
			return null;
		}

		var previous = (LorePage)entityGateway.CloneScalars(lorePage);
		var titleChanged = false;
		if (update.Title.IsSet && !string.IsNullOrWhiteSpace(update.Title.Value) && !string.Equals(update.Title.Value, lorePage.Title, StringComparison.Ordinal))
		{
			lorePage.Title = update.Title.Value.Trim();
			titleChanged = true;
		}

		if (update.Beginning.IsSet)
		{
			lorePage.Beginning = update.Beginning.Value;
		}

		if (titleChanged)
		{
			// A lore page's folder is named "{token} - {title}"; renaming it moves the self-named directory. Recomposing
			// RelativePath from the new title is what lets the save path see a different target and relocate the folder.
			var parent = string.IsNullOrWhiteSpace(lorePage.ParentId)
				? null
				: await context.LorePages.AsNoTracking().FirstOrDefaultAsync(item => item.Id == lorePage.ParentId, cancellationToken);
			lorePage.RelativePath = ComposeRelativePath(lorePage, parent);
		}

		await context.SaveChangesAsync(cancellationToken);
		// Lore is synced, so the change is written back through the entity's own markdown file. Passing the previous
		// snapshot lets the storage layer relocate the self-named directory when the title (and therefore folder) changed.
		await markdownStorageService.SaveLorePageAsync(lorePage, previous, cancellationToken: cancellationToken);
		await auditLogService.WriteAsync("api", "lore.update", subject: lorePage, details: new { previousTitle = previous.Title }, cancellationToken: cancellationToken);
		return Map(lorePage);
	}

	/// <inheritdoc />
	public async Task<bool> DeleteAsync(string puck, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(puck);

		var lorePage = await context.LorePages.FirstOrDefaultAsync(item => item.Id == puck, cancellationToken);
		if (lorePage is null)
		{
			return false;
		}

		var hasChildren = await context.LorePages.AnyAsync(item => item.ParentId == puck, cancellationToken);
		if (hasChildren)
		{
			throw new InvalidOperationException("Lore page cannot be deleted while it still has child lore pages.");
		}

		var snapshot = (LorePage)entityGateway.CloneScalars(lorePage);
		var databaseGraveyard = await temporalDataService.ArchiveEntityAsync(snapshot, "api-delete", Environment.UserName, cancellationToken);
		context.LorePages.Remove(lorePage);
		await context.SaveChangesAsync(cancellationToken);
		var fileGraveyard = await markdownStorageService.DeleteLorePageAsync(snapshot, cancellationToken);
		await auditLogService.WriteAsync(
			"api",
			"lore.delete",
			subjectType: nameof(LorePage),
			subjectId: snapshot.Id,
			subjectTitle: snapshot.Title,
			temporalKind: "database",
			temporalEntryKey: databaseGraveyard.EntryKey,
			temporalEntityType: databaseGraveyard.EntityType,
			temporalEntityId: databaseGraveyard.EntityId,
			temporalEntityTitle: databaseGraveyard.EntityTitle,
			temporalLocation: fileGraveyard?.ArchivedRelativePath,
			details: new { fileTemporalEntryKey = fileGraveyard?.EntryKey },
			cancellationToken: cancellationToken);
		return true;
	}

	private static (string Level, string Discriminator) ResolveChildLevel(LorePage? parent)
	{
		if (parent is null)
		{
			return ("Era", "Era");
		}

		return parent.Level.Trim().ToLowerInvariant() switch
		{
			"era" => ("Cha", "Cha"),
			"cha" => ("Act", "Act"),
			"act" => ("p", "p"),
			"p" => throw new InvalidOperationException("A Phase is the lowest lore level and cannot contain child lore pages."),
			_ => throw new InvalidOperationException($"Lore page '{parent.Id}' has an unrecognized level '{parent.Level}' and cannot receive children."),
		};
	}

	private async Task<int> ResolveNextSiblingIndexAsync(LorePage? parent, string level, CancellationToken cancellationToken)
	{
		var parentId = parent?.Id;
		var siblings = context.LorePages.AsNoTracking().Where(item => item.ParentId == parentId);
		var maxIndex = level.Trim().ToLowerInvariant() switch
		{
			"era" => await siblings.MaxAsync(item => (int?)item.Era, cancellationToken),
			"cha" => await siblings.MaxAsync(item => (int?)item.Chapter, cancellationToken),
			"act" => await siblings.MaxAsync(item => (int?)item.Act, cancellationToken),
			"p" => await siblings.MaxAsync(item => (int?)item.Phase, cancellationToken),
			_ => null,
		};

		return (maxIndex ?? 0) + 1;
	}

	private static void StampOwnIndex(LorePage lorePage, string level, int index)
	{
		switch (level.Trim().ToLowerInvariant())
		{
			case "era":
				lorePage.Era = index;
				break;
			case "cha":
				lorePage.Chapter = index;
				break;
			case "act":
				lorePage.Act = index;
				break;
			case "p":
				lorePage.Phase = index;
				break;
		}
	}

	private string ComposeRelativePath(LorePage lorePage, LorePage? parent)
	{
		var folderName = PuckNamedIdentity.FormatFileName(lorePage.EffectiveIdentifier, lorePage.Title);
		string absolutePath;
		if (parent is null)
		{
			absolutePath = Path.Combine(layout.SagaRoot, folderName, $"{folderName}.md");
		}
		else
		{
			var parentPath = fileLocator.GetLorePageFilePath(parent);
			var parentDirectory = Path.GetDirectoryName(parentPath)
				?? throw new InvalidOperationException($"Lore parent '{parent.Id}' canonical path does not have a valid directory.");
			absolutePath = Path.Combine(parentDirectory, folderName, $"{folderName}.md");
		}

		return Path.GetRelativePath(layout.VaultRoot, absolutePath);
	}

	private static LorePageRecord Map(LorePage lorePage)
	{
		return new LorePageRecord(
			lorePage.Id,
			lorePage.Title,
			lorePage.OverrideIdentifier,
			lorePage.ParentId,
			lorePage.Beginning,
			lorePage.Level,
			lorePage.RelativePath,
			lorePage.Era,
			lorePage.Chapter,
			lorePage.Act,
			lorePage.Phase,
			lorePage.IndexedUtc);
	}
}
