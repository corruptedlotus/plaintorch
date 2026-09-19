namespace Pleiades.Plaintorch;

/// <summary>
/// Tracks whether the hosted core is currently serving a vault and coordinates activation readiness
/// between the activation coordinator and vault-scoped background services.
/// </summary>
/// <remarks>
/// The core runs in daemon mode: it starts idle and only serves a vault once one is activated through
/// user settings. A "ready" session means the coordinator has bound the vault layout, acquired the vault
/// lock, and initialized vault state, so background services (such as the watcher) may safely operate.
/// Each activation is assigned a monotonically increasing generation number and exposes a
/// <see cref="CancellationToken"/> that fires when the vault is deactivated, letting long-running
/// vault-scoped loops tear down and return to idle. Consumers track the generation they last served so a
/// session is never re-entered, which keeps the serving loop from spinning if its work returns early.
/// </remarks>
public sealed class ActiveVaultSession
{
	private readonly object _gate = new();
	private CancellationTokenSource? _sessionCts;
	private TaskCompletionSource _changed = CreateSignal();
	private long _generation;
	private string? _activeVaultPath;

	/// <summary>
	/// Gets a value indicating whether a vault is currently activated and ready to be served.
	/// </summary>
	public bool IsActive
	{
		get
		{
			lock (_gate)
			{
				return _sessionCts is not null;
			}
		}
	}

	/// <summary>
	/// Gets the path of the vault currently being served, or <see langword="null"/> when idle.
	/// </summary>
	public string? ActiveVaultPath
	{
		get
		{
			lock (_gate)
			{
				return _activeVaultPath;
			}
		}
	}

	/// <summary>
	/// Gets the current session generation — a monotonically increasing number bumped on each activation. Cheap
	/// to key a per-vault cache on: a vault switch moves it on, so anything cached against it recomputes.
	/// </summary>
	public long Generation
	{
		get
		{
			lock (_gate)
			{
				return _generation;
			}
		}
	}

	/// <summary>
	/// Marks the session as ready to serve the specified vault, beginning a new session generation.
	/// </summary>
	/// <param name="vaultPath">The absolute path of the activated vault.</param>
	public void MarkReady(string vaultPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(vaultPath);
		lock (_gate)
		{
			CancelCurrentSession();
			_sessionCts = new CancellationTokenSource();
			_generation++;
			_activeVaultPath = vaultPath;
			Signal();
		}
	}

	/// <summary>
	/// Marks the session idle, cancelling the current session token so vault-scoped work stops.
	/// </summary>
	public void MarkIdle()
	{
		lock (_gate)
		{
			if (_sessionCts is null)
			{
				return;
			}

			CancelCurrentSession();
			_activeVaultPath = null;
			Signal();
		}
	}

	/// <summary>
	/// Waits for a vault session whose generation is newer than <paramref name="afterGeneration"/>.
	/// </summary>
	/// <param name="afterGeneration">The last generation the caller has already served; pass <c>0</c> initially.</param>
	/// <param name="stoppingToken">A token used to abandon waiting when the host is shutting down.</param>
	/// <returns>The new session's generation and a cancellation token scoped to that session's lifetime.</returns>
	public async Task<(long Generation, CancellationToken Token)> WaitForSessionAsync(long afterGeneration, CancellationToken stoppingToken)
	{
		while (true)
		{
			Task changed;
			lock (_gate)
			{
				if (_sessionCts is not null && _generation > afterGeneration && !_sessionCts.IsCancellationRequested)
				{
					return (_generation, _sessionCts.Token);
				}

				changed = _changed.Task;
			}

			await changed.WaitAsync(stoppingToken);
		}
	}

	private void CancelCurrentSession()
	{
		if (_sessionCts is null)
		{
			return;
		}

		try
		{
			_sessionCts.Cancel();
		}
		finally
		{
			_sessionCts.Dispose();
			_sessionCts = null;
		}
	}

	private void Signal()
	{
		var previous = _changed;
		_changed = CreateSignal();
		previous.TrySetResult();
	}

	private static TaskCompletionSource CreateSignal()
	{
		return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
	}
}
