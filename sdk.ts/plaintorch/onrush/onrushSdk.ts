import type { PlaintorchCoreClient } from "../coreClient"
import type { Objective } from "../objectives/models"
import type { OnrushSprint, OnrushSprintDateRequest, OnrushSprintPlan } from "./contracts"
export class PlaintorchOnrushSdk {
	public constructor(private readonly client: PlaintorchCoreClient) { }

	public async list(): Promise<OnrushSprint[]> {
		return (await this.client.getJson<OnrushSprint[]>("/api/onrush")) ?? []
	}

	public async getCurrent(): Promise<OnrushSprint | undefined> {
		return await this.client.getJson<OnrushSprint>("/api/onrush/current")
	}

	public async getPlanning(): Promise<OnrushSprint | undefined> {
		return await this.client.getJson<OnrushSprint>("/api/onrush/planning")
	}

	public async available(): Promise<OnrushSprint[]> {
		return (await this.client.getJson<OnrushSprint[]>("/api/onrush/available")) ?? []
	}

	public async get(onrushId: string): Promise<OnrushSprint | undefined> {
		return await this.client.getJson<OnrushSprint>(`/api/onrush/${encodeURIComponent(onrushId)}`)
	}

	public async plan(request: OnrushSprintPlan): Promise<OnrushSprint | undefined> {
		return await this.client.postForJson<OnrushSprint>("/api/onrush/plan", request)
	}

	public async begin(onrushId: string, date: string | undefined = undefined): Promise<OnrushSprint | undefined> {
		const request: OnrushSprintDateRequest = { date }
		return await this.client.postForJson<OnrushSprint>(`/api/onrush/${encodeURIComponent(onrushId)}/begin`, request)
	}

	public async startNew(date: string | undefined = undefined): Promise<OnrushSprint | undefined> {
		const request: OnrushSprintDateRequest = { date }
		return await this.client.postForJson<OnrushSprint>("/api/onrush/start-new", request)
	}

	public async end(onrushId: string, date: string | undefined = undefined): Promise<OnrushSprint | undefined> {
		const request: OnrushSprintDateRequest = { date }
		return await this.client.postForJson<OnrushSprint>(`/api/onrush/${encodeURIComponent(onrushId)}/end`, request)
	}

	public async assignOnrushStateObjectives(onrushId: string): Promise<Objective[]> {
		return (
			(await this.client.postForJson<Objective[]>(
				`/api/onrush/${encodeURIComponent(onrushId)}/assign-onrush`,
				{}
			)) ?? []
		)
	}
}