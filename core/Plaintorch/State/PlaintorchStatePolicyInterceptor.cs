using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.State;

/// <summary>
/// Centralizes PLAINTORCH state-policy enforcement on EF Core save hooks so all write pathways share the same rules.
/// </summary>
public sealed class PlaintorchStatePolicyInterceptor(
	PlaintorchStatePolicyProcessor processor,
	PlaintorchStatePolicyFileSyncService fileSyncService) : SaveChangesInterceptor
{
	private readonly ConcurrentDictionary<Guid, PlaintorchStatePolicyResult> _resultsByContextId = new();

	/// <inheritdoc />
	public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
		DbContextEventData eventData,
		InterceptionResult<int> result,
		CancellationToken cancellationToken = default)
	{
		if (eventData.Context is PlainfraContext context)
		{
			var policyResult = await processor.ApplyAsync(context, cancellationToken);
			_resultsByContextId[context.ContextId.InstanceId] = policyResult;
		}

		return await base.SavingChangesAsync(eventData, result, cancellationToken);
	}

	/// <inheritdoc />
	public override async ValueTask<int> SavedChangesAsync(
		SaveChangesCompletedEventData eventData,
		int result,
		CancellationToken cancellationToken = default)
	{
		if (eventData.Context is PlainfraContext context
			&& _resultsByContextId.TryRemove(context.ContextId.InstanceId, out var policyResult)
			&& policyResult.SupersededForecasts.Count > 0)
		{
			await fileSyncService.DeleteSupersededForecastsAsync(policyResult.SupersededForecasts, cancellationToken);
		}

		return await base.SavedChangesAsync(eventData, result, cancellationToken);
	}

	/// <inheritdoc />
	public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
	{
		if (eventData.Context is PlainfraContext context)
		{
			_resultsByContextId.TryRemove(context.ContextId.InstanceId, out _);
		}

		return base.SaveChangesFailedAsync(eventData, cancellationToken);
	}
}