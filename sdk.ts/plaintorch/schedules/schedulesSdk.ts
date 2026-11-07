import type { PlaintorchCoreClient } from "../coreClient"
import type { AddBacklogScheduleRequest, BacklogSchedule } from "./contracts"

/** The backlog-schedule surface (PEP101, PEP103): planned starts and finishes of backlog items, and the recorded states they reached. */
export class PlaintorchSchedulesSdk {
	public constructor(private readonly client: PlaintorchCoreClient) { }

	/** Lists schedules, plans and records alike, ordered by time — only those of one owner when an entity id is given. */
	public async list(entityId?: string): Promise<BacklogSchedule[]> {
		const query = entityId ? `?entityId=${encodeURIComponent(entityId)}` : ""
		return (await this.client.getJson<BacklogSchedule[]>(`/api/schedules${query}`)) ?? []
	}

	/** Adds a plan, replacing the owner's schedule of the same type and state. Records are refused: only the core writes them. */
	public async add(request: AddBacklogScheduleRequest): Promise<BacklogSchedule | undefined> {
		return await this.client.postForJson<BacklogSchedule>("/api/schedules", request)
	}

	/** Deletes a plan. A record goes only with its owner. */
	public async delete(scheduleId: number): Promise<boolean> {
		return await this.client.delete(`/api/schedules/${scheduleId}`)
	}
}
