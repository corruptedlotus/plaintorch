using Microsoft.EntityFrameworkCore;
using Pleiades.Puck;
using Pleiades.Saga;
using Pleiades.Vault;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.Markdown;

/// <summary>
/// Save hook for lore pages: when a page is reassigned to a different parent, its self-named directory must re-home
/// under the new parent's directory (a vertical move is rejected — the static nesting pattern does not support it) and
/// its stored relative path updated. Extracted from the central storage service so lore's per-type placement rule
/// lives with lore rather than as a special case in the generic save.
/// </summary>
/// <remarks>
/// LATENT: this reassignment path is not currently reachable through the API — <c>LorePageApiService.UpdateAsync</c>
/// does not change <c>ParentId</c>, and <c>SetIndexAsync</c> re-keys ids without passing a <c>previous</c> snapshot —
/// so the hook fires only if a future reparent operation saves a lore page with a changed parent. The logic is
/// preserved and correct, just not yet wired to a caller.
/// </remarks>
public sealed class LorePageSaveHook(PlainfraContext context, VaultLayout layout) : EntitySaveHook<LorePage>
{
	/// <inheritdoc />
	protected override async Task<string?> ResolveWritePathAsync(LorePage entity, LorePage? previous, EntitySaveContext context, CancellationToken cancellationToken)
	{
		if (previous is null || string.Equals(entity.ParentId, previous.ParentId, StringComparison.OrdinalIgnoreCase))
		{
			// Not a parent reassignment — the default canonical path already places the page correctly.
			return null;
		}

		var newPath = await ResolveReassignmentPathAsync(entity, context, cancellationToken);
		entity.RelativePath = Path.GetRelativePath(layout.VaultRoot, newPath);
		return newPath;
	}

	private async Task<string> ResolveReassignmentPathAsync(LorePage lorePage, EntitySaveContext saveContext, CancellationToken cancellationToken)
	{
		var folderName = PuckNamedIdentity.FormatFileName(lorePage.EffectiveIdentifier, lorePage.Title);
		if (string.IsNullOrWhiteSpace(lorePage.ParentId))
		{
			if (string.Equals(lorePage.Level, "Cha", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(lorePage.Level, "Act", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(lorePage.Level, "p", StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException($"Lore page '{lorePage.Id}' cannot be reassigned vertically without a valid parent for level '{lorePage.Level}'.");
			}

			return Path.Combine(layout.SagaRoot, folderName, $"{folderName}.md");
		}

		var parent = await context.LorePages
			.AsNoTracking()
			.FirstOrDefaultAsync(item => item.Id == lorePage.ParentId, cancellationToken)
			?? throw new InvalidOperationException($"Lore parent '{lorePage.ParentId}' was not found during reassignment sync.");

		ValidateReassignment(lorePage, parent);

		var parentPath = await saveContext.ResolveCanonicalPathAsync(parent, cancellationToken);
		var parentDirectory = Path.GetDirectoryName(parentPath)
			?? throw new InvalidOperationException($"Lore parent '{parent.Id}' canonical path does not have a valid directory.");

		return Path.Combine(parentDirectory, folderName, $"{folderName}.md");
	}

	private static void ValidateReassignment(LorePage lorePage, LorePage parent)
	{
		var expectedParentLevel = lorePage.Level.Trim().ToLowerInvariant() switch
		{
			"era" => null,
			"cha" => "Era",
			"act" => "Cha",
			"p" => "Act",
			_ => null,
		};

		if (expectedParentLevel is null)
		{
			if (string.Equals(lorePage.Level, "era", StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException($"Lore page '{lorePage.Id}' is level '{lorePage.Level}' and cannot be reassigned under parent '{parent.Id}'.");
			}

			return;
		}

		if (!string.Equals(parent.Level, expectedParentLevel, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException(
				$"Lore reassignment from '{lorePage.Id}' to parent '{parent.Id}' is vertical and not supported by the static nesting pattern. Expected parent level '{expectedParentLevel}', but found '{parent.Level}'.");
		}
	}
}
