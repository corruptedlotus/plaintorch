import type { PlaintorchCoreClient } from "../coreClient";

export class PlaintorchPolarisSdk {
  public constructor(private readonly client: PlaintorchCoreClient) {}

  public async begin(): Promise<boolean> {
    return await this.client.postJson("/api/polaris/current/begin", { time: null });
  }

  public async startNew(): Promise<boolean> {
    return await this.client.postJson("/api/polaris/start-new", { time: null });
  }

  public async end(): Promise<boolean> {
    return await this.client.postJson("/api/polaris/current/end", { time: null });
  }

  public async addObjectiveToCurrent(objectiveId: string): Promise<boolean> {
    return await this.client.postJson("/api/polaris/current/executives/plan", {
      mode: 3,
      objectiveId
    });
  }
}