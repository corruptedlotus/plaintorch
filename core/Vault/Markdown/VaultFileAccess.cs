namespace Pleiades.Vault.Markdown;

/// <summary>
/// The kind of a vault file-access failure, classified structurally rather than by exception message text (PEP108
/// phase C).
/// </summary>
public enum VaultFileAccessKind
{
	/// <summary>The file could not be accessed because of filesystem permissions.</summary>
	PermissionDenied,

	/// <summary>The file could not be accessed because another process holds it open.</summary>
	InUse,
}

/// <summary>
/// A typed vault file-access failure (PEP108 phase C). It replaces the watcher's exception-message sniffing: the
/// failure kind is carried explicitly, classified from the exception type and OS error code, so callers switch on
/// <see cref="Kind"/> instead of matching English message fragments.
/// </summary>
public sealed class VaultFileAccessException : IOException
{
	// Windows sharing/lock-violation HResults (locale-independent, unlike the message text).
	private const int HResultSharingViolation = unchecked((int)0x80070020);
	private const int HResultLockViolation = unchecked((int)0x80070021);

	/// <summary>Initializes the exception with a classified kind and the offending path.</summary>
	public VaultFileAccessException(VaultFileAccessKind kind, string path, Exception innerException)
		: base($"Vault file access failed ({kind}) for '{path}'.", innerException)
	{
		Kind = kind;
		Path = path;
	}

	/// <summary>Gets the classified failure kind.</summary>
	public VaultFileAccessKind Kind { get; }

	/// <summary>Gets the path whose access failed.</summary>
	public string Path { get; }

	/// <summary>
	/// Classifies an exception as a vault file-access failure by type and OS error code, or returns
	/// <see langword="null"/> when it is not a recognized file-access failure. Already-typed
	/// <see cref="VaultFileAccessException"/> instances report their own kind.
	/// </summary>
	public static VaultFileAccessKind? TryClassify(Exception exception)
	{
		ArgumentNullException.ThrowIfNull(exception);
		return exception switch
		{
			VaultFileAccessException typed => typed.Kind,
			UnauthorizedAccessException => VaultFileAccessKind.PermissionDenied,
			IOException io when io.HResult == HResultSharingViolation || io.HResult == HResultLockViolation => VaultFileAccessKind.InUse,
			_ => null,
		};
	}
}

/// <summary>
/// Reads vault files, translating recognized access failures into a typed <see cref="VaultFileAccessException"/> at
/// the read boundary so the watcher can report a concrete cause without inspecting exception message text.
/// </summary>
public static class VaultFileAccess
{
	// An editor (Obsidian) typically holds a file open only for the few hundred milliseconds of its own save, so a
	// short bounded retry absorbs the common transient sharing violation before the caller has to requeue the work.
	private const int MaxInUseAttempts = 4;
	private static readonly TimeSpan InitialInUseBackoff = TimeSpan.FromMilliseconds(50);

	/// <summary>
	/// Reads a file's full text, throwing <see cref="VaultFileAccessException"/> on a classified access failure. A
	/// transient <see cref="VaultFileAccessKind.InUse"/> failure is retried a bounded number of times with a short
	/// escalating backoff; a lock that outlives the retries surfaces as a typed failure the caller can requeue.
	/// </summary>
	public static async Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
	{
		var backoff = InitialInUseBackoff;
		for (var attempt = 1; ; attempt++)
		{
			try
			{
				return await File.ReadAllTextAsync(path, cancellationToken);
			}
			catch (Exception exception) when (VaultFileAccessException.TryClassify(exception) is { } kind)
			{
				if (kind == VaultFileAccessKind.InUse
					&& attempt < MaxInUseAttempts
					&& !cancellationToken.IsCancellationRequested)
				{
					await Task.Delay(backoff, cancellationToken);
					backoff += backoff;
					continue;
				}

				throw new VaultFileAccessException(kind, path, exception);
			}
		}
	}
}
