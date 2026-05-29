namespace Pleiades.Vault.Watcher;

/// <summary>
/// Represents watcher runtime health as surfaced to the core system status.
/// </summary>
public enum VaultWatcherHealthStatus
{
	/// <summary>
	/// Watcher is processing normally.
	/// </summary>
	Ok,

	/// <summary>
	/// Watcher has active issues that may impact data integrity.
	/// </summary>
	Issues,

	/// <summary>
	/// Watcher is running in standby due to a blocking operational condition.
	/// </summary>
	Standby,

	/// <summary>
	/// Watcher has crashed or stopped and is not online.
	/// </summary>
	Offline,
}
