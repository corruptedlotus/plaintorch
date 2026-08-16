using Pleiades.Plaintorch.Api.Contracts;

namespace Pleiades.Plaintorch.Api.Abstractions;

/// <summary>
/// The media-facing PLAINTORCH application API (PEP105). It owns one concern: storing and listing media assets in
/// the vault. Assigning a stored key to a field — a directive icon or banner, a timeframe icon — is the separate
/// concern of that field's own domain API, so an upload is never coupled to the thing that will reference it.
/// </summary>
public interface IMediaApi
{
	/// <summary>Stores an upload in the shared vault-level asset folder, returning its <c>vault:</c> key.</summary>
	Task<MediaStoreResult> UploadVaultAsync(MediaUpload upload, CancellationToken cancellationToken = default);

	/// <summary>Lists the image file names in the shared vault-level asset folder.</summary>
	Task<IReadOnlyList<string>> ListVaultAssetsAsync(CancellationToken cancellationToken = default);

	/// <summary>Stores an upload in an entity's own asset folder, returning its <c>media:</c> key.</summary>
	Task<MediaStoreResult> UploadEntityAsync(string entityType, string entityId, MediaUpload upload, CancellationToken cancellationToken = default);

	/// <summary>Lists the image file names in an entity's own asset folder.</summary>
	Task<IReadOnlyList<string>> ListEntityAssetsAsync(string entityType, string entityId, CancellationToken cancellationToken = default);
}
