import type { PlaintorchCoreClient } from "../coreClient"
import type { LorePage } from "./contracts"

export class PlaintorchLoreSdk {
	public constructor(private readonly client: PlaintorchCoreClient) { }

	public async list(): Promise<LorePage[]> {
		return (await this.client.getJson<LorePage[]>("/api/lorepages")) ?? []
	}

	public async get(puck: string): Promise<LorePage | undefined> {
		return await this.client.getJson<LorePage>(`/api/lorepages/${puck}`)
	}
}
