using System.Text.Json;
using Pleiades.Plaintorch.Api.Contracts;

namespace Pleiades.Plaintorch.Api.Abstractions;

/// <summary>The vault-bound user-preferences API (PEP116): enumerate, set, and reset preferences.</summary>
public interface IPreferenceApi
{
	/// <summary>Lists every known preference with its current resolved value and its default.</summary>
	Task<IReadOnlyList<PreferenceView>> ListAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Sets a preference. Returns <see langword="null"/> when the key is not a known preference; throws
	/// <see cref="Preferences.PreferenceValidationException"/> when the value does not match the preference's kind.
	/// </summary>
	Task<PreferenceView?> SetAsync(string key, JsonElement value, CancellationToken cancellationToken = default);

	/// <summary>Resets a preference to its default (removing any stored override). Null when the key is unknown.</summary>
	Task<PreferenceView?> ResetAsync(string key, CancellationToken cancellationToken = default);
}
