namespace Pleiades.Plaintorch.Preferences;

/// <summary>
/// Vault-bound preferences for agenda and occurrence materialization (PEP116). Resolved through .NET Options —
/// inject <see cref="Microsoft.Extensions.Options.IOptionsSnapshot{T}"/>.
/// </summary>
public sealed class AgendaPreferences
{
	/// <summary>
	/// Whether an opted-out eventive is materialized automatically as time passes. Defaults to
	/// <see langword="false"/>: an OptOut occurrence is hardened only by a user opting it back in, never by
	/// time-passage alone. Setting it <see langword="true"/> hardens it like any other occurrence, keeping its
	/// OptOut resolution.
	/// </summary>
	public bool AutoMaterialiseOptOut { get; set; }
}
