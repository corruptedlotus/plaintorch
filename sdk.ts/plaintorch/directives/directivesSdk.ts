import type { PlaintorchCoreClient } from "../coreClient"
import type {
	CreateDirectiveRequest,
	CreateLunarDirectiveRequest,
	Directive,
	DirectiveKind,
	DirectiveTimeframeRecord,
	InitDirectiveRequest,
	StellarDirectiveUpdate,
	LunarDirectiveUpdate,
	StellarDirectiveWorkflowShift,
	DirectiveSummary,
	LunarDirectiveWorkflowShift,
	Timeframe,
	TimeframePlan,
	TimeframeUpdate,
	DirectiveIconRequest,
	DirectiveBannerRequest
} from "./contracts"

const base64Alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/'

/**
 * Encodes bytes as standard Base64 without depending on `btoa` or `Buffer`, so the SDK stays environment
 * agnostic and safe for large media payloads (PEP105).
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

export class PlaintorchDirectivesSdk {
	public constructor(private readonly client: PlaintorchCoreClient) { }

	public async get(directiveId: string): Promise<DirectiveSummary | undefined> {
		return await this.client.getJson<DirectiveSummary>(`/api/directives/${encodeURIComponent(directiveId)}`)
	}

	/** Lists directives across both kinds, optionally filtered to a single kind. */
	public async list(kind?: DirectiveKind): Promise<Directive[]> {
		const query = kind ? `?kind=${encodeURIComponent(kind)}` : ""
		return (await this.client.getJson<Directive[]>(`/api/directives${query}`)) ?? []
	}

	/** Lists stellar directives only. */
	public async listStellar(): Promise<Directive[]> {
		return (await this.client.getJson<Directive[]>("/api/directives/stellar")) ?? []
	}

	/** Lists lunar directives only. */
	public async listLunar(): Promise<Directive[]> {
		return (await this.client.getJson<Directive[]>("/api/directives/lunar")) ?? []
	}

	public async search(query: string, take = 10): Promise<DirectiveSummary[]> {
		return (await this.client.getJson<DirectiveSummary[]>(
			`/api/directives?q=${encodeURIComponent(query)}&take=${take}`
		)) ?? []
	}

	public async create(request: CreateDirectiveRequest): Promise<Directive | undefined> {
		return await this.client.postForJson<Directive>("/api/directives", request)
	}

	public async init(request: InitDirectiveRequest): Promise<Directive | undefined> {
		return await this.client.postForJson<Directive>("/api/directives/init", request)
	}

	public async updateStellar(directiveId: string, request: StellarDirectiveUpdate): Promise<Directive | undefined> {
		return await this.client.putForJson<Directive>(`/api/directives/stellar/${encodeURIComponent(directiveId)}`, request)
	}

	public async updateLunar(directiveId: string, request: LunarDirectiveUpdate): Promise<Directive | undefined> {
		return await this.client.putForJson<Directive>(`/api/directives/lunar/${encodeURIComponent(directiveId)}`, request)
	}

	public async shiftStellarWorkflow(directiveId: string, request: StellarDirectiveWorkflowShift): Promise<Directive | undefined> {
		return await this.client.postForJson<Directive>(
			`/api/directives/stellar/${encodeURIComponent(directiveId)}/workflow`,
			request
		)
	}

	public async shiftLunarWorkflow(directiveId: string, request: LunarDirectiveWorkflowShift): Promise<Directive | undefined> {
		return await this.client.postForJson<Directive>(
			`/api/directives/lunar/${encodeURIComponent(directiveId)}/workflow`,
			request
		)
	}

	public async delete(directiveId: string): Promise<boolean> {
		return await this.client.delete(`/api/directives/${encodeURIComponent(directiveId)}`)
	}

	/** Sets or clears a directive's icon (PEP105): a glyph name, an uploaded image, or cleared. */
	public async setIcon(directiveId: string, request: DirectiveIconRequest): Promise<Directive | undefined> {
		return await this.client.putForJson<Directive>(`/api/directives/${encodeURIComponent(directiveId)}/icon`, request)
	}

	/** Sets or clears a directive's banner image (PEP105). */
	public async setBanner(directiveId: string, request: DirectiveBannerRequest): Promise<Directive | undefined> {
		return await this.client.putForJson<Directive>(`/api/directives/${encodeURIComponent(directiveId)}/banner`, request)
	}

	/** Uploads a custom icon image from raw bytes (PEP105); `vault` stores it as shared vault-level media. */
	public async uploadIcon(directiveId: string, fileName: string, bytes: Uint8Array, vault = false): Promise<Directive | undefined> {
		return await this.setIcon(directiveId, { upload: { fileName, contentBase64: encodeBase64(bytes) }, vault })
	}

	/** Sets a directive's icon to a raw key: a glyph/lucide name, a `media:` file, or a `vault:` file (PEP105). */
	public async setIconReference(directiveId: string, reference: string): Promise<Directive | undefined> {
		return await this.setIcon(directiveId, { reference })
	}

	/** Sets a directive's icon to a built-in glyph or lucide name (PEP105). */
	public async setIconGlyph(directiveId: string, glyph: string): Promise<Directive | undefined> {
		return await this.setIconReference(directiveId, glyph)
	}

	/** Clears a directive's icon, falling back to the per-kind default glyph (PEP105). */
	public async clearIcon(directiveId: string): Promise<Directive | undefined> {
		return await this.setIcon(directiveId, { clear: true })
	}

	/** Uploads a banner image from raw bytes (PEP105); `vault` stores it as shared vault-level media. */
	public async uploadBanner(directiveId: string, fileName: string, bytes: Uint8Array, vault = false): Promise<Directive | undefined> {
		return await this.setBanner(directiveId, { upload: { fileName, contentBase64: encodeBase64(bytes) }, vault })
	}

	/** Sets a directive's banner to a raw key: a `media:` file or a `vault:` file (PEP105). */
	public async setBannerReference(directiveId: string, reference: string): Promise<Directive | undefined> {
		return await this.setBanner(directiveId, { reference })
	}

	/** Clears a directive's banner image (PEP105). */
	public async clearBanner(directiveId: string): Promise<Directive | undefined> {
		return await this.setBanner(directiveId, { clear: true })
	}

	public async createLunar(request: CreateLunarDirectiveRequest): Promise<Directive | undefined> {
		return await this.client.postForJson<Directive>("/api/directives/lunar", request)
	}

	/** Lists the timeframes defined by a lunar directive. */
	public async listTimeframes(lunarDirectiveId: string): Promise<Timeframe[]> {
		return (
			(await this.client.getJson<Timeframe[]>(`/api/directives/lunar/${encodeURIComponent(lunarDirectiveId)}/timeframes`)) ?? []
		)
	}

	/** Defines a timeframe under a lunar directive (PEP100). */
	public async createTimeframe(lunarDirectiveId: string, plan: TimeframePlan): Promise<Timeframe | undefined> {
		return await this.client.postForJson<Timeframe>(
			`/api/directives/lunar/${encodeURIComponent(lunarDirectiveId)}/timeframes`,
			plan
		)
	}

	/** Lists every timeframe across all lunar directives, each paired with its owning directive summary. */
	public async listAllTimeframes(): Promise<DirectiveTimeframeRecord[]> {
		return (await this.client.getJson<DirectiveTimeframeRecord[]>("/api/timeframes")) ?? []
	}

	public async updateTimeframe(timeframeId: number, update: TimeframeUpdate): Promise<Timeframe | undefined> {
		return await this.client.putForJson<Timeframe>(`/api/timeframes/${timeframeId}`, update)
	}

	public async deleteTimeframe(timeframeId: number): Promise<boolean> {
		return await this.client.delete(`/api/timeframes/${timeframeId}`)
	}
}
