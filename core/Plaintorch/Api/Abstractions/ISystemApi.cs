using Pleiades.Plaintorch.Api.Contracts;

namespace Pleiades.Plaintorch.Api.Abstractions;

/// <summary>
/// Defines the system-facing application actions exposed by PLAINTORCH.
/// </summary>
public interface ISystemApi
{
	/// <summary>
	/// Gets a compact system brief.
	/// </summary>
	Task<SystemBrief> BriefAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Gets the richer system briefing payload for dashboard-like surfaces.
	/// </summary>
	Task<SystemBriefing> GetBriefingAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Resolves the authoritative PLAINTORCH entity interpretation for a vault-relative markdown path.
	/// </summary>
	Task<VaultNoteAuthorityResolution> ResolveVaultNoteAsync(string vaultRelativePath, CancellationToken cancellationToken = default);
}