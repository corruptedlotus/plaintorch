using Microsoft.EntityFrameworkCore;
using Pleiades.Orchestration;
using Pleiades.Vault;
using Pleiades.Vault.Database;
using Pleiades.Vault.Markdown;
using Pleiades.Vault.Media;

namespace Pleiades.Plaintorch.Media;

/// <summary>
/// Resolves the self asset folder an entity's own (<c>media:</c>) uploads live in (PEP105). It is the single place
/// that knows how each media-bearing entity composes that folder, so the media domain can store an upload beside
/// any such entity without taking on the entity's storage knowledge itself.
/// </summary>
public sealed class MediaAssetFolderResolver(
	PlainfraContext context,
	MarkdownFileLocator markdownFileLocator,
	VaultMediaService mediaService)
{
	/// <summary>
	/// Resolves the absolute self asset folder for an entity (not guaranteed to exist yet), <see langword="null"/>
	/// when the entity is missing, or throws for a type that keeps no self media.
	/// </summary>
	public async Task<string?> ResolveAsync(string entityType, string entityId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
		ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
		return entityType.Trim().ToLowerInvariant() switch
		{
			"directive" => await ResolveDirectiveFolderAsync(entityId, cancellationToken),
			_ => throw new InvalidOperationException($"Entity type '{entityType}' has no self asset folder for media."),
		};
	}

	/// <summary>
	/// Composes a directive's asset folder from its canonical markdown path. Entity→note association is a known gap
	/// for directives (they are excluded from path-sync scanning), so the folder is composed from the canonical
	/// location — where the API writes directives — rather than a resolved note path.
	/// </summary>
	private async Task<string?> ResolveDirectiveFolderAsync(string directiveId, CancellationToken cancellationToken)
	{
		var directive = await context.Directives.AsNoTracking().FirstOrDefaultAsync(item => item.Id == directiveId, cancellationToken);
		if (directive is null)
		{
			return null;
		}

		// Walk the nesting chain on detached instances so the locator can compose the nested folder path without
		// the change tracker treating the linked ancestors as inserts.
		var current = directive;
		while (!string.IsNullOrWhiteSpace(current.ParentDirectiveId))
		{
			var parent = await context.Directives.AsNoTracking().FirstOrDefaultAsync(item => item.Id == current.ParentDirectiveId, cancellationToken);
			if (parent is null)
			{
				break;
			}

			current.ParentDirective = parent;
			current = parent;
		}

		var markdownPath = markdownFileLocator.GetDirectiveFilePath(directive, directive.ParentDirective);
		return mediaService.GetAssetFolder(markdownPath, VaultStorageShape.SelfNamedDirectory);
	}
}
