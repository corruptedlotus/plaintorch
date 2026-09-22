using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.Preferences;
using Pleiades.Vault.Database;

namespace Pleiades.Plaintorch.Api.Services;

/// <summary>
/// Serves the user-preferences API (PEP116) by overlaying the vault's stored overrides onto the code-owned
/// catalog: a listing is every catalog descriptor resolved against its stored value (or default), a write
/// validates against the descriptor's kind, and a reset removes the override.
/// </summary>
public sealed class PreferenceApiService(PlainfraContext context, UserPreferenceService preferences, PreferenceCatalog catalog) : IPreferenceApi
{
	/// <inheritdoc />
	public async Task<IReadOnlyList<PreferenceView>> ListAsync(CancellationToken cancellationToken = default)
	{
		var stored = await context.UserPreferences.AsNoTracking()
			.ToDictionaryAsync(row => row.Key, row => row.Value, cancellationToken);
		return catalog.Descriptors
			.Select(descriptor => ToView(descriptor, stored.GetValueOrDefault(descriptor.Key)))
			.ToList();
	}

	/// <inheritdoc />
	public async Task<PreferenceView?> SetAsync(string key, JsonElement value, CancellationToken cancellationToken = default)
	{
		var descriptor = catalog.Find(key);
		if (descriptor is null)
		{
			return null;
		}

		if (!descriptor.Accepts(value))
		{
			throw new PreferenceValidationException($"'{value.GetRawText()}' is not a valid {descriptor.Kind} value for preference '{key}'.");
		}

		var raw = value.GetRawText();
		await preferences.SetRawAsync(key, raw, cancellationToken);
		return ToView(descriptor, raw);
	}

	/// <inheritdoc />
	public async Task<PreferenceView?> ResetAsync(string key, CancellationToken cancellationToken = default)
	{
		var descriptor = catalog.Find(key);
		if (descriptor is null)
		{
			return null;
		}

		await preferences.ResetAsync(key, cancellationToken);
		return ToView(descriptor, null);
	}

	private static PreferenceView ToView(PreferenceDescriptor descriptor, string? storedRaw) => new(
		descriptor.Key,
		descriptor.Label,
		descriptor.Group,
		descriptor.Description,
		descriptor.Kind.ToString(),
		Parse(storedRaw ?? descriptor.DefaultRaw),
		Parse(descriptor.DefaultRaw),
		descriptor.Options);

	private static JsonElement Parse(string raw) => JsonDocument.Parse(raw).RootElement.Clone();
}
