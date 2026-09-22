using Microsoft.EntityFrameworkCore;
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
/// </remarks>
public sealed class UserPreferenceService(PlainfraContext context, UserPreferenceStore store)
{
	/// <summary>Loads every stored override for the active vault into the store (called on vault activation).</summary>
	public async Task LoadAsync(CancellationToken cancellationToken = default)
	{
		var rows = await context.UserPreferences.AsNoTracking().ToListAsync(cancellationToken);
		store.Load(rows.Select(row => new KeyValuePair<string, string>(row.Key, row.Value)));
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
	public async Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default)
	{
		var raw = UserPreferenceSerializer.Serialize(value);
		var existing = await context.UserPreferences.FirstOrDefaultAsync(item => item.Key == key, cancellationToken);
		if (existing is null)
		{
			context.UserPreferences.Add(new UserPreferenceRecord
			{
				Key = key,
				Value = raw,
				UpdatedUtc = DateTimeOffset.UtcNow,
			});
		}
		else
		{
			existing.Value = raw;
			existing.UpdatedUtc = DateTimeOffset.UtcNow;
		}

		await context.SaveChangesAsync(cancellationToken);
		store.Set(key, raw);
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
		return existing is not null;
	}
}
