import type { PlaintorchCoreClient } from "../coreClient"
import type {
	Attentive,
	AttentiveOccurrenceRef,
	AttentiveUpdate,
	Decree,
	DecreePlan,
	DecreeUpdate,
	Eventive,
	EventiveOccurrenceRef,
	EventiveUpdate,
	Fate,
	FatePlan,
	FateUpdate
} from "./contracts"
export class PlaintorchDeclarativesSdk {
	public constructor(private readonly client: PlaintorchCoreClient) { }

	public async listFates(): Promise<Fate[]> {
		return (await this.client.getJson<Fate[]>("/api/fates")) ?? []
	}

	public async getFate(fateId: string): Promise<Fate | undefined> {
		return await this.client.getJson<Fate>(`/api/fates/${encodeURIComponent(fateId)}`)
	}

	public async createFate(plan: FatePlan): Promise<Fate | undefined> {
		return await this.client.postForJson<Fate>("/api/fates", plan)
	}

	public async updateFate(fateId: string, update: FateUpdate): Promise<Fate | undefined> {
		return await this.client.putForJson<Fate>(`/api/fates/${encodeURIComponent(fateId)}`, update)
	}

	public async deleteFate(fateId: string): Promise<boolean> {
		return await this.client.delete(`/api/fates/${encodeURIComponent(fateId)}`)
	}

	/** Materializes the implicit fate's markdown file and begins its synchronization boundary. */
	public async beginFate(fateId: string): Promise<Fate | undefined> {
		return await this.client.postForJson<Fate>(`/api/fates/${encodeURIComponent(fateId)}/begin`, {})
	}

	public async listDecrees(): Promise<Decree[]> {
		return (await this.client.getJson<Decree[]>("/api/decrees")) ?? []
	}

	public async getDecree(decreeId: string): Promise<Decree | undefined> {
		return await this.client.getJson<Decree>(`/api/decrees/${encodeURIComponent(decreeId)}`)
	}

	public async createDecree(plan: DecreePlan): Promise<Decree | undefined> {
		return await this.client.postForJson<Decree>("/api/decrees", plan)
	}

	public async updateDecree(decreeId: string, update: DecreeUpdate): Promise<Decree | undefined> {
		return await this.client.putForJson<Decree>(`/api/decrees/${encodeURIComponent(decreeId)}`, update)
	}

	/**
	 * Deletes a decree. Its executives and reflectives in ended Polaris cycles stay as work records with no decree; those
	 * in the active cycle and in planned or forecast cycles are removed with it.
	 */
	public async deleteDecree(decreeId: string): Promise<boolean> {
		return await this.client.delete(`/api/decrees/${encodeURIComponent(decreeId)}`)
	}

	/** Materializes the implicit decree's markdown file and begins its synchronization boundary. */
	public async beginDecree(decreeId: string): Promise<Decree | undefined> {
		return await this.client.postForJson<Decree>(`/api/decrees/${encodeURIComponent(decreeId)}/begin`, {})
	}

	public async listEventives(fateId?: string, objectiveId?: string): Promise<Eventive[]> {
		const query = new URLSearchParams()
		if (fateId) {
			query.set("fateId", fateId)
		}
		if (objectiveId) {
			query.set("objectiveId", objectiveId)
		}
		const suffix = query.size > 0 ? `?${query.toString()}` : ""
		return (await this.client.getJson<Eventive[]>(`/api/eventives${suffix}`)) ?? []
	}

	public async listAttentives(decreeId?: string): Promise<Attentive[]> {
		const suffix = decreeId ? `?decreeId=${encodeURIComponent(decreeId)}` : ""
		return (await this.client.getJson<Attentive[]>(`/api/attentives${suffix}`)) ?? []
	}

	public async updateEventive(occurrence: EventiveOccurrenceRef, update: EventiveUpdate): Promise<Eventive | undefined> {
		return await this.client.putForJson<Eventive>(`/api/eventives`, { occurrence, update })
	}

	public async updateAttentive(occurrence: AttentiveOccurrenceRef, update: AttentiveUpdate): Promise<Attentive | undefined> {
		return await this.client.putForJson<Attentive>(`/api/attentives`, { occurrence, update })
	}
}
