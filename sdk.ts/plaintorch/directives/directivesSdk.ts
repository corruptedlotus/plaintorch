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
	TimeframeUpdate
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
