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
/// <param name="Sweeping">
/// Whether a startup sweep (the watcher's initial discovery/reconciliation pass) is still running. It is orthogonal
/// to <paramref name="Phase"/>: the core reaches <see cref="PlaintorchHostPhase.Active"/> — the API serves — while the
/// sweep continues in the background, so a status surface can keep a splash up and show the sweep is still going.
/// </param>
public sealed record PlaintorchHostStatus(
	PlaintorchHostPhase Phase,
	string? Message,
	string? VaultPath,
	DateTimeOffset At,
	bool Sweeping = false);

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
	/// Records a phase transition and notifies listeners. A transition clears the sweep flag unless
	/// <paramref name="sweeping"/> keeps it set (the coordinator sets it as it hands off to <see cref="PlaintorchHostPhase.Active"/>).
	/// </summary>
	/// <param name="phase">The new phase.</param>
	/// <param name="message">A human-readable line describing the phase.</param>
	/// <param name="vaultPath">The vault the phase concerns, when any.</param>
	/// <param name="sweeping">Whether a startup sweep is in progress; defaults to not sweeping.</param>
	public void Report(PlaintorchHostPhase phase, string? message = null, string? vaultPath = null, bool sweeping = false)
	{
		PlaintorchHostStatus snapshot;
		lock (_gate)
		{
			snapshot = new PlaintorchHostStatus(phase, message, vaultPath, DateTimeOffset.UtcNow, sweeping);
			_current = snapshot;
		}

		Changed?.Invoke(snapshot);
	}

	/// <summary>
	/// Ends the startup sweep: keeps the current phase, vault, and message, but clears the sweep flag so a status
	/// surface can stop holding a splash. A no-op when no sweep is in progress, so every watcher exit branch (and the
	/// coordinator's backstop) can call it unconditionally.
	/// </summary>
	/// <param name="message">An optional message to set as the sweep ends (for example, "Serving vault.").</param>
	public void EndSweep(string? message = null)
	{
		PlaintorchHostStatus snapshot;
		lock (_gate)
		{
			if (!_current.Sweeping)
			{
				return;
			}

			snapshot = _current with { Sweeping = false, Message = message ?? _current.Message, At = DateTimeOffset.UtcNow };
			_current = snapshot;
		}

		Changed?.Invoke(snapshot);
	}
}
