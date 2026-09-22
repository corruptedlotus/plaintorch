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
}
