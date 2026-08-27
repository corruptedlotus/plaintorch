import type { PlaintorchCoreClient } from "../coreClient"
import type { LorePage, LorePageCreate, LorePageUpdate } from "./contracts"

export class PlaintorchLoreSdk {
	public constructor(private readonly client: PlaintorchCoreClient) { }

	public async list(): Promise<LorePage[]> {
		return (await this.client.getJson<LorePage[]>("/api/lorepages")) ?? []
	}

	public async get(puck: string): Promise<LorePage | undefined> {
		return await this.client.getJson<LorePage>(`/api/lorepages/${puck}`)
	}

	public async create(request: LorePageCreate): Promise<LorePage | undefined> {
		return await this.client.postForJson<LorePage>("/api/lorepages", request)
	}

	public async update(puck: string, update: LorePageUpdate): Promise<LorePage | undefined> {
		return await this.client.putForJson<LorePage>(`/api/lorepages/${puck}`, update)
	}

	// The puck is not encoded so its hierarchy slashes reach the catch-all route, matching list/get/update above.
	public async delete(puck: string): Promise<boolean> {
		return await this.client.delete(`/api/lorepages/${puck}`)
	}
}
