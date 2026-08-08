using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.State;
using Microsoft.EntityFrameworkCore;
using Pleiades.Calendar;
using Pleiades.Orchestration;
using Pleiades.Puck;
using Pleiades.Saga;
using Pleiades.Vault;
using Pleiades.Vault.Database;
using Pleiades.Vault.Markdown;
using Pleiades.Vault.Watcher;

namespace Pleiades.Plaintorch.Api.Services;

/// <summary>
/// Implements the system-facing PLAINTORCH application API.
/// </summary>
public sealed class SystemApiService(
	PlaintorchStateService stateService,
	PlainfraContext context,
	VaultLayout layout,
	VaultPathSyncModelCatalog pathSyncModelCatalog,
	VaultWatcherIssueRegistry watcherIssueRegistry,
	PuckEntityResolutionService puckEntityResolutionService,
	MarkdownFrontMatterSerializer markdownSerializer,
	ILogger<SystemApiService> logger) : ISystemApi
{
	/// <inheritdoc />
	public async Task<SystemBriefing> GetBriefingAsync(CancellationToken cancellationToken = default)
	{
		var activeOnrush = await stateService.GetActiveOnrushSprintAsync(cancellationToken);
		var onrushSelectionMode = activeOnrush is not null ? "active" : "planning";
		var briefingOnrush = activeOnrush is not null
			? await LoadOnrushSprintAsync(activeOnrush.Id, cancellationToken)
			: await LoadPlanningOnrushSprintAsync(cancellationToken);
		if (briefingOnrush is null)
		{
			onrushSelectionMode = null;
		}

		var activePolaris = await stateService.GetActivePolarisCycleAsync(cancellationToken);
		var briefingPolaris = activePolaris is null
			? null
			: await LoadPolarisCycleAsync(activePolaris.Id, cancellationToken);

		var activeLorePages = await LoadActiveLorePagesAsync(cancellationToken);
		var watcherStatus = watcherIssueRegistry.GetStatus().ToString().ToLowerInvariant();
		var watcherIssues = watcherIssueRegistry.GetIssues();
		var watcherCriteria = watcherIssueRegistry.GetCriterionSummary();

		return new SystemBriefing(
			"ok",
			DateTimeOffset.UtcNow,
			stateService.GetActiveVaultPath(),
			PleiadeanCalendar.FromDateTime(DateTime.Today).ToString(),
			await stateService.GetCelestronBankedAsync(cancellationToken),
			watcherStatus,
			watcherIssues.Count,
			watcherIssues.Count(static issue => issue.IsCritical),
			watcherCriteria.Total,
			watcherCriteria.Failed,
			onrushSelectionMode,
			briefingOnrush,
			briefingPolaris,
			activeLorePages);
	}

	/// <inheritdoc />
	public async Task<EntityExistence> ResolveVaultNoteAsync(string vaultRelativePath, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(vaultRelativePath);
		var normalizedRelativePath = vaultRelativePath
			.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
			.TrimStart(Path.DirectorySeparatorChar);
		var absolutePath = Path.GetFullPath(Path.Combine(layout.VaultRoot, normalizedRelativePath));
		var normalizedRoot = layout.VaultRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

		if (!absolutePath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("Resolved note path escaped the active vault root.");
		}

		if (!pathSyncModelCatalog.TryResolve(absolutePath, out var model) || model is null)
		{
			var freeformResolution = await ResolveByFrontMatterPuckAsync(absolutePath, cancellationToken);
			return freeformResolution ?? new EntityExistence(normalizedRelativePath, false);
		}

		var entityKind = PuckEntityAttribute.ResolveKind(model.EntityType);
		if (entityKind is null)
		{
			return new EntityExistence(normalizedRelativePath, false);
		}

		var (pathPuck, pathTitle) = MarkdownFileLocator.ParseLoosePuckIdentityFromPath(absolutePath);
		var (resolvedPuck, _) = await ResolveEntityIdentityAsync(model.EntityType, absolutePath, pathPuck, pathTitle, cancellationToken, logger);

		if (string.IsNullOrWhiteSpace(resolvedPuck))
		{
			// The note is a recognized PLAINTORCH entity kind by path shape, but its identity is not yet
			// resolvable to a stored entity (for example, an implicit note not yet synced to the database).
			return new EntityExistence(string.Empty, true, model.EntityType.Name, entityKind, null, normalizedRelativePath);
		}

		return await ResolveEntityByPuckAsync(resolvedPuck, cancellationToken);
	}

	private async Task<EntityExistence?> ResolveByFrontMatterPuckAsync(
		string absolutePath,
		CancellationToken cancellationToken)
	{
		if (!File.Exists(absolutePath)
			|| !string.Equals(Path.GetExtension(absolutePath), ".md", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		var markdown = await File.ReadAllTextAsync(absolutePath, cancellationToken);
		var frontMatter = markdownSerializer.ParseFrontMatter(markdown);
		if (!frontMatter.TryGetValue("puck", out var rawPuck)
			|| string.IsNullOrWhiteSpace(rawPuck))
		{
			return null;
		}

		var puck = NormalizeFrontMatterPuck(rawPuck);
		if (string.IsNullOrWhiteSpace(puck))
		{
			return null;
		}

		var resolved = await puckEntityResolutionService.ResolveAsync(puck, cancellationToken);
		if (!resolved.Exists || string.IsNullOrWhiteSpace(resolved.EntityKind))
		{
			return null;
		}

		return ToEntityExistence(resolved);
	}

	private static string NormalizeFrontMatterPuck(string rawPuck)
	{
		return rawPuck.Trim().Trim('"');
	}

	/// <inheritdoc />
	public async Task<EntityExistence> ResolveEntityByPuckAsync(string id, CancellationToken cancellationToken = default)
	{
		var resolved = await puckEntityResolutionService.ResolveAsync(id, cancellationToken);
		return ToEntityExistence(resolved);
	}

	private static EntityExistence ToEntityExistence(PuckEntityExistence resolved)
	{
		return new EntityExistence(
			resolved.Id,
			resolved.Exists,
			resolved.EntityType,
			resolved.EntityKind,
			resolved.Entity,
			resolved.AssociatedNote);
	}

	/// <inheritdoc />
	public Task<WatcherIssueReport> GetWatcherIssuesAsync(CancellationToken cancellationToken = default)
	{
		return Task.FromResult(BuildWatcherIssueReport(scopedAbsolutePath: null, scopedPathIsDirectory: false));
	}

	/// <inheritdoc />
	public Task<WatcherIssueReport> GetWatcherIssuesForPathAsync(string scopedPath, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(scopedPath);
		var normalized = scopedPath
			.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
			.Trim();

		var scopedAbsolutePath = ResolveScopedAbsolutePath(normalized);
		var scopedPathIsDirectory = IsDirectoryScope(normalized, scopedAbsolutePath);
		return Task.FromResult(BuildWatcherIssueReport(scopedAbsolutePath, scopedPathIsDirectory));
	}

	private WatcherIssueReport BuildWatcherIssueReport(string? scopedAbsolutePath, bool scopedPathIsDirectory)
	{
		var allIssues = watcherIssueRegistry.GetIssues();
		var allCriteria = watcherIssueRegistry.GetCriteriaStates();

		var filteredIssues = scopedAbsolutePath is null
			? allIssues
			: allIssues.Where(issue => MatchesScope(issue.Path, scopedAbsolutePath, scopedPathIsDirectory)).ToList();

		var filteredCriteria = scopedAbsolutePath is null
			? allCriteria
			: allCriteria.Where(criterion => MatchesScope(criterion.Path, scopedAbsolutePath, scopedPathIsDirectory)).ToList();

		var issueRecords = filteredIssues
			.Select(issue => new WatcherIssueRecord(
				issue.Key,
				issue.Type.ToString(),
				issue.Category,
				issue.Message,
				issue.IsCritical,
				issue.Criterion,
				issue.ResolutionCriterion,
				issue.OccurrenceCount,
				issue.Path,
				ToVaultRelativePathOrNull(issue.Path),
				issue.FirstObservedUtc,
				issue.LastObservedUtc))
			.ToList();

		var criterionRecords = filteredCriteria
			.Select(criterion => new WatcherCriterionRecord(
				criterion.Criterion,
				criterion.Satisfied,
				criterion.EvaluatedUtc,
				criterion.ScopeKey,
				criterion.Path,
				ToVaultRelativePathOrNull(criterion.Path),
				criterion.Detail))
			.ToList();

		return new WatcherIssueReport(
			watcherIssueRegistry.GetStatus().ToString().ToLowerInvariant(),
			issueRecords.Count,
			issueRecords.Count(static issue => issue.IsCritical),
			criterionRecords.Count,
			criterionRecords.Count(static criterion => !criterion.Satisfied),
			scopedAbsolutePath is null ? null : ToVaultRelativePathOrAbsolute(scopedAbsolutePath),
			scopedPathIsDirectory,
			issueRecords,
			criterionRecords);
	}

	private string ResolveScopedAbsolutePath(string scopedPath)
	{
		var absolute = Path.IsPathRooted(scopedPath)
			? Path.GetFullPath(scopedPath)
			: Path.GetFullPath(Path.Combine(layout.VaultRoot, scopedPath.TrimStart(Path.DirectorySeparatorChar)));

		if (!IsPathUnderRoot(absolute, layout.VaultRoot))
		{
			throw new InvalidOperationException("Scoped path must remain under the active vault root.");
		}

		return absolute.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
	}

	private static bool IsDirectoryScope(string rawPath, string scopedAbsolutePath)
	{
		if (Directory.Exists(scopedAbsolutePath))
		{
			return true;
		}

		if (File.Exists(scopedAbsolutePath))
		{
			return false;
		}

		return rawPath.EndsWith(Path.DirectorySeparatorChar)
			|| rawPath.EndsWith(Path.AltDirectorySeparatorChar)
			|| !Path.HasExtension(rawPath);
	}

	private static bool MatchesScope(string? candidatePath, string scopedAbsolutePath, bool scopedPathIsDirectory)
	{
		if (string.IsNullOrWhiteSpace(candidatePath))
		{
			return false;
		}

		var normalizedCandidatePath = Path.GetFullPath(candidatePath)
			.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

		if (!scopedPathIsDirectory)
		{
			return string.Equals(normalizedCandidatePath, scopedAbsolutePath, StringComparison.OrdinalIgnoreCase);
		}

		return string.Equals(normalizedCandidatePath, scopedAbsolutePath, StringComparison.OrdinalIgnoreCase)
			|| normalizedCandidatePath.StartsWith(scopedAbsolutePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
	}

	private string? ToVaultRelativePathOrNull(string? absolutePath)
	{
		if (string.IsNullOrWhiteSpace(absolutePath))
		{
			return null;
		}

		return IsPathUnderRoot(absolutePath, layout.VaultRoot)
			? Path.GetRelativePath(layout.VaultRoot, absolutePath)
			: null;
	}

	private string ToVaultRelativePathOrAbsolute(string absolutePath)
	{
		return IsPathUnderRoot(absolutePath, layout.VaultRoot)
			? Path.GetRelativePath(layout.VaultRoot, absolutePath)
			: absolutePath;
	}

	private async Task<(string? Puck, string Title)> ResolveEntityIdentityAsync(
		Type entityType,
		string absolutePath,
		string? pathPuck,
		string pathTitle,
		CancellationToken cancellationToken,
		ILogger? logger = null)
	{
		/*if (!string.IsNullOrWhiteSpace(pathPuck))
		{
			return (pathPuck, pathTitle);
		}*/

		if (entityType == typeof(Objective))
		{
			return await ResolveObjectiveIdentityAsync(absolutePath, pathTitle, cancellationToken);
		}

		if (entityType == typeof(Directive))
		{
			var directive = await ResolveDirectiveByDirectoryPathAsync(Path.GetDirectoryName(absolutePath), cancellationToken);
			return directive is null ? (null, pathTitle) : (directive.Id, directive.Title);
		}

		if (entityType == typeof(OnrushSprint))
		{
			var sprint = await ResolveSingleOnrushSprintByTitleAsync(pathTitle, cancellationToken);
			return sprint is null ? (null, pathTitle) : (sprint.Id, sprint.Title);
		}

		if (entityType == typeof(PolarisCycle))
		{
			var cycle = await ResolveSinglePolarisCycleByTitleAsync(pathTitle, cancellationToken);
			return cycle is null ? (null, pathTitle) : (cycle.Id, cycle.Title);
		}

		if (entityType == typeof(LorePage))
		{
			var lorePage = new LorePage
			{
				Id = string.Empty,
				Title = string.Empty,
			};


			if (MarkdownFileLocator.ApplyLorePageCompositionFromPath(lorePage, absolutePath, layout.VaultRoot, layout.SagaRoot, logger))
			{
				return (lorePage.Id, lorePage.Title);
			}
		}

		return (null, pathTitle);
	}

	private async Task<(string? Puck, string Title)> ResolveObjectiveIdentityAsync(string absolutePath, string title, CancellationToken cancellationToken)
	{
		IQueryable<Objective> query = context.Objectives
			.AsNoTracking()
			.Where(item => item.Title == title);

		if (IsPathUnderRoot(absolutePath, layout.ObjectivesRoot))
		{
			query = query.Where(item => item.DirectiveId == null);
		}
		else if (IsPathUnderRoot(absolutePath, layout.DirectivesRoot))
		{
			var parentDirective = await ResolveDirectiveByDirectoryPathAsync(Path.GetDirectoryName(absolutePath), cancellationToken);
			if (parentDirective is not null)
			{
				query = query.Where(item => item.DirectiveId == parentDirective.Id);
			}
		}

		var candidates = await query
			.OrderBy(item => item.Id)
			.Take(2)
			.ToListAsync(cancellationToken);

		if (candidates.Count == 1)
		{
			return (candidates[0].Id, candidates[0].Title);
		}

		var fallbackCandidates = await context.Objectives
			.AsNoTracking()
			.Where(item => item.Title == title)
			.OrderBy(item => item.Id)
			.Take(2)
			.ToListAsync(cancellationToken);

		return fallbackCandidates.Count == 1
			? (fallbackCandidates[0].Id, fallbackCandidates[0].Title)
			: (null, title);
	}

	private async Task<Directive?> ResolveDirectiveByDirectoryPathAsync(string? directoryPath, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(directoryPath))
		{
			return null;
		}

		var fullDirectoryPath = Path.GetFullPath(directoryPath);
		var directivesRoot = Path.GetFullPath(layout.DirectivesRoot);
		if (!IsPathUnderRoot(fullDirectoryPath, directivesRoot) || string.Equals(fullDirectoryPath, directivesRoot, StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		var titles = new Stack<string>();
		var currentDirectory = fullDirectoryPath;

		while (!string.Equals(currentDirectory, directivesRoot, StringComparison.OrdinalIgnoreCase))
		{
			if (MarkdownFileLocator.IsSelfNamedDirectory(currentDirectory))
			{
				var primaryFile = Path.Combine(currentDirectory, $"{Path.GetFileName(currentDirectory)}.md");
				titles.Push(MarkdownFileLocator.ParseLoosePuckIdentityFromPath(primaryFile).Title);
			}

			currentDirectory = Directory.GetParent(currentDirectory)?.FullName
				?? throw new InvalidOperationException("Directive path resolution lost its parent chain before reaching the directives root.");
		}

		if (titles.Count == 0)
		{
			return null;
		}

		Directive? resolved = null;
		string? parentDirectiveId = null;
		while (titles.Count > 0)
		{
			var title = titles.Pop();
			var matches = await context.Directives
				.AsNoTracking()
				.Where(item => item.Title == title && item.ParentDirectiveId == parentDirectiveId)
				.OrderBy(item => item.Id)
				.Take(2)
				.ToListAsync(cancellationToken);

			if (matches.Count != 1)
			{
				return null;
			}

			resolved = matches[0];
			parentDirectiveId = resolved.Id;
		}

		return resolved;
	}

	private async Task<OnrushSprint?> ResolveSingleOnrushSprintByTitleAsync(string title, CancellationToken cancellationToken)
	{
		var matches = await context.OnrushSprints
			.AsNoTracking()
			.Where(item => item.Title == title)
			.OrderBy(item => item.Id)
			.Take(2)
			.ToListAsync(cancellationToken);

		return matches.Count == 1 ? matches[0] : null;
	}

	private async Task<PolarisCycle?> ResolveSinglePolarisCycleByTitleAsync(string title, CancellationToken cancellationToken)
	{
		var matches = await context.PolarisCycles
			.AsNoTracking()
			.Where(item => item.Title == title)
			.OrderBy(item => item.Id)
			.Take(2)
			.ToListAsync(cancellationToken);

		return matches.Count == 1 ? matches[0] : null;
	}

	private static bool IsPathUnderRoot(string path, string root)
	{
		var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		return string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase)
			|| fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
	}

	private async Task<OnrushSprint?> LoadPlanningOnrushSprintAsync(CancellationToken cancellationToken)
	{
		var planned = await stateService.GetPlanningOnrushSprintAsync(cancellationToken);
		return planned is null ? null : await LoadOnrushSprintAsync(planned.Id, cancellationToken);
	}

	private async Task<OnrushSprint?> LoadOnrushSprintAsync(string sprintId, CancellationToken cancellationToken)
	{
		var sprint = await context.OnrushSprints
			.AsNoTracking()
			.Include(item => item.Objectives)
			.Include(item => item.ExecutiveOrders)
			.FirstOrDefaultAsync(item => item.Id == sprintId, cancellationToken);

		return sprint;
	}

	private async Task<PolarisCycle?> LoadPolarisCycleAsync(string cycleId, CancellationToken cancellationToken)
	{
		var cycle = await context.PolarisCycles
			.AsNoTracking()
			.Include(item => item.Executives)
				.ThenInclude(item => item.Objective)
			.Include(item => item.Reflectives)
				.ThenInclude(item => item.Decree)
			.Include(item => item.Attentives)
				.ThenInclude(item => item.Decree)
			.FirstOrDefaultAsync(item => item.Id == cycleId, cancellationToken);

		return cycle;
	}

	private async Task<IReadOnlyList<LorePage>> LoadActiveLorePagesAsync(CancellationToken cancellationToken)
	{
		var today = DateOnly.FromDateTime(DateTime.UtcNow);
		var lorePages = await context.LorePages
			.AsNoTracking()
			.ToListAsync(cancellationToken);

		if (lorePages.Count == 0)
		{
			return [];
		}

		var siblingsByParent = lorePages
			.GroupBy(item => item.ParentId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

		var active = lorePages
			.Where(item =>
			{
				var siblings = siblingsByParent[item.ParentId ?? string.Empty];
				var endingExclusive = LorePage.ResolveEndingExclusive(item, siblings);
				return item.WasOngoingIn(today, endingExclusive);
			})
			.Where(item =>
				string.Equals(item.Level, "Era", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(item.Level, "Cha", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(item.Level, "Act", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(item.Level, "p", StringComparison.OrdinalIgnoreCase))
			.OrderBy(item => GetLoreLevelOrder(item.Level))
			.ThenBy(item => item.Beginning)
			.ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
			.ToArray();

		return active;
	}

	private static int GetLoreLevelOrder(string level)
	{
		return level.Trim().ToLowerInvariant() switch
		{
			"era" => 0,
			"cha" => 1,
			"act" => 2,
			"p" => 3,
			_ => 99,
		};
	}
}