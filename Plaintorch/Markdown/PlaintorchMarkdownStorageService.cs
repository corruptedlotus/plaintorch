using Pleiades.Orchestration;
using Pleiades.Vault;
using Pleiades.Vault.Database;
using Pleiades.Vault.Markdown;
using Pleiades.Vault.Watcher;
using Microsoft.EntityFrameworkCore;

namespace Pleiades.Plaintorch.Markdown;

/// <summary>
/// Synchronizes vault-backed markdown files for application API actions while preserving existing body content when possible.
/// </summary>
public sealed class PlaintorchMarkdownStorageService(
	PlainfraContext context,
	VaultLayout layout,
	MarkdownFrontMatterSerializer markdownSerializer,
	MarkdownFileLocator markdownFileLocator,
	VaultTemporalDataService temporalDataService,
	VaultWatcherWriteBarrier writeBarrier)
{
	/// <summary>
	/// Writes the canonical markdown file for a directive.
	/// </summary>
	public async Task SaveDirectiveAsync(Directive directive, Directive? previous = null, CancellationToken cancellationToken = default)
	{
		var previousParent = previous is null ? null : await LoadDirectiveHierarchyAsync(previous.ParentDirectiveId, cancellationToken);
		var currentParent = await LoadDirectiveHierarchyAsync(directive.ParentDirectiveId, cancellationToken);
		var previousPath = previous is null ? null : markdownFileLocator.GetDirectiveFilePath(previous, previousParent);
		var newPath = markdownFileLocator.GetDirectiveFilePath(directive, currentParent);
		var body = await ResolveBodyAsync(previousPath, newPath, $"# {directive.Title}", cancellationToken);
		await WriteMarkdownAsync(newPath, markdownSerializer.Serialize(directive, body), cancellationToken);
		writeBarrier.Suppress(newPath);
		if (!string.IsNullOrWhiteSpace(previousPath))
		{
			writeBarrier.Suppress(previousPath);
		}

		DeleteOldPath(previousPath, newPath, layout.DirectivesRoot);
	}

	/// <summary>
	/// Deletes a directive markdown file.
	/// </summary>
	public Task<FileGraveyardEntry?> DeleteDirectiveAsync(Directive directive, CancellationToken cancellationToken = default)
	{
		return DeleteDirectiveInternalAsync(directive, cancellationToken);
	}

	/// <summary>
	/// Writes the canonical markdown file for an objective.
	/// </summary>
	public async Task SaveObjectiveAsync(Objective objective, Objective? previous = null, CancellationToken cancellationToken = default)
	{
		var previousDirective = previous is null ? null : await LoadDirectiveHierarchyAsync(previous.DirectiveId, cancellationToken);
		var currentDirective = await LoadDirectiveHierarchyAsync(objective.DirectiveId, cancellationToken);
		var previousPath = previous is null ? null : markdownFileLocator.GetObjectiveFilePath(previous, previousDirective);
		var newPath = markdownFileLocator.GetObjectiveFilePath(objective, currentDirective);
		var body = await ResolveBodyAsync(previousPath, newPath, $"# {objective.Title}", cancellationToken);
		await WriteMarkdownAsync(newPath, markdownSerializer.Serialize(objective, body), cancellationToken);
		writeBarrier.Suppress(newPath);
		if (!string.IsNullOrWhiteSpace(previousPath))
		{
			writeBarrier.Suppress(previousPath);
		}

		DeleteOldPath(previousPath, newPath, previousDirective is null ? layout.ObjectivesRoot : markdownFileLocator.GetDirectiveDirectoryPath(previousDirective, previousDirective.ParentDirective));
	}

	/// <summary>
	/// Deletes an objective markdown file.
	/// </summary>
	public Task<FileGraveyardEntry?> DeleteObjectiveAsync(Objective objective, CancellationToken cancellationToken = default)
	{
		return DeleteObjectiveInternalAsync(objective, cancellationToken);
	}

	/// <summary>
	/// Writes the canonical markdown file for an onrush sprint.
	/// </summary>
	public async Task SaveOnrushSprintAsync(OnrushSprint sprint, OnrushSprint? previous = null, CancellationToken cancellationToken = default)
	{
		var previousPath = previous is null ? null : markdownFileLocator.GetOnrushSprintFilePath(previous);
		var newPath = markdownFileLocator.GetOnrushSprintFilePath(sprint);
		var body = await ResolveBodyAsync(previousPath, newPath, $"# {sprint.Title}", cancellationToken);
		await WriteMarkdownAsync(newPath, markdownSerializer.Serialize(sprint, body), cancellationToken);
		writeBarrier.Suppress(newPath);
		if (!string.IsNullOrWhiteSpace(previousPath))
		{
			writeBarrier.Suppress(previousPath);
		}

		DeleteOldPath(previousPath, newPath, layout.OnrushRoot);
	}

	/// <summary>
	/// Deletes an onrush sprint markdown file.
	/// </summary>
	public Task<FileGraveyardEntry?> DeleteOnrushSprintAsync(OnrushSprint sprint, CancellationToken cancellationToken = default)
	{
		return DeletePathAsync(markdownFileLocator.GetOnrushSprintFilePath(sprint), layout.OnrushRoot, sprint.GetType().Name, sprint.Id, sprint.Title, "api-delete", cancellationToken);
	}

	/// <summary>
	/// Writes the canonical markdown file for a Polaris cycle.
	/// </summary>
	public async Task SavePolarisCycleAsync(PolarisCycle cycle, PolarisCycle? previous = null, string? defaultBody = null, CancellationToken cancellationToken = default)
	{
		var previousPath = previous is null ? null : markdownFileLocator.GetPolarisCycleFilePath(previous);
		var newPath = markdownFileLocator.GetPolarisCycleFilePath(cycle);
		var body = await ResolveBodyAsync(previousPath, newPath, defaultBody ?? $"# {cycle.Title}", cancellationToken);
		await WriteMarkdownAsync(newPath, markdownSerializer.Serialize(cycle, body), cancellationToken);
		writeBarrier.Suppress(newPath);
		if (!string.IsNullOrWhiteSpace(previousPath))
		{
			writeBarrier.Suppress(previousPath);
		}

		DeleteOldPath(previousPath, newPath, layout.JournalRoot);
	}

	/// <summary>
	/// Deletes a Polaris cycle markdown file.
	/// </summary>
	public Task<FileGraveyardEntry?> DeletePolarisCycleAsync(PolarisCycle cycle, CancellationToken cancellationToken = default)
	{
		return DeletePathAsync(markdownFileLocator.GetPolarisCycleFilePath(cycle), layout.JournalRoot, cycle.GetType().Name, cycle.Id, cycle.Title, "api-delete", cancellationToken);
	}

	private async Task<FileGraveyardEntry?> DeleteDirectiveInternalAsync(Directive directive, CancellationToken cancellationToken)
	{
		var parentDirective = await LoadDirectiveHierarchyAsync(directive.ParentDirectiveId, cancellationToken);
		return await DeletePathAsync(markdownFileLocator.GetDirectiveFilePath(directive, parentDirective), layout.DirectivesRoot, directive.GetType().Name, directive.Id, directive.Title, "api-delete", cancellationToken);
	}

	private async Task<FileGraveyardEntry?> DeleteObjectiveInternalAsync(Objective objective, CancellationToken cancellationToken)
	{
		var directive = await LoadDirectiveHierarchyAsync(objective.DirectiveId, cancellationToken);
		var rootPath = directive is null ? layout.ObjectivesRoot : markdownFileLocator.GetDirectiveDirectoryPath(directive, directive.ParentDirective);
		return await DeletePathAsync(markdownFileLocator.GetObjectiveFilePath(objective, directive), rootPath, objective.GetType().Name, objective.Id, objective.Title, "api-delete", cancellationToken);
	}

	private static async Task WriteMarkdownAsync(string path, string markdown, CancellationToken cancellationToken)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		await File.WriteAllTextAsync(path, markdown, cancellationToken);
	}

	private static async Task<string> ResolveBodyAsync(string? previousPath, string currentPath, string fallbackBody, CancellationToken cancellationToken)
	{
		var candidatePath = previousPath is not null && File.Exists(previousPath)
			? previousPath
			: File.Exists(currentPath)
				? currentPath
				: null;

		if (candidatePath is null)
		{
			return fallbackBody;
		}

		var markdown = await File.ReadAllTextAsync(candidatePath, cancellationToken);
		var body = ExtractBody(markdown);
		return string.IsNullOrWhiteSpace(body) ? fallbackBody : body;
	}

	private static string ExtractBody(string markdown)
	{
		if (!markdown.StartsWith("---", StringComparison.Ordinal))
		{
			return markdown;
		}

		using var reader = new StringReader(markdown);
		reader.ReadLine();
		while (reader.ReadLine() is { } line)
		{
			if (line == "---")
			{
				break;
			}
		}

		return reader.ReadToEnd().TrimStart('\r', '\n');
	}

	private static void DeleteOldPath(string? previousPath, string currentPath, string rootPath)
	{
		if (string.IsNullOrWhiteSpace(previousPath) || string.Equals(previousPath, currentPath, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		DeletePath(previousPath, rootPath);
	}

	private async Task<FileGraveyardEntry?> DeletePathAsync(
		string path,
		string rootPath,
		string? entityType,
		string? entityId,
		string? entityTitle,
		string reason,
		CancellationToken cancellationToken)
	{
		var archiveTarget = ResolveArchiveTarget(path);
		if (archiveTarget is null)
		{
			DeletePath(path, rootPath);
			return null;
		}

		var resolvedArchiveTarget = archiveTarget.Value;

		var cleanupRoot = resolvedArchiveTarget.IsDirectory
			? Directory.GetParent(resolvedArchiveTarget.Path)?.FullName
			: Path.GetDirectoryName(resolvedArchiveTarget.Path);

		var entry = await temporalDataService.ArchivePathAsync(
			resolvedArchiveTarget.Path,
			reason,
			entityType,
			entityId,
			entityTitle,
			archivedBy: Environment.UserName,
			cancellationToken);

		writeBarrier.Suppress(resolvedArchiveTarget.Path);

		PruneEmptyDirectories(cleanupRoot, rootPath);
		return entry;
	}

	private static (string Path, bool IsDirectory)? ResolveArchiveTarget(string path)
	{
		if (File.Exists(path))
		{
			var directory = Path.GetDirectoryName(path)!;
			var selfNamedDirectory = string.Equals(Path.GetFileNameWithoutExtension(path), Path.GetFileName(directory), StringComparison.OrdinalIgnoreCase);
			if (selfNamedDirectory && Directory.Exists(directory))
			{
				return (directory, true);
			}

			return (path, false);
		}

		return Directory.Exists(path)
			? (path, true)
			: null;
	}

	private static void DeletePath(string path, string rootPath)
	{
		if (File.Exists(path))
		{
			File.Delete(path);
		}

		PruneEmptyDirectories(Path.GetDirectoryName(path), rootPath);
	}

	private static void PruneEmptyDirectories(string? startDirectory, string rootPath)
	{
		var currentDirectory = startDirectory;
		while (!string.IsNullOrWhiteSpace(currentDirectory)
			&& !string.Equals(currentDirectory, rootPath, StringComparison.OrdinalIgnoreCase)
			&& Directory.Exists(currentDirectory)
			&& !Directory.EnumerateFileSystemEntries(currentDirectory).Any())
		{
			var parent = Directory.GetParent(currentDirectory)?.FullName;
			Directory.Delete(currentDirectory);
			currentDirectory = parent;
		}
	}

	private async Task<Directive?> LoadDirectiveHierarchyAsync(string? directiveId, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(directiveId))
		{
			return null;
		}

		var directive = await context.Directives
			.AsNoTracking()
			.FirstOrDefaultAsync(item => item.Id == directiveId, cancellationToken);

		if (directive is null || string.IsNullOrWhiteSpace(directive.ParentDirectiveId))
		{
			return directive;
		}

		directive.ParentDirective = await LoadDirectiveHierarchyAsync(directive.ParentDirectiveId, cancellationToken);
		return directive;
	}
}
