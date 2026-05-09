using System.Collections.Concurrent;

namespace Pleiades.Vault.Watcher;

/// <summary>
/// Suppresses short-lived watcher events caused by service-originated filesystem writes.
/// </summary>
public sealed class VaultWatcherWriteBarrier
{
	private readonly ConcurrentDictionary<string, DateTimeOffset> _suppressedPaths = new(StringComparer.OrdinalIgnoreCase);
	private readonly TimeSpan _ttl = TimeSpan.FromSeconds(3);

	/// <summary>
	/// Marks a path as service-originated for a short suppression window.
	/// </summary>
	public void Suppress(string path)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		_suppressedPaths[Normalize(path)] = DateTimeOffset.UtcNow.Add(_ttl);
	}

	/// <summary>
	/// Determines whether a watcher event for the path should currently be ignored.
	/// </summary>
	public bool IsSuppressed(string path)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		var normalized = Normalize(path);
		if (!_suppressedPaths.TryGetValue(normalized, out var expiresAt))
		{
			return false;
		}

		if (expiresAt < DateTimeOffset.UtcNow)
		{
			_suppressedPaths.TryRemove(normalized, out _);
			return false;
		}

		return true;
	}

	/// <summary>
	/// Removes expired suppressions.
	/// </summary>
	public void PruneExpired()
	{
		var now = DateTimeOffset.UtcNow;
		foreach (var pair in _suppressedPaths)
		{
			if (pair.Value < now)
			{
				_suppressedPaths.TryRemove(pair.Key, out _);
			}
		}
	}

	private static string Normalize(string path)
	{
		return Path.GetFullPath(path);
	}
}
