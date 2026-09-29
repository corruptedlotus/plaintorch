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

namespace Pleiades.Plaintorch.Api.Services;

/// <summary>
/// Implements the system-facing PLAINTORCH application API.
/// </summary>
public sealed class SystemApiService(
	PlaintorchStateService stateService,
	PlainfraContext context,
	VaultLayout layout,
	OperationStatusRegistry statusRegistry,
	OperationStatusDismissalService dismissalService,
	PuckEntityResolutionService puckEntityResolutionService,
	VaultMarkdownDiscoveryService discoveryService,
	VaultEntityModelCatalog entityModelCatalog,
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

		// A note is an entity's only through the identity it carries, never through its title, and it carries it exactly
		// where the watcher reads it: the kind the note is (settled by what it asserts where the kind is identity-driven),
		// and the one identity source that kind's storage declares — an Index filename token, a lore hierarchy composed
		// from the folder tree, or a quiet note's frontmatter. The identity counts only when a stored entity of that kind
		// stands behind it, and then this note, the one asserting it, is the entity's associated note.
		VaultSyncCandidate? note;
		try
		{
			note = await discoveryService.ReadNoteAsync(absolutePath, cancellationToken);
		}
		catch (VaultFileAccessException exception)
		{
			// The note is momentarily held open by another process; report it as not-yet-resolvable rather than
			// failing the request, so a note-resolution call never turns into a 500 over a transient lock.
			logger.LogDebug(exception, "Note '{Path}' is in use; treating it as unresolved for now.", absolutePath);
			note = null;
		}

		// A path that holds no note is no entity's note, even where a path-bound kind's path alone would name an identity.
		if (note is not { FileExists: true, PathId: { Length: > 0 } identity })
		{
			return new EntityExistence(normalizedRelativePath, false);
		}

		var resolved = await puckEntityResolutionService.ResolveAsync(identity, cancellationToken);
		if (!resolved.Exists || !entityModelCatalog.IsFamilyMember(note.Model.EntityType, resolved.EntityType))
		{
			return new EntityExistence(normalizedRelativePath, false);
		}

		return ToEntityExistence(resolved) with { AssociatedNote = note.VaultRelativePath.Replace(Path.DirectorySeparatorChar, '/') };
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

		// A fatal issue is why the watcher is on standby; it is not the user's to snooze, whatever the dismissal's scope.
		if (statusRegistry.FindActive(operationId, scopeKey, reasonCode) is { } active && !OperationStatusRegistry.IsDismissible(active))
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

	/// <summary>Whether a severity counts as critical on the wire: critical or fatal — the ones that block the watcher's work.</summary>
	private static bool IsCriticalSeverity(OperationSeverity severity) => severity >= OperationSeverity.Critical;

	private static string MapHealthStatus(OperationHealth health) => health switch
	{
		OperationHealth.Ok => "ok",
		OperationHealth.Issues => "issues",
		OperationHealth.Critical => "critical",
		OperationHealth.Standby => "standby",
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