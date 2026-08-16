using Pleiades.Plaintorch.Api.Abstractions;
using Pleiades.Plaintorch.Api.Contracts;
using Pleiades.Plaintorch.Media;
using Pleiades.Vault.Database;
using Pleiades.Vault.Media;

namespace Pleiades.Plaintorch.Api.Services;

/// <summary>
/// Implements the media-facing PLAINTORCH application API (PEP105): it stores and lists media assets, and returns
/// the key a field can later reference. It never touches a field itself, keeping upload separate from selection.
/// </summary>
public sealed class MediaApiService(
	VaultMediaService mediaService,
	MediaAssetFolderResolver folderResolver,
	VaultAuditLogService auditLogService) : IMediaApi
{
	/// <inheritdoc />
	public async Task<MediaStoreResult> UploadVaultAsync(MediaUpload upload, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(upload);
		var stored = await mediaService.StoreAsync(mediaService.VaultAssetFolder, upload.FileName, DecodeUpload(upload), cancellationToken);
		var key = VaultMediaService.ToVaultReference(stored);
		await auditLogService.WriteAsync("api", "media.upload-vault", subjectType: "Media", subjectId: key, cancellationToken: cancellationToken);
		return new MediaStoreResult(key);
	}

	/// <inheritdoc />
	public Task<IReadOnlyList<string>> ListVaultAssetsAsync(CancellationToken cancellationToken = default)
	{
		return Task.FromResult(mediaService.List(mediaService.VaultAssetFolder));
	}

	/// <inheritdoc />
	public async Task<MediaStoreResult> UploadEntityAsync(string entityType, string entityId, MediaUpload upload, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
		ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
		ArgumentNullException.ThrowIfNull(upload);

		var folder = await folderResolver.ResolveAsync(entityType, entityId, cancellationToken)
			?? throw new InvalidOperationException($"Entity '{entityType}/{entityId}' was not found, so its media has nowhere to be stored.");
		var stored = await mediaService.StoreAsync(folder, upload.FileName, DecodeUpload(upload), cancellationToken);
		var key = VaultMediaService.ToSelfReference(stored);
		await auditLogService.WriteAsync(
			"api",
			"media.upload-entity",
			subjectType: "Media",
			subjectId: key,
			details: new { entityType, entityId },
			cancellationToken: cancellationToken);
		return new MediaStoreResult(key);
	}

	/// <inheritdoc />
	public async Task<IReadOnlyList<string>> ListEntityAssetsAsync(string entityType, string entityId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
		ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
		var folder = await folderResolver.ResolveAsync(entityType, entityId, cancellationToken);
		return folder is null ? [] : mediaService.List(folder);
	}

	private static byte[] DecodeUpload(MediaUpload upload)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(upload.ContentBase64);
		try
		{
			return Convert.FromBase64String(upload.ContentBase64);
		}
		catch (FormatException exception)
		{
			throw new InvalidOperationException("Media upload content is not valid Base64.", exception);
		}
	}
}
