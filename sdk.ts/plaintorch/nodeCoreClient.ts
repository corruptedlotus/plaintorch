import { request as sendRequest } from "node:http"
import { homedir } from "node:os"
import path from "node:path"
import {
	createLoopbackBaseUrl,
	FetchPlaintorchCoreTransport,
	type PlaintorchCoreRequest,
	type PlaintorchCoreResponse,
	type PlaintorchCoreTransport
} from "./internal/transport"
import { PlaintorchCoreClient, type PlaintorchCoreClientOptions } from "./coreClient"
import { ModelValueConstructor } from "@a11d/api-dotnet"
export interface NodePlaintorchCoreClientOptions extends Omit<PlaintorchCoreClientOptions, "transports"> {
	socketPath?: string
	preferSocket?: boolean
}

// Compile-time flag baked into the bundle by the bundler (see the Obsidian plugin's esbuild `define`). A dev/watch
// build substitutes `true` here, so the default client targets the persistent dev sub-profile socket a manual `serve`
// binds (`~/.pleiades/plaintorch-dev`); a production build substitutes `false`. The `typeof` guard keeps the SDK safe
// when it is consumed without the define at all (the identifier is simply absent from the output) — it then falls back
// to the real per-user profile (`~/.pleiades/plaintorch`). This is a build-time substitution, not a runtime env read,
// so it survives into the shipped bundle regardless of the process environment it later runs in.
declare const __PLAINTORCH_DEV_PROFILE__: boolean
const useDevProfile = typeof __PLAINTORCH_DEV_PROFILE__ !== "undefined" && __PLAINTORCH_DEV_PROFILE__
const defaultProfileDirectory = useDevProfile ? "plaintorch-dev" : "plaintorch"
const defaultSocketPath = path.join(homedir(), ".pleiades", defaultProfileDirectory, "plaintorch.sock")
export class NodePlaintorchCoreClient extends PlaintorchCoreClient {
	public constructor(options: NodePlaintorchCoreClientOptions = {}) {
		const baseUrl = options.baseUrl ?? createLoopbackBaseUrl(options.host ?? "127.0.0.1", options.loopbackPort ?? 43118)
		const fetchTransport = new FetchPlaintorchCoreTransport({
			baseUrl,
			headers: options.headers
		})
		const socketTransport = new NodeSocketPlaintorchCoreTransport(options.socketPath ?? defaultSocketPath)
		const transports = options.preferSocket
			? [socketTransport, fetchTransport]
			: [fetchTransport, socketTransport]
		super({
			...options,
			baseUrl,
			transports
		})
	}
}

class NodeSocketPlaintorchCoreTransport implements PlaintorchCoreTransport {
	public constructor(private readonly socketPath: string) { }

	public async send(request: PlaintorchCoreRequest): Promise<PlaintorchCoreResponse | undefined> {
		return await new Promise<PlaintorchCoreResponse | undefined>((resolve) => {
			const payload = request.body === undefined ? undefined : JSON.stringify(request.body)
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
					resolve(wrapNodeResponse(response))
				}
			)
			httpRequest.on("error", () => resolve(undefined))
			if (payload !== undefined) {
				httpRequest.write(payload)
			}

			httpRequest.end()
		})
	}
}

function wrapNodeResponse(response: NodeJS.ReadableStream & { statusCode?: number }): PlaintorchCoreResponse {
	let cachedText: string | undefined
	let readPromise: Promise<string> | undefined
	return {
		ok: !!response.statusCode && response.statusCode >= 200 && response.statusCode < 300,
		status: response.statusCode ?? 0,
		async text() {
			if (cachedText !== undefined) {
				return cachedText
			}

			readPromise ??= readNodeResponseText(response).then((value) => {
				cachedText = value
				return value
			})
			return await readPromise
		},
		async json<T>() {
			return new ModelValueConstructor().construct(JSON.parse(await this.text())) as T
		}
	}
}

async function readNodeResponseText(response: NodeJS.ReadableStream): Promise<string> {
	return await new Promise<string>((resolve, reject) => {
		const chunks: Buffer[] = []
		response.on("data", (chunk) => {
			chunks.push(Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk))
		})
		response.on("end", () => {
			resolve(Buffer.concat(chunks).toString("utf8"))
		})
		response.on("error", reject)
	})
}