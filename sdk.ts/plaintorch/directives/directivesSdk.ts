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
	DirectiveBannerRequest,
	DirectiveAvailabilityRequest
} from "./contracts"

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

	/** Selects or clears a directive's icon (PEP105): a raw reference key, or cleared. Uploading is `core.media`'s job. */
	public async setIcon(directiveId: string, request: DirectiveIconRequest): Promise<Directive | undefined> {
		return await this.client.putForJson<Directive>(`/api/directives/${encodeURIComponent(directiveId)}/icon`, request)
	}

	/** Selects or clears a directive's banner image (PEP105). */
	public async setBanner(directiveId: string, request: DirectiveBannerRequest): Promise<Directive | undefined> {
		return await this.client.putForJson<Directive>(`/api/directives/${encodeURIComponent(directiveId)}/banner`, request)
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

	/** Sets a directive's banner to a raw key: a `media:` file or a `vault:` file (PEP105). */
	public async setBannerReference(directiveId: string, reference: string): Promise<Directive | undefined> {
		return await this.setBanner(directiveId, { reference })
	}

	/** Clears a directive's banner image (PEP105). */
	public async clearBanner(directiveId: string): Promise<Directive | undefined> {
		return await this.setBanner(directiveId, { clear: true })
	}

	/**
	 * Sets a directive's availability to an Availability-mode timeframe, or clears it with `null` (PEP100 patch 2).
	 * Kind-agnostic: stellar and lunar directives alike. Executives created under the directive or its descendants are
	 * auto-assigned to the nearest availability, ahead of college auto-inclusion.
	 */
	public async setAvailability(directiveId: string, timeframeId: number | null): Promise<Directive | undefined> {
		const request: DirectiveAvailabilityRequest = { timeframeId }
		return await this.client.putForJson<Directive>(`/api/directives/${encodeURIComponent(directiveId)}/availability`, request)
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

	/**
	 * Lists the timeframes active right now (PEP100 patch 2). The core owns the rule: it narrows the strictly active
	 * Polaris cycle's cached candidates (orbit matched on the cycle day) to those whose window holds the current
	 * minute, and when any of those is exclusive only the exclusive ones remain. Empty when no cycle is active.
	 */
	public async listActiveTimeframes(): Promise<DirectiveTimeframeRecord[]> {
		return (await this.client.getJson<DirectiveTimeframeRecord[]>("/api/timeframes/active")) ?? []
	}

	public async updateTimeframe(timeframeId: number, update: TimeframeUpdate): Promise<Timeframe | undefined> {
		return await this.client.putForJson<Timeframe>(`/api/timeframes/${timeframeId}`, update)
	}

	public async deleteTimeframe(timeframeId: number): Promise<boolean> {
		return await this.client.delete(`/api/timeframes/${timeframeId}`)
	}
}
