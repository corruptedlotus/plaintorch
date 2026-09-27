using Microsoft.EntityFrameworkCore;
using Pleiades.Plaintorch.Materialization;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.Preferences;

/// <summary>
/// Reads and writes vault-bound user preferences (PEP116). Preferences are <em>sparse</em>: the database holds
/// only the values a user has explicitly set, and everything else resolves to the code-owned default (an Options
/// POCO property initializer), so introducing a new preference needs no migration or seed.
/// </summary>
/// <remarks>
/// Writes are write-through — the database row and the in-memory <see cref="UserPreferenceStore"/> (the Options
/// read path) are updated together, so a change takes effect immediately and survives restarts. Scoped, because
/// it holds the vault database context; the store it writes through is the singleton.
/// <para>
/// Every store write that can change <see cref="PreferenceKeys.DefaultCalendar"/> also invalidates the
/// <see cref="TimeframeCandidateCache"/> once the new value is visible (PEP100 patch 2). The save interceptor
/// invalidates too, but it fires before the store is updated, so a recompute racing that gap could otherwise cache
/// candidates read on the old calendar with nothing left to drop them.
/// </para>
/// </remarks>
public sealed class UserPreferenceService(PlainfraContext context, UserPreferenceStore store, TimeframeCandidateCache timeframeCandidateCache)
{
	/// <summary>Loads every stored override for the active vault into the store (called on vault activation).</summary>
	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		var rows = await context.UserPreferences.AsNoTracking().ToListAsync(cancellationToken);
		store.Load(rows.Select(row => new KeyValuePair<string, string>(row.Key, row.Value)));
		// A wholesale reload may swap the default calendar.
		timeframeCandidateCache.Invalidate();
	}

	/// <summary>
	/// Resolves a preference straight from the database, returning <paramref name="fallback"/> when it is unset
	/// or its stored value cannot be read. Prefer injecting the matching Options type for hot reads.
	/// </summary>
	public async Task<T> GetAsync<T>(string key, T fallback, CancellationToken cancellationToken = default)
	{
		var row = await context.UserPreferences.AsNoTracking()
			.FirstOrDefaultAsync(item => item.Key == key, cancellationToken);
		return row is null ? fallback : UserPreferenceSerializer.Deserialize(row.Value, fallback);
	}

	/// <summary>Sets a preference (idempotent upsert), writing through to the store.</summary>
	public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default)
		=> SetRawAsync(key, UserPreferenceSerializer.Serialize(value), cancellationToken);

	/// <summary>
	/// Sets a preference from an already-serialized JSON value (the generic API write path), writing through to
	/// the store. The value is stored as-is; a value that cannot later be read as the consumer's type degrades to
	/// the default on read.
	/// </summary>
	public async Task SetRawAsync(string key, string rawValue, CancellationToken cancellationToken = default)
	{
		var existing = await context.UserPreferences.FirstOrDefaultAsync(item => item.Key == key, cancellationToken);
		if (existing is null)
		{
			context.UserPreferences.Add(new UserPreferenceRecord
			{
				Key = key,
				Value = rawValue,
				UpdatedUtc = DateTimeOffset.UtcNow,
			});
		}
		else
		{
			existing.Value = rawValue;
			existing.UpdatedUtc = DateTimeOffset.UtcNow;
		}

		await context.SaveChangesAsync(cancellationToken);
		store.Set(key, rawValue);
		InvalidateCandidatesFor(key);
	}

	/// <summary>Clears a preference back to its default (delete), writing through to the store.</summary>
	/// <returns>Whether a stored value was removed.</returns>
	public async Task<bool> ResetAsync(string key, CancellationToken cancellationToken = default)
	{
		var existing = await context.UserPreferences.FirstOrDefaultAsync(item => item.Key == key, cancellationToken);
		if (existing is not null)
		{
			context.UserPreferences.Remove(existing);
			await context.SaveChangesAsync(cancellationToken);
		}

		store.Remove(key);
		InvalidateCandidatesFor(key);
		return existing is not null;
	}

	/// <summary>
	/// Drops the cached timeframe candidates after a store write to the default calendar, which decides how timeframe
	/// orbits read (PEP100 patch 2). Other keys leave the candidates alone.
	/// </summary>
	private void InvalidateCandidatesFor(string key)
	{
		if (string.Equals(key, PreferenceKeys.DefaultCalendar, StringComparison.Ordinal))
		{
			timeframeCandidateCache.Invalidate();
		}
	}
}
