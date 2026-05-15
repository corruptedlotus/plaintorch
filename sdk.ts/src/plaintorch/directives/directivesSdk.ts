import type { PlaintorchCoreClient } from "../coreClient";
import type { PlaintorchDirectiveSummary } from "./contracts";

export class PlaintorchDirectivesSdk {
  public constructor(private readonly client: PlaintorchCoreClient) {}

  public async get(directiveId: string): Promise<PlaintorchDirectiveSummary | null> {
    return await this.client.getJson<PlaintorchDirectiveSummary>(`/api/directives/${encodeURIComponent(directiveId)}`);
  }

  public async search(query: string, take = 10): Promise<PlaintorchDirectiveSummary[]> {
    return (await this.client.getJson<PlaintorchDirectiveSummary[]>(
      `/api/directives?q=${encodeURIComponent(query)}&take=${take}`
    )) ?? [];
  }
}