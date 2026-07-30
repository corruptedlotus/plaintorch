namespace Pleiades.Plaintorch.Api.Changes;

/// <summary>
/// Marks the work currently in progress as a source a client must not disregard.
/// </summary>
/// <remarks>
/// A client treats an ordinary change announcement as advisory: an edit the user is making outranks a
/// refresh that was already in flight, so a late-arriving sync is discarded rather than allowed to revert
/// them. Some changes cannot be treated that way — the vault reconciling a hand-edited markdown file is the
/// authority on that entity, and a purge removes it outright — so those are announced as critical and the
/// client applies them regardless.
///
/// Carried ambiently rather than as a parameter because the announcement is made by a save-hook interceptor
/// several layers below whatever decided the work was authoritative.
/// </remarks>
public static class PlaintorchChangeOrigin
{
	private static readonly AsyncLocal<bool> IsCriticalScope = new();

	/// <summary>
	/// Gets whether the work in progress on this asynchronous flow is authoritative.
	/// </summary>
	public static bool IsCritical => IsCriticalScope.Value;

	/// <summary>
	/// Marks everything saved within the returned scope as authoritative.
	/// </summary>
	/// <returns>A scope that restores the previous state when disposed.</returns>
	public static IDisposable Critical()
	{
		var previous = IsCriticalScope.Value;
		IsCriticalScope.Value = true;
		return new Scope(previous);
	}

	private sealed class Scope(bool previous) : IDisposable
	{
		private bool _disposed;

		public void Dispose()
		{
			if (!_disposed)
			{
				_disposed = true;
				IsCriticalScope.Value = previous;
			}
		}
	}
}
