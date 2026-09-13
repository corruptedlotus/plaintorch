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

		if (lorePage is not null)
		{
			lorePage.IsActive = (await ActiveIdsAsync(cancellationToken)).Contains(lorePage.Id);
		}

		return lorePage;
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<LorePage>> ListAsync(CancellationToken cancellationToken = default)
	{
		var pages = await context.LorePages
			.AsNoTracking()
			.OrderBy(item => item.Id)
			.ToListAsync(cancellationToken);

		// Stamp the active spine from the single source of truth (LoreIndex) so the client reads "active" from the API
		// rather than re-deriving a divergent definition. Built from the loaded pages — no extra query.
		var activeIds = new LoreIndex(pages).ActivePages.Select(page => page.Id).ToHashSet(StringComparer.Ordinal);
		foreach (var page in pages)
		{
			page.IsActive = activeIds.Contains(page.Id);
		}

		return pages;
	}

	/// <summary>The ids of the pages on the current active lore spine (<see cref="LoreIndex"/>), the one active definition.</summary>
	private async Task<HashSet<string>> ActiveIdsAsync(CancellationToken cancellationToken)
	{
		var index = await context.LorePages.AsNoTracking().ToLoreIndexAsync(cancellationToken);
		return index.ActivePages.Select(page => page.Id).ToHashSet(StringComparer.Ordinal);
	}

	public async Task<IReadOnlyList<LorePage>> ListActiveAsync(CancellationToken cancellationToken = default)
	{
		var index = await context.LorePages
			.AsNoTracking()
			.ToLoreIndexAsync(cancellationToken);
		return index.ActivePages;
	}

	/// <inheritdoc />
	public async Task<LorePage> CreateAsync(LorePageCreateRequest request, CancellationToken cancellationToken = default)
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

		await ValidateBeginningWithinParentAsync(request.Beginning, parent, cancellationToken);
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
		lorePage.IsActive = (await ActiveIdsAsync(cancellationToken)).Contains(lorePage.Id);
		return lorePage;
	}

	/// <inheritdoc />
	public async Task<LorePage?> UpdateAsync(string puck, LorePageUpdate update, CancellationToken cancellationToken = default)
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
			var beginningParent = string.IsNullOrWhiteSpace(lorePage.ParentId)
				? null
				: await context.LorePages.AsNoTracking().FirstOrDefaultAsync(item => item.Id == lorePage.ParentId, cancellationToken);
			await ValidateBeginningWithinParentAsync(update.Beginning.Value, beginningParent, cancellationToken);
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
		lorePage.IsActive = (await ActiveIdsAsync(cancellationToken)).Contains(lorePage.Id);
		return lorePage;
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

	/// <inheritdoc />
	public async Task<LorePage?> SetIndexAsync(string puck, int index, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(puck);
		if (index < 0)
		{
			// Zero is a valid narrative index (a prologue / "Chapter 0"); only negatives are rejected.
			throw new InvalidOperationException("A lore index must be 0 or greater.");
		}

		var page = await context.LorePages.AsNoTracking().FirstOrDefaultAsync(item => item.Id == puck, cancellationToken);
		if (page is null)
		{
			return null;
		}

		if ((OwnIndex(page) ?? 0) == index)
		{
			return page;
		}

		// Renumbering changes the page's terminal PUCK token, so its id — and every descendant's id, which is prefixed
		// by it — changes with it. The parent (and therefore the folder placement) is unchanged; only the leaf number.
		var oldId = page.Id;
		var discriminator = LevelDiscriminator(page.Level);
		var parentPrefix = string.IsNullOrWhiteSpace(page.ParentId) ? string.Empty : page.ParentId + "/";
		var newId = $"{parentPrefix}{discriminator}{index}";
		if (await context.LorePages.AnyAsync(item => item.Id == newId, cancellationToken))
		{
			throw new InvalidOperationException($"A lore page already exists at {page.Level} index {index}.");
		}

		var parent = string.IsNullOrWhiteSpace(page.ParentId)
			? null
			: await context.LorePages.AsNoTracking().FirstOrDefaultAsync(item => item.Id == page.ParentId, cancellationToken);
		var descendants = await context.LorePages.AsNoTracking()
			.Where(item => item.Id.StartsWith(oldId + "/"))
			.ToListAsync(cancellationToken);

		var previous = (LorePage)entityGateway.CloneScalars(page);
		var oldDirectory = Path.GetDirectoryName(previous.RelativePath) ?? string.Empty;

		// Re-key the page itself: new id, own index, new folder path.
		page.Id = newId;
		StampOwnIndex(page, page.Level, index);
		page.RelativePath = ComposeRelativePath(page, parent);
		var newDirectory = Path.GetDirectoryName(page.RelativePath) ?? string.Empty;

		// Re-key each descendant: swap the id/parent prefix, renumber the shared level (descendants inherit it), and
		// rebase its path under the page's new folder — the whole subtree moves with the page's folder on disk.
		var remaps = new List<(string OldId, LorePage Entity)> { (oldId, page) };
		foreach (var descendant in descendants)
		{
			var descendantOldId = descendant.Id;
			descendant.Id = newId + descendantOldId[oldId.Length..];
			if (string.Equals(descendant.ParentId, oldId, StringComparison.Ordinal))
			{
				descendant.ParentId = newId;
			}
			else if (descendant.ParentId is not null && descendant.ParentId.StartsWith(oldId + "/", StringComparison.Ordinal))
			{
				descendant.ParentId = newId + descendant.ParentId[oldId.Length..];
			}

			StampOwnIndex(descendant, page.Level, index);
			if (descendant.RelativePath.StartsWith(oldDirectory, StringComparison.OrdinalIgnoreCase))
			{
				descendant.RelativePath = newDirectory + descendant.RelativePath[oldDirectory.Length..];
			}

			remaps.Add((descendantOldId, descendant));
		}

		// Apply the re-key in one transaction, deferring SQLite FK checks so the intermediate dangling parent
		// references during the sweep are tolerated and only the consistent final state is validated at commit.
		await using (var transaction = await context.Database.BeginTransactionAsync(cancellationToken))
		{
			await context.Database.ExecuteSqlRawAsync("PRAGMA defer_foreign_keys = ON;", cancellationToken);
			foreach (var (rekeyOldId, entity) in remaps)
			{
				await context.LorePages.Where(item => item.Id == rekeyOldId).ExecuteUpdateAsync(setters => setters
					.SetProperty(item => item.Id, entity.Id)
					.SetProperty(item => item.ParentId, entity.ParentId)
					.SetProperty(item => item.Era, entity.Era)
					.SetProperty(item => item.Chapter, entity.Chapter)
					.SetProperty(item => item.Act, entity.Act)
					.SetProperty(item => item.Phase, entity.Phase)
					.SetProperty(item => item.RelativePath, entity.RelativePath), cancellationToken);
			}

			await transaction.CommitAsync(cancellationToken);
		}

		// Move the page's self-named folder (relocating the whole subtree on disk) and rewrite its frontmatter, then
		// rewrite each descendant's frontmatter PUCK at its new, already-moved location.
		await markdownStorageService.SaveLorePageAsync(page, previous, cancellationToken: cancellationToken);
		foreach (var descendant in descendants)
		{
			await markdownStorageService.SaveLorePageAsync(descendant, cancellationToken: cancellationToken);
		}

		await auditLogService.WriteAsync("api", "lore.set-index", subject: page, details: new { previousId = oldId, index }, cancellationToken: cancellationToken);
		page.IsActive = (await ActiveIdsAsync(cancellationToken)).Contains(page.Id);
		return page;
	}

	/// <summary>
	/// Enforces the hierarchical beginning invariant (D17): a child lore page's beginning must fall within its
	/// parent's span — at or after the parent's beginning, and before the parent's own span ends (its next sibling's
	/// beginning). This keeps the lore hierarchy a coherent nested timeline, so an un-begun ancestor can never
	/// contain a begun child.
	/// </summary>
	private async Task ValidateBeginningWithinParentAsync(DateOnly? beginning, LorePage? parent, CancellationToken cancellationToken)
	{
		if (beginning is not { } value || parent is null)
		{
			return;
		}

		if (parent.Beginning is { } parentBeginning && value < parentBeginning)
		{
			throw new InvalidOperationException(
				$"Lore page cannot begin on {value:yyyy-MM-dd}, before its parent '{parent.Id}' begins on {parentBeginning:yyyy-MM-dd}.");
		}

		var parentSiblings = await context.LorePages.AsNoTracking()
			.Where(item => item.ParentId == parent.ParentId && item.Id != parent.Id)
			.ToListAsync(cancellationToken);
		if (LorePage.ResolveEndingExclusive(parent, parentSiblings) is { } parentEnding && value >= parentEnding)
		{
			throw new InvalidOperationException(
				$"Lore page cannot begin on {value:yyyy-MM-dd}, at or after its parent '{parent.Id}' ends on {parentEnding:yyyy-MM-dd}.");
		}
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
		// The numbering policy is not simply per-direct-parent: Era and Chapter numbering are global and never reset,
		// Act numbering resets each Era, and Phase numbering resets each Act. So a new index is the max at that level
		// within the appropriate scope, plus one.
		var pages = context.LorePages.AsNoTracking();
		var parentEra = parent?.Era;
		var parentId = parent?.Id;
		var maxIndex = level.Trim().ToLowerInvariant() switch
		{
			"era" => await pages.Where(item => item.Level == "Era").MaxAsync(item => (int?)item.Era, cancellationToken),
			"cha" => await pages.Where(item => item.Level == "Cha").MaxAsync(item => (int?)item.Chapter, cancellationToken),
			"act" => await pages.Where(item => item.Level == "Act" && item.Era == parentEra).MaxAsync(item => (int?)item.Act, cancellationToken),
			"p" => await pages.Where(item => item.Level == "p" && item.ParentId == parentId).MaxAsync(item => (int?)item.Phase, cancellationToken),
			_ => null,
		};

		return (maxIndex ?? 0) + 1;
	}

	/// <summary>Gets the own-level index a lore page carries (its Era/Chapter/Act/Phase number).</summary>
	private static int? OwnIndex(LorePage lorePage)
	{
		return lorePage.Level.Trim().ToLowerInvariant() switch
		{
			"era" => lorePage.Era,
			"cha" => lorePage.Chapter,
			"act" => lorePage.Act,
			"p" => lorePage.Phase,
			_ => null,
		};
	}

	/// <summary>Gets the PUCK discriminator token for a lore level (Era/Cha/Act/p).</summary>
	private static string LevelDiscriminator(string level)
	{
		return level.Trim().ToLowerInvariant() switch
		{
			"era" => "Era",
			"cha" => "Cha",
			"act" => "Act",
			"p" => "p",
			_ => throw new InvalidOperationException($"Lore level '{level}' has no PUCK discriminator."),
		};
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
}
