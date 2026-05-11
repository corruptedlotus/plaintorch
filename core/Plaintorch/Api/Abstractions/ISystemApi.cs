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
}