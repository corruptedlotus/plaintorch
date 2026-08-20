import type { PlaintorchCoreClient } from "../coreClient"
import type { MediaStoreResult } from "./models"

const base64Alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/'

/**
 * Encodes bytes as standard Base64 without depending on `btoa` or `Buffer`, so the SDK stays environment agnostic
 * and safe for large media payloads (PEP105).
 */
function encodeBase64(bytes: Uint8Array): string {
	let result = ''
	for (let index = 0; index < bytes.length; index += 3) {
		const byte0 = bytes[index] ?? 0
		const byte1 = bytes[index + 1] ?? 0
		const byte2 = bytes[index + 2] ?? 0
		result += base64Alphabet[byte0 >> 2]
		result += base64Alphabet[((byte0 & 0x03) << 4) | (byte1 >> 4)]
		result += index + 1 < bytes.length ? base64Alphabet[((byte1 & 0x0f) << 2) | (byte2 >> 6)] : '='
		result += index + 2 < bytes.length ? base64Alphabet[byte2 & 0x3f] : '='
	}

	return result
}

/**
 * The media domain (PEP105): it stores and lists media assets and hands back a key, and it owns nothing about the
 * fields — a directive icon or banner, a timeframe icon — that later reference that key. Selecting a stored key
 * onto a field is the separate concern of that field's own domain SDK, keeping upload apart from selection.
 */
export class PlaintorchMediaSdk {
	public constructor(private readonly client: PlaintorchCoreClient) { }

	/** Uploads an image to the shared vault-level asset folder, returning its stored `vault:` key. */
	public async uploadVault(fileName: string, bytes: Uint8Array): Promise<string | undefined> {
		const result = await this.client.postForJson<MediaStoreResult>("/api/media/vault", {
			fileName,
			contentBase64: encodeBase64(bytes)
		})
		return result?.key
	}

	/** Lists the image file names in the shared vault-level asset folder. */
	public async listVault(): Promise<string[]> {
		return (await this.client.getJson<string[]>("/api/media/vault")) ?? []
	}

	/** Uploads an image to an entity's own asset folder, returning its stored `media:` key. */
	public async uploadEntity(entityType: string, entityId: string, fileName: string, bytes: Uint8Array): Promise<string | undefined> {
		const result = await this.client.postForJson<MediaStoreResult>(
			`/api/media/entity/${encodeURIComponent(entityType)}/${encodeURIComponent(entityId)}`,
			{ fileName, contentBase64: encodeBase64(bytes) }
		)
		return result?.key
	}

	/** Lists the image file names in an entity's own asset folder. */
	public async listEntity(entityType: string, entityId: string): Promise<string[]> {
		return (await this.client.getJson<string[]>(
			`/api/media/entity/${encodeURIComponent(entityType)}/${encodeURIComponent(entityId)}`
		)) ?? []
	}
}
