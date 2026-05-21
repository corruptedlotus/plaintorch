import type { PlaintorchCoreClient } from "../coreClient"
import type {
	Executive,
	ExecutiveUpdate,
	PolarisCycle,
	PolarisCyclePlanRequest,
	PolarisCycleTimeRequest,
	PolarisExecutivePlan,
	PolarisExecutivePlanResult,
	Reflective,
	ReflectiveDrawRequest,
	ReflectiveUpdate
} from "./contracts"
export class PlaintorchPolarisSdk {
	public constructor(private readonly client: PlaintorchCoreClient) { }

	public async getCurrent(): Promise<PolarisCycle | undefined> {
		return await this.client.getJson<PolarisCycle>("/api/polaris/current")
	}

	public async get(polarisCycleId: string): Promise<PolarisCycle | undefined> {
		return await this.client.getJson<PolarisCycle>(`/api/polaris/${encodeURIComponent(polarisCycleId)}`)
	}

	public async listForecasts(): Promise<PolarisCycle[]> {
		return (await this.client.getJson<PolarisCycle[]>("/api/polaris/forecasts")) ?? []
	}

	public async plan(request: PolarisCyclePlanRequest): Promise<PolarisCycle | undefined> {
		return await this.client.postForJson<PolarisCycle>("/api/polaris/plan", request)
	}

	public async begin(time: string | undefined = undefined): Promise<PolarisCycle | undefined> {
		const request: PolarisCycleTimeRequest = { time }
		return await this.client.postForJson<PolarisCycle>("/api/polaris/current/begin", request)
	}

	public async beginCycle(polarisCycleId: string, time: string | undefined = undefined): Promise<PolarisCycle | undefined> {
		const request: PolarisCycleTimeRequest = { time }
		return await this.client.postForJson<PolarisCycle>(
			`/api/polaris/${encodeURIComponent(polarisCycleId)}/begin`,
			request
		)
	}

	public async startNew(time: string | undefined = undefined): Promise<PolarisCycle | undefined> {
		const request: PolarisCycleTimeRequest = { time }
		return await this.client.postForJson<PolarisCycle>("/api/polaris/start-new", request)
	}

	public async end(time: string | undefined = undefined): Promise<PolarisCycle | undefined> {
		const request: PolarisCycleTimeRequest = { time }
		return await this.client.postForJson<PolarisCycle>("/api/polaris/current/end", request)
	}

	public async endCycle(polarisCycleId: string, time: string | undefined = undefined): Promise<PolarisCycle | undefined> {
		const request: PolarisCycleTimeRequest = { time }
		return await this.client.postForJson<PolarisCycle>(
			`/api/polaris/${encodeURIComponent(polarisCycleId)}/end`,
			request
		)
	}

	public async planExecutive(plan: PolarisExecutivePlan): Promise<PolarisExecutivePlanResult | undefined> {
		return await this.client.postForJson<PolarisExecutivePlanResult>("/api/polaris/current/executives/plan", plan)
	}

	public async planExecutiveForCycle(
		polarisCycleId: string,
		plan: PolarisExecutivePlan
	): Promise<PolarisExecutivePlanResult | undefined> {
		return await this.client.postForJson<PolarisExecutivePlanResult>(
			`/api/polaris/${encodeURIComponent(polarisCycleId)}/executives/plan`,
			plan
		)
	}

	public async addObjectiveToCurrent(objectiveId: string): Promise<boolean> {
		return (
			(await this.client.postForJson<PolarisExecutivePlanResult>("/api/polaris/current/executives/plan", {
				mode: 3,
				objectiveId
			})) !== undefined
		)
	}

	public async drawReflectives(request: ReflectiveDrawRequest = {}): Promise<Reflective[]> {
		return (await this.client.postForJson<Reflective[]>("/api/polaris/current/reflectives/draw", request)) ?? []
	}

	public async drawReflectivesForCycle(
		polarisCycleId: string,
		request: ReflectiveDrawRequest = {}
	): Promise<Reflective[]> {
		return (
			(await this.client.postForJson<Reflective[]>(
				`/api/polaris/${encodeURIComponent(polarisCycleId)}/reflectives/draw`,
				request
			)) ?? []
		)
	}

	public async updateExecutive(executiveId: number, update: ExecutiveUpdate): Promise<Executive | undefined> {
		return await this.client.putForJson<Executive>(`/api/executives/${executiveId}`, update)
	}

	public async updateReflective(reflectiveId: number, update: ReflectiveUpdate): Promise<Reflective | undefined> {
		return await this.client.putForJson<Reflective>(`/api/reflectives/${reflectiveId}`, update)
	}
}