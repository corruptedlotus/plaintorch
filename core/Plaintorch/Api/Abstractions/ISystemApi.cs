using Pleiades.Plaintorch.Api.Contracts;

namespace Pleiades.Plaintorch.Api.Abstractions;

/// <summary>
/// Defines the system-facing application actions exposed by PLAINTORCH.
/// </summary>
public interface ISystemApi
{
	/// <summary>
	/// Gets the richer system briefing payload for dashboard-like surfaces.
	/// </summary>
	Task<SystemBriefing> GetBriefingAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Resolves the authoritative PLAINTORCH entity interpretation for a vault-relative markdown path.
	/// </summary>
	Task<EntityExistence> ResolveVaultNoteAsync(string vaultRelativePath, CancellationToken cancellationToken = default);

	/// <summary>
	/// Resolves a PUCK identifier into its authoritative entity representation.
	/// </summary>
	Task<EntityExistence> ResolveEntityByPuckAsync(string id, CancellationToken cancellationToken = default);

	/// <summary>
	/// Gets full watcher diagnostics including active issues and evaluated criteria.
	/// </summary>
	Task<WatcherIssueReport> GetWatcherIssuesAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Gets watcher diagnostics scoped to a specific vault file or folder path.
	/// </summary>
	Task<WatcherIssueReport> GetWatcherIssuesForPathAsync(string scopedPath, CancellationToken cancellationToken = default);
}