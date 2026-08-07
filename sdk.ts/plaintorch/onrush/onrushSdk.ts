import type { PlaintorchCoreClient } from "../coreClient"
import type { Objective } from "../objectives/models"
import type {
	ExecutiveOrder,
	ExecutiveOrderPlan,
	ExecutiveOrderUpdate,
	OnrushSprint,
	OnrushSprintDateRequest,
	OnrushSprintPlan,
	OnrushSprintUpdate,
	SetGraphLayoutRequest
} from "./contracts"
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

	/** Deletes an onrush sprint (its milestone and orders with it; objectives are detached, not deleted). */
	public async delete(onrushId: string): Promise<boolean> {
		return await this.client.delete(`/api/onrush/${encodeURIComponent(onrushId)}`)
	}

	public async update(onrushId: string, update: OnrushSprintUpdate): Promise<OnrushSprint | undefined> {
		return await this.client.putForJson<OnrushSprint>(`/api/onrush/${encodeURIComponent(onrushId)}`, update)
	}

	/** Persists a sprint's dependency-canvas layout (PEP102); pass undefined to forget it. */
	public async setGraphLayout(onrushId: string, layout: string | undefined): Promise<boolean> {
		const request: SetGraphLayoutRequest = { layout }
		return await this.client.putJson(`/api/onrush/${encodeURIComponent(onrushId)}/graph-layout`, request)
	}

	public async assignOnrushStateObjectives(onrushId: string): Promise<Objective[]> {
		return (
			(await this.client.postForJson<Objective[]>(
				`/api/onrush/${encodeURIComponent(onrushId)}/assign-onrush`,
				{}
			)) ?? []
		)
	}

	public async listExecutiveOrders(onrushId: string): Promise<ExecutiveOrder[]> {
		return (
			(await this.client.getJson<ExecutiveOrder[]>(`/api/onrush/${encodeURIComponent(onrushId)}/orders`)) ?? []
		)
	}

	public async issueExecutiveOrder(onrushId: string, plan: ExecutiveOrderPlan): Promise<ExecutiveOrder | undefined> {
		return await this.client.postForJson<ExecutiveOrder>(`/api/onrush/${encodeURIComponent(onrushId)}/orders`, plan)
	}

	public async updateExecutiveOrder(
		executiveOrderId: string,
		update: ExecutiveOrderUpdate
	): Promise<ExecutiveOrder | undefined> {
		return await this.client.putForJson<ExecutiveOrder>(
			`/api/executive-orders/${encodeURIComponent(executiveOrderId)}`,
			update
		)
	}

	public async deleteExecutiveOrder(executiveOrderId: string): Promise<boolean> {
		return await this.client.delete(`/api/executive-orders/${encodeURIComponent(executiveOrderId)}`)
	}
}