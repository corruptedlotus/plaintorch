using System.Text.Json.Serialization;

namespace Pleiades.Plaintorch.Hosting;

/// <summary>
/// The coarse lifecycle phase of the hosted core, as a shell or operator sees it.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PlaintorchHostPhase>))]
public enum PlaintorchHostPhase
{
	/// <summary>The host process is up but the activation coordinator has not run yet.</summary>
	Starting,

	/// <summary>No vault is being served; the core waits for a vault to be activated in user settings.</summary>
	Idle,

	/// <summary>A vault is being locked and initialized.</summary>
	Activating,

	/// <summary>A vault is locked, initialized, and served.</summary>
	Active,

	/// <summary>The configured vault could not be activated; the core stays idle until it is reconfigured.</summary>
	Failed,

	/// <summary>The host is shutting down.</summary>
	Stopping,
}

/// <summary>
/// A point-in-time snapshot of the hosted core's phase.
/// </summary>
/// <param name="Phase">The lifecycle phase.</param>
/// <param name="Message">A human-readable line describing the phase, for a status surface.</param>
/// <param name="VaultPath">The vault the phase concerns, when any.</param>
/// <param name="At">When the snapshot was taken.</param>
public sealed record PlaintorchHostStatus(
	PlaintorchHostPhase Phase,
	string? Message,
	string? VaultPath,
	DateTimeOffset At);

/// <summary>
/// Observable host phase shared by the activation coordinator and every status surface (the health endpoint,
/// the spawn-mode stdout stream, a future in-process shell).
/// </summary>
/// <remarks>
/// This replaces the earlier splash popup, which the coordinator drove directly. The coordinator now only
/// reports here; whoever is hosting the core decides how (or whether) to present it.
/// </remarks>
public sealed class PlaintorchHostState
{
	private readonly object _gate = new();
	private PlaintorchHostStatus _current = new(PlaintorchHostPhase.Starting, "Starting PLAINTORCH core...", null, DateTimeOffset.UtcNow);

	/// <summary>
	/// Raised after every phase transition with the new snapshot. Handlers run synchronously on the reporting thread.
	/// </summary>
	public event Action<PlaintorchHostStatus>? Changed;

	/// <summary>
	/// Gets the latest snapshot.
	/// </summary>
	public PlaintorchHostStatus Current
	{
		get
		{
			lock (_gate)
			{
				return _current;
			}
		}
	}

	/// <summary>
	/// Records a phase transition and notifies listeners.
	/// </summary>
	/// <param name="phase">The new phase.</param>
	/// <param name="message">A human-readable line describing the phase.</param>
	/// <param name="vaultPath">The vault the phase concerns, when any.</param>
	public void Report(PlaintorchHostPhase phase, string? message = null, string? vaultPath = null)
	{
		PlaintorchHostStatus snapshot;
		lock (_gate)
		{
			snapshot = new PlaintorchHostStatus(phase, message, vaultPath, DateTimeOffset.UtcNow);
			_current = snapshot;
		}

		Changed?.Invoke(snapshot);
	}
}
