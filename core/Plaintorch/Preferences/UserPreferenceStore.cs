namespace Pleiades.Plaintorch.Preferences;

/// <summary>
/// The in-memory, copy-on-write snapshot of the active vault's stored preference overrides (PEP116). It is the
/// synchronous hot-read path behind the .NET Options binding, so resolving a preference during request or
/// watcher work never touches the database.
/// </summary>
/// <remarks>
/// A singleton for the process. It is replaced wholesale from the vault when a vault session activates
/// (<see cref="UserPreferenceService.LoadAsync"/>) and kept current by write-through from
/// <see cref="UserPreferenceService"/>. Reads see a consistent snapshot because each mutation swaps in a new
/// dictionary rather than editing the live one.
/// </remarks>
public sealed class UserPreferenceStore
{
	private volatile IReadOnlyDictionary<string, string> _overrides =
		new Dictionary<string, string>(StringComparer.Ordinal);

	/// <summary>Replaces the whole snapshot with the active vault's stored overrides.</summary>
	public void Load(IEnumerable<KeyValuePair<string, string>> overrides) =>
		_overrides = new Dictionary<string, string>(overrides, StringComparer.Ordinal);

	/// <summary>Resolves a preference from the snapshot, or returns <paramref name="fallback"/> when unset.</summary>
	public T Get<T>(string key, T fallback) =>
		_overrides.TryGetValue(key, out var raw) ? UserPreferenceSerializer.Deserialize(raw, fallback) : fallback;

	/// <summary>Records an override, replacing any prior value for the key.</summary>
	public void Set(string key, string value)
	{
		_overrides = new Dictionary<string, string>(_overrides, StringComparer.Ordinal)
		{
			[key] = value,
		};
	}

	/// <summary>Drops an override so the preference resolves to its default again.</summary>
	public void Remove(string key)
	{
		if (!_overrides.ContainsKey(key))
		{
			return;
		}

		var next = new Dictionary<string, string>(_overrides, StringComparer.Ordinal);
		next.Remove(key);
		_overrides = next;
	}
}
