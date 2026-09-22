namespace Pleiades.Plaintorch.Preferences;

/// <summary>
/// The registry of user preferences (PEP116) — the single list the settings surface enumerates. Each entry's
/// default is read from its Options POCO, so the POCO stays the one source of the default. Adding a preference is
/// a matter of adding its property, its <see cref="PreferenceKeys"/> constant and binding, and one entry here.
/// </summary>
public sealed class PreferenceCatalog
{
	/// <summary>Every known preference, in display order.</summary>
	public IReadOnlyList<PreferenceDescriptor> Descriptors { get; }

	/// <summary>Builds the catalog, taking each default from a fresh Options POCO instance.</summary>
	public PreferenceCatalog()
	{
		var watcher = new WatcherPreferences();
		var agenda = new AgendaPreferences();
		var calDav = new CalDavPreferences();

		Descriptors =
		[
			new PreferenceDescriptor
			{
				Key = PreferenceKeys.NoteQueueTimeout,
				Label = "Note queue timeout (ms)",
				Group = "Watcher",
				Description = "How long a note may wait in the watcher's write queue before it is drained.",
				Kind = PreferenceKind.Integer,
				DefaultRaw = UserPreferenceSerializer.Serialize(watcher.NoteQueueTimeout),
			},
			new PreferenceDescriptor
			{
				Key = PreferenceKeys.AutoMaterialiseOptOut,
				Label = "Auto-materialise opted-out occurrences",
				Group = "Agenda",
				Description = "Materialise an opted-out eventive as time passes, instead of only when it is opted back in.",
				Kind = PreferenceKind.Boolean,
				DefaultRaw = UserPreferenceSerializer.Serialize(agenda.AutoMaterialiseOptOut),
			},
			new PreferenceDescriptor
			{
				Key = PreferenceKeys.CalDavFloatingRender,
				Label = "CalDAV floating-window rendering",
				Group = "CalDAV",
				Description = "How a floating occurrence is exported to CalDAV, which cannot represent an unslotted intraday window.",
				Kind = PreferenceKind.Enum,
				DefaultRaw = UserPreferenceSerializer.Serialize(calDav.FloatingRender),
				Options = Enum.GetNames<CalDavFloatingRender>(),
			},
		];
	}

	/// <summary>Finds a descriptor by key, or <see langword="null"/> when the key is not a known preference.</summary>
	public PreferenceDescriptor? Find(string key) => Descriptors.FirstOrDefault(descriptor => descriptor.Key == key);
}
