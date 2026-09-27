namespace Pleiades.Plaintorch.Preferences;

/// <summary>
/// How a floating occurrence — one whose granularity window is wider than its duration, e.g. "2h somewhere on
/// Sunday" — is projected when exported to CalDAV, which has no representation for an unslotted intraday window.
/// </summary>
public enum CalDavFloatingRender
{
	/// <summary>Export as an all-day event, dropping the intraday slot (no fabricated start time).</summary>
	AllDay,

	/// <summary>Export as a timed event pinned to the start of the granularity window.</summary>
	PinToStart,
}

/// <summary>
/// Vault-bound preferences for CalDAV export (PEP116; consumed by the CalDAV integration, PEP111). Resolved
/// through .NET Options — inject <see cref="Microsoft.Extensions.Options.IOptionsSnapshot{T}"/>.
/// </summary>
public sealed class CalDavPreferences
{
	/// <summary>
	/// How a floating occurrence is rendered on export. Defaults to <see cref="CalDavFloatingRender.AllDay"/>,
	/// which never invents a start time.
	/// </summary>
	public CalDavFloatingRender FloatingRender { get; set; } = CalDavFloatingRender.AllDay;
}
