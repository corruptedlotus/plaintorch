using System.Text.Json;
using Pleiades.Vault;

namespace Pleiades.Plaintorch;

/// <summary>
/// Manages the active service lock file inside the vault root.
/// </summary>
public sealed class PlaintorchVaultLockService(
	VaultLayout layout,
	PlaintorchUserLayout userLayout)
{
	/// <summary>
	/// Acquires the active vault lock and writes owner metadata into the lock file.
	/// </summary>
	public async Task<PlaintorchVaultLockHandle> AcquireAsync(CancellationToken cancellationToken = default)
	{
		Directory.CreateDirectory(layout.VaultRoot);

		FileStream stream;
		try
		{
			stream = new FileStream(
				layout.LockPath,
				FileMode.OpenOrCreate,
				FileAccess.ReadWrite,
				FileShare.None,
				bufferSize: 4096,
				FileOptions.Asynchronous);
		}
		catch (IOException exception)
		{
			throw new InvalidOperationException($"Vault '{layout.VaultRoot}' is already owned by another PLAINTORCH service instance.", exception);
		}

		var metadata = new PlaintorchVaultLockMetadata(
			Environment.UserName,
			Environment.MachineName,
			Environment.ProcessId,
			DateTimeOffset.UtcNow,
			userLayout.SocketPath);

		stream.SetLength(0);
		await JsonSerializer.SerializeAsync(stream, metadata, cancellationToken: cancellationToken);
		await stream.FlushAsync(cancellationToken);
		stream.Position = 0;
		return new PlaintorchVaultLockHandle(layout.LockPath, stream);
	}
}

/// <summary>
/// Represents the vault lock owner metadata written into the lock file.
/// </summary>
public sealed record PlaintorchVaultLockMetadata(
	string UserName,
	string MachineName,
	int ProcessId,
	DateTimeOffset AcquiredUtc,
	string SocketPath);

/// <summary>
/// Holds the active vault lock until the service stops.
/// </summary>
public sealed class PlaintorchVaultLockHandle(string path, FileStream stream) : IAsyncDisposable
{
	private bool _disposed;

	/// <inheritdoc />
	public async ValueTask DisposeAsync()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		await stream.DisposeAsync();
		if (File.Exists(path))
		{
			File.Delete(path);
		}
	}
}