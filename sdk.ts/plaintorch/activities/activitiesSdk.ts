import type { PlaintorchCoreClient } from "../coreClient"
import type { Activity } from "./models"

/**
 * One-shot queries over activities — the objectives and decrees a Polaris cycle accepts — mirroring the objectives
 * search surface. Used by pickers that let the user add either kind to the current cycle.
 */
export class PlaintorchActivitiesSdk {
	public constructor(private readonly client: PlaintorchCoreClient) { }

	public async list(): Promise<Activity[]> {
		return (await this.client.getJson<Activity[]>("/api/activities")) ?? []
	}

	public async search(query: string, take = 10): Promise<Activity[]> {
		return (await this.client.getJson<Activity[]>(
			`/api/activities?q=${encodeURIComponent(query)}&take=${take}`
		)) ?? []
	}
}
