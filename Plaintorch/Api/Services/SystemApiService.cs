using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.State;

namespace Pleiades.Plaintorch.Api.Services;

/// <summary>
/// Implements the system-facing PLAINTORCH application API.
/// </summary>
public sealed class SystemApiService(PlaintorchStateService stateService) : ISystemApi
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
}