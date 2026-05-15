using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.State;
using Microsoft.EntityFrameworkCore;
using Pleiades.Calendar;
using Pleiades.Orchestration;
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
	VaultPathSyncModelCatalog pathSyncModelCatalog) : ISystemApi
{
	/// <inheritdoc />
	public async Task<SystemBrief> BriefAsync(CancellationToken cancellationToken = default)
	{
		var activeOnrush = await stateService.GetActiveOnrushSprintAsync(cancellationToken);
		var activePolaris = await stateService.GetActivePolarisCycleAsync(cancellationToken);
		var banked = await stateService.GetCelestronBankedAsync(cancellationToken);

		return new SystemBrief(
			DateTimeOffset.UtcNow,
			stateService.GetActiveVaultPath(),
			activeOnrush?.Id,
			activePolaris?.Id,
			banked);
	}

	/// <inheritdoc />
	public async Task<SystemBriefing> GetBriefingAsync(CancellationToken cancellationToken = default)
	{
		var activeOnrush = await stateService.GetActiveOnrushSprintAsync(cancellationToken);
		var briefingOnrush = activeOnrush is not null
			? await LoadBriefingOnrushSprintAsync(activeOnrush.Id, "active", cancellationToken)
			: await LoadPlanningOnrushSprintAsync(cancellationToken);

		var activePolaris = await stateService.GetActivePolarisCycleAsync(cancellationToken);
		var briefingPolaris = activePolaris is null
			? null
			: await LoadBriefingPolarisCycleAsync(activePolaris.Id, cancellationToken);

		return new SystemBriefing(
			"ok",
			DateTimeOffset.UtcNow,
			stateService.GetActiveVaultPath(),
			PleiadeanCalendar.FromDateTime(DateTime.Today).ToString(),
			await stateService.GetCelestronBankedAsync(cancellationToken),
			briefingOnrush,
			briefingPolaris);
	}

	/// <inheritdoc />
	public async Task<VaultNoteAuthorityResolution> ResolveVaultNoteAsync(string vaultRelativePath, CancellationToken cancellationToken = default)
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
			return new VaultNoteAuthorityResolution(normalizedRelativePath, false);
		}

		var entityKind = model.EntityType.Name switch
		{
			"Directive" => "directive",
			"Objective" => "objective",
			"OnrushSprint" => "onrush-sprint",
			"PolarisCycle" => "polaris-cycle",
			_ => null,
		};

		if (entityKind is null)
		{
			return new VaultNoteAuthorityResolution(normalizedRelativePath, false);
		}

		var (pathPuck, pathTitle) = MarkdownFileLocator.ParseLoosePuckIdentityFromPath(absolutePath);
		var (resolvedPuck, resolvedTitle) = await ResolveEntityIdentityAsync(model.EntityType, absolutePath, pathPuck, pathTitle, cancellationToken);

		return new VaultNoteAuthorityResolution(
			normalizedRelativePath,
			true,
			entityKind,
			model.EntityName,
			$"plaintorch-{entityKind}",
			resolvedPuck,
			resolvedTitle);
	}

	private async Task<(string? Puck, string Title)> ResolveEntityIdentityAsync(
		Type entityType,
		string absolutePath,
		string? pathPuck,
		string pathTitle,
		CancellationToken cancellationToken)
	{
		if (!string.IsNullOrWhiteSpace(pathPuck))
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
			if (!MarkdownFileLocator.IsSelfNamedDirectory(currentDirectory))
			{
				return null;
			}

			var primaryFile = Path.Combine(currentDirectory, $"{Path.GetFileName(currentDirectory)}.md");
			titles.Push(MarkdownFileLocator.ParseLoosePuckIdentityFromPath(primaryFile).Title);
			currentDirectory = Directory.GetParent(currentDirectory)?.FullName
				?? throw new InvalidOperationException("Directive path resolution lost its parent chain before reaching the directives root.");
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

	private async Task<SystemBriefingOnrushSprint?> LoadPlanningOnrushSprintAsync(CancellationToken cancellationToken)
	{
		var planned = await stateService.GetPlanningOnrushSprintAsync(cancellationToken);
		return planned is null ? null : await LoadBriefingOnrushSprintAsync(planned.Id, "planning", cancellationToken);
	}

	private async Task<SystemBriefingOnrushSprint?> LoadBriefingOnrushSprintAsync(string sprintId, string selectionMode, CancellationToken cancellationToken)
	{
		var sprint = await context.OnrushSprints
			.AsNoTracking()
			.Include(item => item.Objectives)
			.FirstOrDefaultAsync(item => item.Id == sprintId, cancellationToken);

		if (sprint is null)
		{
			return null;
		}

		return new SystemBriefingOnrushSprint(
			selectionMode,
			sprint.Id,
			sprint.Title,
			sprint.StartDate,
			sprint.EndDate,
			sprint.Objectives
				.OrderBy(objective => objective.Title)
				.Select(objective => new SystemBriefingObjective(
					objective.Id,
					objective.Title,
					objective.Status.ToString(),
					objective.College.ToString(),
					objective.CelestronValue,
					objective.IsEnduring))
				.ToArray());
	}

	private async Task<SystemBriefingPolarisCycle?> LoadBriefingPolarisCycleAsync(string cycleId, CancellationToken cancellationToken)
	{
		var cycle = await context.PolarisCycles
			.AsNoTracking()
			.Include(item => item.Executives)
				.ThenInclude(item => item.Objective)
			.FirstOrDefaultAsync(item => item.Id == cycleId, cancellationToken);

		if (cycle is null)
		{
			return null;
		}

		return new SystemBriefingPolarisCycle(
			cycle.Id,
			cycle.Title,
			cycle.StartTime,
			cycle.EndTime,
			cycle.IsForecast,
			cycle.Executives
				.OrderBy(item => item.Id)
				.Select(item => new SystemBriefingExecutive(
					item.Id,
					item.Title,
					item.Executed,
					item.ObjectiveId,
					item.Objective?.Title))
				.ToArray());
	}
}