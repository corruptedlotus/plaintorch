using Pleiades.Vault.Watcher;

namespace Pleiades.Vault.Policy;

/// <summary>
/// Delegates watcher path resolution and model belonging checks to storage-mode policy services.
/// </summary>
public sealed class VaultStoragePolicyEngine(
	VaultPathSyncModelCatalog modelCatalog,
	VaultStorageModePolicyRouter policyRouter)
{
	/// <summary>
	/// Resolves a watcher path to an inspectable markdown path and model via mode-specific policies.
	/// </summary>
	public bool TryResolveWatchPath(string path, out string? markdownPath, out VaultPathSyncModel? model)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		var fullPath = Path.GetFullPath(path);
		var isDirectoryEvent = Directory.Exists(fullPath);

		foreach (var candidate in modelCatalog.GetModels()
			.OrderByDescending(entry => entry.ScanRoots.Max(root => root.Length)))
		{
			var policy = policyRouter.Resolve(candidate.Mode);
			if (!policy.TryResolveWatchPath(candidate, fullPath, isDirectoryEvent, out markdownPath)
				|| string.IsNullOrWhiteSpace(markdownPath))
			{
				continue;
			}

			model = candidate;
			markdownPath = Path.GetFullPath(markdownPath);
			return true;
		}

		markdownPath = null;
		model = null;
		return false;
	}

	/// <summary>
	/// Enumerates candidate markdown paths by forwarding root/path checks to mode-specific policies.
	/// </summary>
	public IReadOnlyList<string> EnumerateCandidateMarkdownPaths()
	{
		var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var model in modelCatalog.GetModels())
		{
			var policy = policyRouter.Resolve(model.Mode);
			foreach (var root in model.ScanRoots.Where(Directory.Exists))
			{
				foreach (var markdown in Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories))
				{
					if (policy.TryResolveWatchPath(model, markdown, isDirectoryEvent: false, out var resolved)
						&& !string.IsNullOrWhiteSpace(resolved))
					{
						results.Add(Path.GetFullPath(resolved));
					}
				}
			}
		}

		return results.ToList();
	}

	/// <summary>
	/// Determines whether a path+markdown candidate belongs to the given model under mode-specific rules.
	/// </summary>
	public Task<bool> BelongsToModelAsync(VaultPathSyncModel model, string fullPath, string markdown, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(model);
		var policy = policyRouter.Resolve(model.Mode);
		return policy.BelongsToModelAsync(model, fullPath, markdown, cancellationToken);
	}
}
