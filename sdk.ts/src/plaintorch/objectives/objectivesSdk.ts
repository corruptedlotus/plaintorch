import type { PlaintorchCoreClient } from "../coreClient";
import type { PlaintorchObjectiveDetail } from "./contracts";

export class PlaintorchObjectivesSdk {
  public constructor(private readonly client: PlaintorchCoreClient) {}

  public async get(objectiveId: string): Promise<PlaintorchObjectiveDetail | null> {
    return await this.client.getJson<PlaintorchObjectiveDetail>(`/api/objectives/${encodeURIComponent(objectiveId)}`);
  }

  public async addToOnrush(objectiveId: string, onrushSprintId: string): Promise<boolean> {
    return await this.client.postJson(`/api/objectives/${encodeURIComponent(objectiveId)}/onrush`, { onrushSprintId });
  }
}