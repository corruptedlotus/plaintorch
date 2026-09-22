using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.State;
using Microsoft.EntityFrameworkCore;
using Pleiades.Calendar;
using Pleiades.Diagnostics;
using Pleiades.Plaintorch.Diagnostics;
using Pleiades.Orchestration;
using Pleiades.Puck;
using Pleiades.Saga;
using Pleiades.Vault;
using Pleiades.Vault.Database;
using Pleiades.Vault.Markdown;
using Pleiades.Vault.Watcher;
using System.Reflection;

namespace Pleiades.Plaintorch.Api.Services;

/// <summary>
/// Implements the system-facing PLAINTORCH application API.
/// </summary>
public sealed class SystemApiService(
	PlaintorchStateService stateService,
	PlainfraContext context,
	VaultLayout layout,
	VaultPathSyncModelCatalog pathSyncModelCatalog,
	OperationStatusRegistry statusRegistry,
	OperationStatusDismissalService dismissalService,
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
		var watcherStatus = MapHealthStatus(statusRegistry.GetHealth());
		// Dismissed statuses (PEP108 dismiss feature) are excluded from the briefing counts, matching the health rollup.
		var watcherIssues = statusRegistry.GetActiveStatusesWithDismissal()
			.Where(item => !item.Dismissed)
			.Select(item => item.Status)
			.ToList();

		return new SystemBriefing(
			"ok",
			DateTimeOffset.UtcNow,
			stateService.GetActiveVaultPath(),
			PleiadeanCalendar.FromDateTime(DateTime.Today).ToString(),
			await stateService.GetCelestronBankedAsync(cancellationToken),
			watcherStatus,
			watcherIssues.Count,
			watcherIssues.Count(static issue => IsCriticalSeverity(issue.Severity)),
			watcherIssues.Count,
			watcherIssues.Count,
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

		// Fast path: when the path shape classifies to a PUCK-backed kind, resolve that kind's identity from the path
		// (Index kinds carry the PUCK in the filename; title-driven kinds resolve by title/parent) and return it only
		// when a stored entity stands behind it.
		if (pathSyncModelCatalog.TryResolve(absolutePath, out var model)
			&& model is not null
			&& PuckEntityAttribute.ResolveKind(model.EntityType) is not null)
		{
			var (pathPuck, pathTitle) = MarkdownFileLocator.ParseLoosePuckIdentityFromPath(absolutePath);
			var (resolvedPuck, _) = await ResolveEntityIdentityAsync(model.EntityType, absolutePath, pathPuck, pathTitle, cancellationToken, logger);
			if (!string.IsNullOrWhiteSpace(resolvedPuck))
			{
				var byPath = await ResolveEntityByPuckAsync(resolvedPuck, cancellationToken);
				if (byPath.Exists)
				{
					return byPath;
				}
			}
		}

		// Authoritative fallback: the note's OWN asserted frontmatter identity. This resolves identity-driven notes the
		// path shape does not — or mis- — claim: a freeform directive note whose folder is read as a containing
		// directive, or any freeform/implicit note outside the canonical layout. It stays stored-only — existence is
		// reported only when the asserted identity maps to a stored entity — so a note carrying no stored identity is
		// still not an entity (the removed path-shape "template entity" phantom is not re-introduced).
		var frontMatterResolution = await ResolveByFrontMatterPuckAsync(absolutePath, cancellationToken);
		return frontMatterResolution ?? new EntityExistence(normalizedRelativePath, false);
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

		string markdown;
		try
		{
			markdown = await VaultFileAccess.ReadAllTextAsync(absolutePath, cancellationToken);
		}
		catch (VaultFileAccessException exception)
		{
			// The note is momentarily held open by another process; report it as not-yet-resolvable rather than
			// failing the request, so a note-resolution call never turns into a 500 over a transient lock.
			logger.LogDebug(exception, "Note '{Path}' is in use; treating it as unresolved for now.", absolutePath);
			return null;
		}

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

		// This note asserts this identity, so it IS the entity's associated note — authoritative over the self-named
		// enumeration inside ResolveAsync, which does not locate a freeform note whose file name differs from its folder.
		var relativeNote = Path.GetRelativePath(layout.VaultRoot, absolutePath).Replace(Path.DirectorySeparatorChar, '/');
		return ToEntityExistence(resolved) with { AssociatedNote = relativeNote };
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
		var allStatuses = statusRegistry.GetActiveStatusesWithDismissal();

		var filtered = scopedAbsolutePath is null
			? allStatuses
			: allStatuses.Where(item => MatchesScope(ScopePathOf(item.Status), scopedAbsolutePath, scopedPathIsDirectory)).ToList();

		// The report carries every active status (dismissed ones flagged) so a client can show a "Dismissed" section,
		// but the counts and failed criteria — the live problem surface that drives the indicator — exclude dismissed.
		var issueRecords = filtered.Select(item => ToWatcherIssueRecord(item.Status, item.Dismissed)).ToList();
		var liveRecords = issueRecords.Where(static issue => !issue.Dismissed).ToList();
		var criterionRecords = filtered.Where(item => !item.Dismissed).Select(item => ToWatcherCriterionRecord(item.Status)).ToList();

		return new WatcherIssueReport(
			MapHealthStatus(statusRegistry.GetHealth()),
			liveRecords.Count,
			liveRecords.Count(static issue => issue.IsCritical),
			criterionRecords.Count,
			criterionRecords.Count(static criterion => !criterion.Satisfied),
			scopedAbsolutePath is null ? null : ToVaultRelativePathOrAbsolute(scopedAbsolutePath),
			scopedPathIsDirectory,
			issueRecords,
			criterionRecords);
	}

	private WatcherIssueRecord ToWatcherIssueRecord(OperationStatus status, bool dismissed)
	{
		var descriptor = WatcherOperations.Describe(status.ReasonCode);
		var originPath = ScopePathOf(status);
		return new WatcherIssueRecord(
			WatcherOperations.ComposeIssueKey(status.OperationId, status.ReasonCode, status.ScopeKey),
			status.OperationId,
			descriptor.Category,
			descriptor.Message,
			status.Detail,
			IsCriticalSeverity(status.Severity),
			status.Severity.ToString().ToLowerInvariant(),
			status.Files.Select(ToVaultRelativePathOrAbsolute).ToList(),
			status.ReasonCode,
			$"{status.ReasonCode}-cleared",
			status.OccurrenceCount,
			originPath,
			ToVaultRelativePathOrNull(originPath),
			status.FirstRaisedUtc,
			status.LastObservedUtc,
			dismissed);
	}

	/// <inheritdoc />
	public async Task<bool> DismissWatcherIssueAsync(string issueKey, string? scope = null, CancellationToken cancellationToken = default)
	{
		if (!TryResolveDismissalTarget(issueKey, scope, out var dismissalScope, out var operationId, out var scopeKey, out var reasonCode))
		{
			return false;
		}

		return await dismissalService.DismissAsync(dismissalScope, operationId, scopeKey, reasonCode, cancellationToken);
	}

	/// <inheritdoc />
	public async Task<bool> RestoreWatcherIssueAsync(string issueKey, string? scope = null, CancellationToken cancellationToken = default)
	{
		if (!TryResolveDismissalTarget(issueKey, scope, out var dismissalScope, out var operationId, out var scopeKey, out var reasonCode))
		{
			return false;
		}

		return await dismissalService.RestoreAsync(dismissalScope, operationId, scopeKey, reasonCode, cancellationToken);
	}

	private static bool TryResolveDismissalTarget(
		string issueKey,
		string? scope,
		out OperationStatusDismissalScope dismissalScope,
		out string operationId,
		out string scopeKey,
		out string reasonCode)
	{
		dismissalScope = ParseDismissalScope(scope);
		return WatcherOperations.TryParseIssueKey(issueKey, out operationId, out reasonCode, out scopeKey);
	}

	private static OperationStatusDismissalScope ParseDismissalScope(string? scope)
		=> scope?.Trim().ToLowerInvariant() switch
		{
			"file" => OperationStatusDismissalScope.File,
			"reason" => OperationStatusDismissalScope.Reason,
			_ => OperationStatusDismissalScope.Instance,
		};

	private WatcherCriterionRecord ToWatcherCriterionRecord(OperationStatus status)
	{
		var originPath = ScopePathOf(status);
		return new WatcherCriterionRecord(
			status.ReasonCode,
			Satisfied: false,
			status.LastObservedUtc,
			status.ScopeKey,
			originPath,
			ToVaultRelativePathOrNull(originPath),
			status.Detail);
	}

	private static string? ScopePathOf(OperationStatus status)
		=> string.Equals(status.ScopeKey, WatcherOperations.GlobalScope, StringComparison.Ordinal) ? null : status.ScopeKey;

	private static bool IsCriticalSeverity(OperationSeverity severity) => severity >= OperationSeverity.Error;

	private static string MapHealthStatus(OperationHealth health) => health switch
	{
		OperationHealth.Ok => "ok",
		OperationHealth.Suspended => "standby",
		OperationHealth.Issues => "issues",
		OperationHealth.Offline => "offline",
		_ => "ok",
	};

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
		var storage = entityType.GetCustomAttribute<VaultStorageAttribute>(inherit: true);

		// Lore identity is composed from the full Era/Cha/Act/p directory hierarchy, not the terminal filename segment,
		// so it must compose before the flat Index shortcut below — which would otherwise return just "Act9" for the
		// note whose real identity is "Era3/Cha8/Act9", leaving every nested lore page unresolvable.
		if (entityType == typeof(LorePage))
		{
			var lorePage = new LorePage
			{
				Id = string.Empty,
				Title = string.Empty,
			};

			return MarkdownFileLocator.ApplyLorePageCompositionFromPath(lorePage, absolutePath, layout.VaultRoot, layout.SagaRoot, logger)
				? (lorePage.Id, lorePage.Title)
				: (null, pathTitle);
		}

		if (storage?.PuckStorage == VaultPuckStorage.Index && !string.IsNullOrWhiteSpace(pathPuck))
		{
			return (pathPuck, pathTitle);
		}

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
				.ThenInclude(item => item.Incentive)
			.Include(item => item.Reflectives)
				.ThenInclude(item => item.Decree)
			.FirstOrDefaultAsync(item => item.Id == cycleId, cancellationToken);

		return cycle;
	}

	private async Task<IReadOnlyList<LorePage>> LoadActiveLorePagesAsync(CancellationToken cancellationToken)
	{
		var today = DateOnly.FromDateTime(DateTime.UtcNow);
		var lorePages = await context.LorePages
			.AsNoTracking()
			.ToLoreIndexAsync(cancellationToken);

		return lorePages.ActivePages;
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