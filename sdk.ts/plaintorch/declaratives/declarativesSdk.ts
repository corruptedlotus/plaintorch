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
	EventivePlan,
	EventiveUpdate,
	Fate,
	FatePlan,
	FateUpdate,
	Terminative,
	TerminativeOccurrenceRef,
	TerminativeUpdate
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

	public async listEventives(fateId?: string): Promise<Eventive[]> {
		const suffix = fateId ? `?fateId=${encodeURIComponent(fateId)}` : ""
		return (await this.client.getJson<Eventive[]>(`/api/eventives${suffix}`)) ?? []
	}

	public async listAttentives(decreeId?: string): Promise<Attentive[]> {
		const suffix = decreeId ? `?decreeId=${encodeURIComponent(decreeId)}` : ""
		return (await this.client.getJson<Attentive[]>(`/api/attentives${suffix}`)) ?? []
	}

	/**
	 * Adds a manual eventive to a fate — one added by hand rather than generated from its orbit. It is born hardened
	 * with no RECURRENCE-ID, so it is addressed by its row id from then on.
	 */
	public async createEventive(plan: EventivePlan): Promise<Eventive | undefined> {
		return await this.client.postForJson<Eventive>("/api/eventives", plan)
	}

	public async updateEventive(occurrence: EventiveOccurrenceRef, update: EventiveUpdate): Promise<Eventive | undefined> {
		return await this.client.putForJson<Eventive>(`/api/eventives`, { occurrence, update })
	}

	public async updateAttentive(occurrence: AttentiveOccurrenceRef, update: AttentiveUpdate): Promise<Attentive | undefined> {
		return await this.client.putForJson<Attentive>(`/api/attentives`, { occurrence, update })
	}

	/** Lists the hardened terminatives (PEP103 patch 7), or only the one of an origin. */
	public async listTerminatives(originId?: string): Promise<Terminative[]> {
		const suffix = originId ? `?originId=${encodeURIComponent(originId)}` : ""
		return (await this.client.getJson<Terminative[]>(`/api/terminatives${suffix}`)) ?? []
	}

	/**
	 * Raises or lowers a terminative's ignore flag, the one change a user makes to it (PEP103 patch 7). A projected
	 * terminative hardens into the same save; the response carries its origin.
	 */
	public async updateTerminative(occurrence: TerminativeOccurrenceRef, update: TerminativeUpdate): Promise<Terminative | undefined> {
		return await this.client.putForJson<Terminative>(`/api/terminatives`, { occurrence, update })
	}
}
