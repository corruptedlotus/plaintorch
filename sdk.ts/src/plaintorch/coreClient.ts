import { PlaintorchDirectivesSdk } from "./directives/directivesSdk";
import {
  createLoopbackBaseUrl,
  FetchPlaintorchCoreTransport,
  type PlaintorchCoreRequest,
  type PlaintorchCoreTransport
} from "./internal/transport";
import { PlaintorchObjectivesSdk } from "./objectives/objectivesSdk";
import { PlaintorchOnrushSdk } from "./onrush/onrushSdk";
import { PlaintorchPolarisSdk } from "./polaris/polarisSdk";
import { PlaintorchSystemSdk } from "./system/systemSdk";

export interface PlaintorchCoreClientOptions {
  baseUrl?: string;
  loopbackPort?: number;
  host?: string;
  cacheTtlMs?: number;
  headers?: Record<string, string>;
  transports?: PlaintorchCoreTransport[];
}

const defaultLoopbackPort = 43118;
const defaultHost = "127.0.0.1";

export class PlaintorchCoreClient {
  private readonly transports: PlaintorchCoreTransport[];

  public readonly system: PlaintorchSystemSdk;
  public readonly directives: PlaintorchDirectivesSdk;
  public readonly objectives: PlaintorchObjectivesSdk;
  public readonly onrush: PlaintorchOnrushSdk;
  public readonly polaris: PlaintorchPolarisSdk;

  public constructor(options: PlaintorchCoreClientOptions = {}) {
    const host = options.host ?? defaultHost;
    const loopbackPort = options.loopbackPort ?? defaultLoopbackPort;
    const baseUrl = options.baseUrl ?? createLoopbackBaseUrl(host, loopbackPort);

    this.transports = options.transports ?? [
      new FetchPlaintorchCoreTransport({
        baseUrl,
        headers: options.headers
      })
    ];

    this.system = new PlaintorchSystemSdk(this, options.cacheTtlMs ?? 15_000);
    this.directives = new PlaintorchDirectivesSdk(this);
    this.objectives = new PlaintorchObjectivesSdk(this);
    this.onrush = new PlaintorchOnrushSdk(this);
    this.polaris = new PlaintorchPolarisSdk(this);
  }

  public async getJson<T>(path: string): Promise<T | null> {
    const response = await this.send({
      method: "GET",
      path
    });

    return response ? await response.json<T>() : null;
  }

  public async postJson(path: string, body: unknown): Promise<boolean> {
    return await this.sendForSuccess({
      method: "POST",
      path,
      body
    });
  }

  public async putJson(path: string, body: unknown): Promise<boolean> {
    return await this.sendForSuccess({
      method: "PUT",
      path,
      body
    });
  }

  protected async send(request: PlaintorchCoreRequest) {
    for (const transport of this.transports) {
      const response = await transport.send(request);
      if (response?.ok) {
        return response;
      }
    }

    return null;
  }

  private async sendForSuccess(request: PlaintorchCoreRequest): Promise<boolean> {
    return (await this.send(request)) !== null;
  }
}

export const plaintorchCoreClient = new PlaintorchCoreClient();
