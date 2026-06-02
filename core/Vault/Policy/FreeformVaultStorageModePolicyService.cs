using Pleiades.Orchestration;
using Pleiades.Puck;
using Pleiades.Vault.Markdown;
using Pleiades.Vault.Watcher;

namespace Pleiades.Vault.Policy;

/// <summary>
/// Implements freeform storage policy where ownership is identity-driven (frontmatter PUCK) rather than path-shape matching.
/// </summary>
public sealed class FreeformVaultStorageModePolicyService(
	VaultLayout layout,
	MarkdownFrontMatterSerializer markdownSerializer,
	PuckEntityResolutionService puckEntityResolutionService) : IVaultStorageModePolicyService
{
	/// <inheritdoc />
	public VaultStorageMode Mode => VaultStorageMode.Freeform;

	/// <inheritdoc />
	public bool TryResolveWatchPath(VaultPathSyncModel model, string fullPath, bool isDirectoryEvent, out string? inspectPath)
	{
		if (isDirectoryEvent || !string.Equals(Path.GetExtension(fullPath), ".md", StringComparison.OrdinalIgnoreCase))
		{
			inspectPath = null;
			return false;
		}

		var normalizedRoot = Path.GetFullPath(layout.VaultRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
		if (!Path.GetFullPath(fullPath).StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
		{
			inspectPath = null;
			return false;
		}

		inspectPath = fullPath;
		return true;
	}

	/// <inheritdoc />
	public async Task<bool> BelongsToModelAsync(VaultPathSyncModel model, string fullPath, string markdown, CancellationToken cancellationToken)
	{
		if (!string.Equals(Path.GetExtension(fullPath), ".md", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		if (!File.Exists(fullPath))
		{
			return true;
		}

		var frontMatter = markdownSerializer.ParseFrontMatter(markdown);
		if (!frontMatter.TryGetValue("puck", out var rawPuck) || string.IsNullOrWhiteSpace(rawPuck))
		{
			return false;
		}

		var puck = rawPuck.Trim().Trim('"');
		if (string.IsNullOrWhiteSpace(puck))
		{
			return false;
		}

		var resolved = await puckEntityResolutionService.ResolveAsync(puck, cancellationToken);
		if (resolved.Exists)
		{
			return string.Equals(resolved.EntityType, model.EntityName, StringComparison.Ordinal);
		}

		return true;
	}

	/// <inheritdoc />
	public (VaultSyncAction Action, string Reason) Decide(VaultStorageModeDecisionContext context)
	{
		if (string.IsNullOrWhiteSpace(context.PathId))
		{
			return (VaultSyncAction.Ignore, "Freeform storage does not auto-create entities from files without frontmatter PUCK identity.");
		}

		var exists = context.KnownIds.Contains(context.PathId);
		if (!context.FileExists)
		{
			if (exists)
			{
				return (VaultSyncAction.DeleteFromDatabase, "Freeform storage removes known entities when their asserted file is deleted.");
			}

			return (VaultSyncAction.Ignore, "Missing freeform file does not map to a known PUCK identity.");
		}

		if (context.IssueMessages.Count > 0)
		{
			if (!exists)
			{
				return (VaultSyncAction.PurgeFile, "Unknown freeform PUCK assertion with validation issues is disallowed by freeform policy.");
			}

			return (VaultSyncAction.RewriteFromDatabase, "Freeform candidate has validation issues and must be rewritten from canonical state.");
		}

		if (exists)
		{
			return (VaultSyncAction.UpdateFromFile, "Frontmatter PUCK identity exists in storage and can be synced from file.");
		}

		return (VaultSyncAction.PurgeFile, "Freeform storage rejects unknown frontmatter PUCK assertions.");
	}

	/// <inheritdoc />
	public string? ResolveRelocationOldIdFallback(VaultPathSyncModel model, string? oldPathId, string? newPathId)
	{
		if (!string.IsNullOrWhiteSpace(oldPathId))
		{
			return oldPathId;
		}

		return model.EntityType == typeof(Directive)
			? newPathId
			: oldPathId;
	}
}
