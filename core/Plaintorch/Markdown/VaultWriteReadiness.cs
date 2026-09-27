namespace Pleiades.Plaintorch.Markdown;

/// <summary>
/// A per-request signal recording whether any vault write in the current request is still pending — its file did not
/// land within <see cref="Pleiades.Plaintorch.Preferences.WatcherPreferences.NoteQueueTimeout"/> and is finishing in
/// the background (PEP110 Refactor BETA). The <c>noteReady</c> endpoint filter reads this after the endpoint runs and
/// tells the client, so a rename-then-open / create-then-open flow can wait for the note instead of opening a file
/// that is not on disk yet. Scoped, so it is one instance per request.
/// </summary>
public sealed class VaultWriteReadiness
{
	/// <summary>Gets whether a vault write in this request is still draining in the background.</summary>
	public bool IsPending { get; private set; }

	/// <summary>Marks that a vault write in this request did not finish within the timeout and is pending.</summary>
	public void MarkPending() => IsPending = true;
}
