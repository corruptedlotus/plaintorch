namespace Pleiades.Plaintorch.Preferences;

/// <summary>
/// Vault-bound preferences for the watcher / note-sync pipeline (PEP116), resolved through .NET Options.
/// </summary>
/// <remarks>
/// Inject <see cref="Microsoft.Extensions.Options.IOptionsSnapshot{T}"/> for live, per-scope values (an
/// <see cref="Microsoft.Extensions.Options.IOptions{T}"/> is computed once and would not reflect a runtime
/// change). The property initializers are the code-owned defaults; a value appears in the database only when the
/// user has overridden it, so adding a new watcher preference is a matter of adding a property here (plus its
/// <see cref="PreferenceKeys"/> and binding) — no migration.
/// </remarks>
public sealed class WatcherPreferences
{
	/// <summary>
	/// The number of milliseconds a note may wait in the watcher's write queue before it is drained. Defaults
	/// to 2000.
	/// </summary>
	public int NoteQueueTimeout { get; set; } = 2000;
}
