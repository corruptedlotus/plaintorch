import type { PlaintorchCoreClient } from "../coreClient"
import type {
	Checkpoint,
	CreateCheckpointRequest,
	CreateDependencyRequest,
	Dependency,
	DependencyLockView
} from "./contracts"

export class PlaintorchDependenciesSdk {
	public constructor(private readonly client: PlaintorchCoreClient) { }

	/** Lists dependency edges, optionally filtered to those touching an endpoint id. */
	public async list(entityId?: string): Promise<Dependency[]> {
		const query = entityId ? `?entityId=${encodeURIComponent(entityId)}` : ""
		return (await this.client.getJson<Dependency[]>(`/api/dependencies${query}`)) ?? []
	}

	/** Creates a dependency edge (source blocks target). */
	public async create(request: CreateDependencyRequest): Promise<Dependency | undefined> {
		return await this.client.postForJson<Dependency>("/api/dependencies", request)
	}

	/** Deletes a dependency edge. */
	public async delete(dependencyId: number): Promise<boolean> {
		return await this.client.delete(`/api/dependencies/${dependencyId}`)
	}

	/** Gets the emitted dependency lock for an entity id (its unsatisfied incoming dependencies). */
	public async lock(entityId: string): Promise<DependencyLockView | undefined> {
		return await this.client.getJson<DependencyLockView>(`/api/dependencies/lock/${encodeURIComponent(entityId)}`)
	}

	/** Lists checkpoints. */
	public async listCheckpoints(): Promise<Checkpoint[]> {
		return (await this.client.getJson<Checkpoint[]>("/api/checkpoints")) ?? []
	}

	/** Gets a checkpoint by id. */
	public async getCheckpoint(checkpointId: string): Promise<Checkpoint | undefined> {
		return await this.client.getJson<Checkpoint>(`/api/checkpoints/${encodeURIComponent(checkpointId)}`)
	}

	/** Creates a checkpoint. */
	public async createCheckpoint(request: CreateCheckpointRequest): Promise<Checkpoint | undefined> {
		return await this.client.postForJson<Checkpoint>("/api/checkpoints", request)
	}

	/** Deletes a checkpoint and the dependency edges touching it. */
	public async deleteCheckpoint(checkpointId: string): Promise<boolean> {
		return await this.client.delete(`/api/checkpoints/${encodeURIComponent(checkpointId)}`)
	}

	/** Pays a checkpoint's Celestron toll from the banked balance (order-independent). */
	public async payToll(checkpointId: string): Promise<Checkpoint | undefined> {
		return await this.client.postForJson<Checkpoint>(`/api/checkpoints/${encodeURIComponent(checkpointId)}/toll`, {})
	}

	/** Sets a checkpoint's external condition switch. */
	public async setCondition(checkpointId: string, met: boolean): Promise<Checkpoint | undefined> {
		return await this.client.postForJson<Checkpoint>(`/api/checkpoints/${encodeURIComponent(checkpointId)}/condition`, { met })
	}
}
