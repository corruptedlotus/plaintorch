import type { PlaintorchCoreClient } from "../coreClient"
import type {
	AddObjectiveToOnrushRequest,
	CreateObjectiveRequest,
	Objective,
	ObjectiveDetail,
	ObjectiveUpdate,
	ObjectiveWorkflowShift
} from "./contracts"
export class PlaintorchObjectivesSdk {
	public constructor(private readonly client: PlaintorchCoreClient) { }

	public async get(objectiveId: string): Promise<ObjectiveDetail | undefined> {
		return await this.client.getJson<ObjectiveDetail>(`/api/objectives/${encodeURIComponent(objectiveId)}`)
	}

	public async list(): Promise<Objective[]> {
		return (await this.client.getJson<Objective[]>("/api/objectives")) ?? []
	}

	public async search(query: string, take = 10): Promise<Objective[]> {
		return (await this.client.getJson<Objective[]>(
			`/api/objectives?q=${encodeURIComponent(query)}&take=${take}`
		)) ?? []
	}

	public async create(request: CreateObjectiveRequest): Promise<Objective | undefined> {
		return await this.client.postForJson<Objective>("/api/objectives", request)
	}

	public async update(objectiveId: string, request: ObjectiveUpdate): Promise<Objective | undefined> {
		return await this.client.putForJson<Objective>(`/api/objectives/${encodeURIComponent(objectiveId)}`, request)
	}

	public async shiftWorkflow(objectiveId: string, request: ObjectiveWorkflowShift): Promise<Objective | undefined> {
		return await this.client.postForJson<Objective>(
			`/api/objectives/${encodeURIComponent(objectiveId)}/workflow`,
			request
		)
	}

	public async addToOnrush(objectiveId: string, onrushSprintId: string): Promise<boolean> {
		const request: AddObjectiveToOnrushRequest = { onrushSprintId }
		return (await this.client.postForJson<Objective>(
			`/api/objectives/${encodeURIComponent(objectiveId)}/onrush`,
			request
		)) !== null
	}

	public async removeFromOnrush(objectiveId: string): Promise<boolean> {
		return await this.client.delete(`/api/objectives/${encodeURIComponent(objectiveId)}/onrush`)
	}

	public async addToOnrushAndFetch(objectiveId: string, onrushSprintId: string): Promise<Objective | undefined> {
		const request: AddObjectiveToOnrushRequest = { onrushSprintId }
		return await this.client.postForJson<Objective>(
			`/api/objectives/${encodeURIComponent(objectiveId)}/onrush`,
			request
		)
	}

	public async delete(objectiveId: string): Promise<boolean> {
		return await this.client.delete(`/api/objectives/${encodeURIComponent(objectiveId)}`)
	}
}