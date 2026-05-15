import { request as sendRequest } from "node:http";
import { homedir } from "node:os";
import path from "node:path";

import {
  createLoopbackBaseUrl,
  FetchPlaintorchCoreTransport,
  type PlaintorchCoreRequest,
  type PlaintorchCoreResponse,
  type PlaintorchCoreTransport
} from "./internal/transport";
import { PlaintorchCoreClient, type PlaintorchCoreClientOptions } from "./coreClient";

export interface NodePlaintorchCoreClientOptions extends Omit<PlaintorchCoreClientOptions, "transports"> {
  socketPath?: string;
  preferSocket?: boolean;
}

const defaultSocketPath = path.join(homedir(), ".pleiades", "plaintorch", "plaintorch.sock");

export class NodePlaintorchCoreClient extends PlaintorchCoreClient {
  public constructor(options: NodePlaintorchCoreClientOptions = {}) {
    const baseUrl = options.baseUrl ?? createLoopbackBaseUrl(options.host ?? "127.0.0.1", options.loopbackPort ?? 43118);
    const fetchTransport = new FetchPlaintorchCoreTransport({
      baseUrl,
      headers: options.headers
    });
    const socketTransport = new NodeSocketPlaintorchCoreTransport(options.socketPath ?? defaultSocketPath);
    const transports = options.preferSocket
      ? [socketTransport, fetchTransport]
      : [fetchTransport, socketTransport];

    super({
      ...options,
      baseUrl,
      transports
    });
  }
}

class NodeSocketPlaintorchCoreTransport implements PlaintorchCoreTransport {
  public constructor(private readonly socketPath: string) {}

  public async send(request: PlaintorchCoreRequest): Promise<PlaintorchCoreResponse | null> {
    return await new Promise<PlaintorchCoreResponse | null>((resolve) => {
      const payload = request.body === undefined ? undefined : JSON.stringify(request.body);
      const httpRequest = sendRequest(
        {
          socketPath: this.socketPath,
          path: request.path,
          method: request.method,
          headers: {
            Accept: "application/json",
            ...request.headers,
            ...(payload === undefined
              ? {}
              : {
                  "Content-Type": "application/json",
                  "Content-Length": Buffer.byteLength(payload)
                })
          }
        },
        (response) => {
          resolve(wrapNodeResponse(response));
        }
      );

      httpRequest.on("error", () => resolve(null));
      if (payload !== undefined) {
        httpRequest.write(payload);
      }

      httpRequest.end();
    });
  }
}

function wrapNodeResponse(response: NodeJS.ReadableStream & { statusCode?: number }): PlaintorchCoreResponse {
  let cachedText: string | undefined;
  let readPromise: Promise<string> | undefined;

  return {
    ok: !!response.statusCode && response.statusCode >= 200 && response.statusCode < 300,
    status: response.statusCode ?? 0,
    async text() {
      if (cachedText !== undefined) {
        return cachedText;
      }

      readPromise ??= readNodeResponseText(response).then((value) => {
        cachedText = value;
        return value;
      });

      return await readPromise;
    },
    async json<T>() {
      return JSON.parse(await this.text()) as T;
    }
  };
}

async function readNodeResponseText(response: NodeJS.ReadableStream): Promise<string> {
  return await new Promise<string>((resolve, reject) => {
    const chunks: Buffer[] = [];
    response.on("data", (chunk) => {
      chunks.push(Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk));
    });
    response.on("end", () => {
      resolve(Buffer.concat(chunks).toString("utf8"));
    });
    response.on("error", reject);
  });
}