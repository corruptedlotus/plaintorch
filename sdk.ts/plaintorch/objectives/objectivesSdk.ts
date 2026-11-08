import type { PlaintorchCoreClient } from "../coreClient"
import type { OnrushSlot } from "../onrush/models"
import type {
	AddObjectiveToOnrushRequest,
	CreateObjectiveRequest,
	InitObjectiveRequest,
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

	public async init(request: InitObjectiveRequest): Promise<Objective | undefined> {
		return await this.client.postForJson<Objective>("/api/objectives/init", request)
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

	/**
	 * Adds an objective to a sprint: one named by id, or the active or planning one (an {@link OnrushSlot}), which the core
	 * creates when it does not exist yet. Reports whether the core accepted it; a refused or unanswered request reports
	 * `false`.
	 */
	public async addToOnrush(objectiveId: string, target: string | OnrushSlot): Promise<boolean> {
		const request: AddObjectiveToOnrushRequest = typeof target === 'string' ? { onrushSprintId: target } : { slot: target }
		return (await this.client.postForJson<Objective>(
			`/api/objectives/${encodeURIComponent(objectiveId)}/onrush`,
			request
		)) !== undefined
	}

	public async removeFromOnrush(objectiveId: string): Promise<boolean> {
		return await this.client.delete(`/api/objectives/${encodeURIComponent(objectiveId)}/onrush`)
	}

	public async addToOnrushAndFetch(objectiveId: string, target: string | OnrushSlot): Promise<Objective | undefined> {
		const request: AddObjectiveToOnrushRequest = typeof target === 'string' ? { onrushSprintId: target } : { slot: target }
		return await this.client.postForJson<Objective>(
			`/api/objectives/${encodeURIComponent(objectiveId)}/onrush`,
			request
		)
	}

	/**
	 * Deletes an objective. Its executives in ended Polaris cycles stay as work records with no incentive; those in the
	 * active cycle and in planned or forecast cycles are removed with it.
	 */
	public async delete(objectiveId: string): Promise<boolean> {
		return await this.client.delete(`/api/objectives/${encodeURIComponent(objectiveId)}`)
	}

	/** Materializes the implicit objective's markdown file and begins its synchronization boundary. */
	public async beginObjective(objectiveId: string): Promise<Objective | undefined> {
		return await this.client.postForJson<Objective>(`/api/objectives/${encodeURIComponent(objectiveId)}/begin`, {})
	}
}