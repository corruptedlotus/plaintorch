using Pleiades.Orchestration;

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
		var reflection = new ReflectionPreferences();
		var calDav = new CalDavPreferences();

		Descriptors =
		[
			new PreferenceDescriptor
			{
				Key = PreferenceKeys.NoteQueueTimeout,
				Label = "Note Queue Timeout (ms)",
				Group = "Watcher",
				Description = "How long to wait for a new note before giving up.",
				Kind = PreferenceKind.Integer,
				DefaultRaw = UserPreferenceSerializer.Serialize(watcher.NoteQueueTimeout),
			},
			new PreferenceDescriptor
			{
				Key = PreferenceKeys.AutoMaterialiseOptOut,
				Label = "Auto-materialise Opted-out Occurrences",
				Group = "Agenda",
				Description = "Materialise an opted-out eventive as time passes, instead of only when it's opted back in.",
				Kind = PreferenceKind.Boolean,
				DefaultRaw = UserPreferenceSerializer.Serialize(agenda.AutoMaterialiseOptOut),
			},
			new PreferenceDescriptor
			{
				Key = PreferenceKeys.DefaultCalendar,
				Label = "Default Calendar",
				Group = "Agenda",
				Description = "The default calendar used by Orbit calculation and date display. (This can be overriden by some items)",
				Kind = PreferenceKind.Enum,
				DefaultRaw = UserPreferenceSerializer.Serialize(agenda.DefaultCalendar),
				Options = Enum.GetNames<DeclarativeCalendar>(),
			},
			new PreferenceDescriptor
			{
				Key = PreferenceKeys.BaseDailyReflectives,
				Label = "Daily Chaos Reflectives",
				Group = "Lunar Reflection",
				Description = "How many chaos reflection each Polaris cycle draws at random.",
				Kind = PreferenceKind.Integer,
				DefaultRaw = UserPreferenceSerializer.Serialize(reflection.BaseDailyReflectives),
			},
			new PreferenceDescriptor
			{
				Key = PreferenceKeys.ReflectionDailyReward,
				Label = "Daily Reflection Reward (Celestron)",
				Group = "Lunar Reflection",
				Description = "The Celestron granted once every reflective of a Polaris cycle is done.",
				Kind = PreferenceKind.Integer,
				DefaultRaw = UserPreferenceSerializer.Serialize(reflection.DailyReward),
			},
			new PreferenceDescriptor
			{
				Key = PreferenceKeys.CalDavFloatingRender,
				Label = "CalDAV Floating-window Rendering",
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
