/** An uploaded media file (PEP105); the bytes travel as Base64 because the transport carries JSON only. */
export interface MediaUpload {
	fileName: string
	contentBase64: string
}

/**
 * The stored key a media-domain upload returns (PEP105): a `media:` file (entity-level) or a `vault:` file
 * (shared). A field references this key through its own domain's set-reference call — upload and selection stay
 * separate.
 */
export interface MediaStoreResult {
	key: string
}
