namespace Pleiades.Plaintorch.Preferences;

/// <summary>
/// The stable string keys under which vault-bound user preferences are stored (PEP116). A key is the
/// storage identity of a preference and must never change once shipped; renaming a preference means
/// keeping the old key readable (a lazy read-time migration), not editing the constant.
/// </summary>
public static class PreferenceKeys
{
	/// <summary>Backs <see cref="WatcherPreferences.NoteQueueTimeout"/>.</summary>
	public const string NoteQueueTimeout = "watcher.note-queue-timeout";

	/// <summary>Backs <see cref="AgendaPreferences.AutoMaterialiseOptOut"/>.</summary>
	public const string AutoMaterialiseOptOut = "agenda.auto-materialise-optout";

	/// <summary>Backs <see cref="AgendaPreferences.DefaultCalendar"/>.</summary>
	public const string DefaultCalendar = "agenda.default-calendar";

	/// <summary>Backs <see cref="CalDavPreferences.FloatingRender"/>.</summary>
	public const string CalDavFloatingRender = "caldav.floating-render";
}
