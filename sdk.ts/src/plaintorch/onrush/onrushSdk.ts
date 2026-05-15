import type { PlaintorchCoreClient } from "../coreClient";

export class PlaintorchOnrushSdk {
  public constructor(private readonly client: PlaintorchCoreClient) {}

  public async begin(onrushId: string): Promise<boolean> {
    return await this.client.postJson(`/api/onrush/${encodeURIComponent(onrushId)}/begin`, { date: null });
  }

  public async startNew(): Promise<boolean> {
    return await this.client.postJson("/api/onrush/start-new", { date: null });
  }

  public async end(onrushId: string): Promise<boolean> {
    return await this.client.postJson(`/api/onrush/${encodeURIComponent(onrushId)}/end`, { date: null });
  }
}