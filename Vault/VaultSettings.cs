using System.Text.Json;

namespace Pleiades.Vault;

/// <summary>
/// Represents the persisted PLAINTORCH settings stored inside a vault.
/// </summary>
public sealed class VaultSettings
{
    /// <summary>
    /// Gets or sets the logical location key mapping.
    /// </summary>
    public Dictionary<string, string> LocationKeys { get; init; } = CreateDefaultLocationKeys();

    /// <summary>
    /// Loads settings from a vault settings file, applying defaults for missing keys.
    /// </summary>
    /// <param name="settingsPath">The vault settings file path.</param>
    /// <returns>The loaded settings.</returns>
    public static VaultSettings Load(string settingsPath)
    {
        if (!File.Exists(settingsPath))
        {
            return new VaultSettings();
        }

        try
        {
            var json = File.ReadAllText(settingsPath);
            var settings = JsonSerializer.Deserialize<VaultSettings>(json) ?? new VaultSettings();
            return settings.WithDefaults();
        }
        catch
        {
            return new VaultSettings();
        }
    }

    /// <summary>
    /// Saves the current settings to a vault settings file.
    /// </summary>
    /// <param name="settingsPath">The vault settings file path.</param>
    public void Save(string settingsPath)
    {
        var directory = Path.GetDirectoryName(settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true,
        });

        File.WriteAllText(settingsPath, json + Environment.NewLine);
    }

    /// <summary>
    /// Resolves a configured directory name for a logical location key.
    /// </summary>
    /// <param name="locationKey">The logical location key.</param>
    /// <returns>The configured directory name.</returns>
    public string ResolveLocation(string locationKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locationKey);
        return LocationKeys.TryGetValue(locationKey, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : locationKey;
    }

    private VaultSettings WithDefaults()
    {
        var defaults = CreateDefaultLocationKeys();
        foreach (var pair in defaults)
        {
            if (!LocationKeys.ContainsKey(pair.Key) || string.IsNullOrWhiteSpace(LocationKeys[pair.Key]))
            {
                LocationKeys[pair.Key] = pair.Value;
            }
        }

        return this;
    }

    private static Dictionary<string, string> CreateDefaultLocationKeys()
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [VaultLocationKeys.Directives] = "Directives",
            [VaultLocationKeys.Onrush] = "Onrush",
            [VaultLocationKeys.Objectives] = "Objectives",
            [VaultLocationKeys.Journal] = "Journal",
            [VaultLocationKeys.Saga] = "Saga",
        };
    }
}
