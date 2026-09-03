using Microsoft.EntityFrameworkCore;
using Pleiades.Diagnostics;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.Diagnostics;

/// <summary>
/// Bridges durable status dismissals (PEP108 dismiss feature) and the in-memory <see cref="OperationStatusRegistry"/>.
/// A dismissal is write-through: it updates the registry and the database together, so it takes effect immediately and
/// survives restarts. Because dismissal matching is by value, loading the durable set back into the registry when a
/// vault session activates re-applies each snooze to the status the startup scan re-raises.
/// </summary>
public sealed class OperationStatusDismissalService(PlainfraContext context, OperationStatusRegistry registry)
{
	/// <summary>Loads every durable dismissal for the active vault into the registry, replacing any current set.</summary>
	public async Task LoadIntoRegistryAsync(CancellationToken cancellationToken = default)
	{
		var rows = await context.OperationStatusDismissals.AsNoTracking().ToListAsync(cancellationToken);
		registry.LoadDismissals(rows.Select(ToDomain));
	}

	/// <summary>
	/// Dismisses a status. Returns <see langword="false"/> when an <see cref="OperationStatusDismissalScope.Instance"/>
	/// dismissal is requested for a status that is not currently active (nothing to dismiss); File/Reason always record.
	/// </summary>
	public async Task<bool> DismissAsync(
		OperationStatusDismissalScope scope,
		string operationId,
		string scopeKey,
		string reasonCode,
		CancellationToken cancellationToken = default)
	{
		var dismissal = registry.Dismiss(scope, operationId, scopeKey, reasonCode);
		if (dismissal is null)
		{
			return false;
		}

		var existing = await context.OperationStatusDismissals.FirstOrDefaultAsync(row => row.Key == dismissal.Key, cancellationToken);
		if (existing is null)
		{
			context.OperationStatusDismissals.Add(ToRecord(dismissal));
		}
		else
		{
			existing.OperationId = dismissal.OperationId;
			existing.ScopeKey = dismissal.ScopeKey;
			existing.ReasonCode = dismissal.ReasonCode;
			existing.Fingerprint = dismissal.Fingerprint;
			existing.DismissedUtc = dismissal.DismissedUtc;
		}

		await context.SaveChangesAsync(cancellationToken);
		return true;
	}

	/// <summary>Restores (un-dismisses) a status. Returns whether anything was removed from the registry or the store.</summary>
	public async Task<bool> RestoreAsync(
		OperationStatusDismissalScope scope,
		string operationId,
		string scopeKey,
		string reasonCode,
		CancellationToken cancellationToken = default)
	{
		var removedFromRegistry = registry.Restore(scope, operationId, scopeKey, reasonCode) is not null;

		var key = OperationStatusDismissalKey.Compose(scope, operationId, scopeKey, reasonCode);
		var row = await context.OperationStatusDismissals.FirstOrDefaultAsync(item => item.Key == key, cancellationToken);
		if (row is not null)
		{
			context.OperationStatusDismissals.Remove(row);
			await context.SaveChangesAsync(cancellationToken);
		}

		return removedFromRegistry || row is not null;
	}

	private static OperationStatusDismissal ToDomain(OperationStatusDismissalRecord record) => new(
		record.Scope,
		record.OperationId,
		record.ScopeKey,
		record.ReasonCode,
		record.Fingerprint,
		record.DismissedUtc);

	private static OperationStatusDismissalRecord ToRecord(OperationStatusDismissal dismissal) => new()
	{
		Key = dismissal.Key,
		Scope = dismissal.Scope,
		OperationId = dismissal.OperationId,
		ScopeKey = dismissal.ScopeKey,
		ReasonCode = dismissal.ReasonCode,
		Fingerprint = dismissal.Fingerprint,
		DismissedUtc = dismissal.DismissedUtc,
	};
}
