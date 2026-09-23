import { Agent, request as sendRequest } from "node:http"
import { homedir, userInfo } from "node:os"
import path from "node:path"
import {
	createLoopbackBaseUrl,
	defaultRequestTimeoutMs,
	defaultStreamConnectTimeoutMs,
	isRetryable,
	toLines,
	type PlaintorchCoreRequest,
	type PlaintorchCoreResponse,
	type PlaintorchCoreTransport
} from "./internal/transport"
import { PlaintorchCoreClient, type PlaintorchCoreClientOptions } from "./coreClient"
export interface NodePlaintorchCoreClientOptions extends Omit<PlaintorchCoreClientOptions, "transports"> {
	socketPath?: string
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
// On Windows the core binds a named pipe, because Node resolves a socket path to a named pipe there and cannot reach a
// .NET AF_UNIX socket; every other platform uses the AF_UNIX socket under the profile directory. The pipe name mirrors
// the host's: `{profileDir}.{user}` — per-profile and per-user (Windows pipe names match case-insensitively).
const defaultSocketPath = process.platform === "win32"
	? `\\\\.\\pipe\\${defaultProfileDirectory}.${userInfo().username}`
	: path.join(homedir(), ".pleiades", defaultProfileDirectory, "plaintorch.sock")
export class NodePlaintorchCoreClient extends PlaintorchCoreClient {
	public constructor(options: NodePlaintorchCoreClientOptions = {}) {
		// Node clients talk to the core exclusively over its per-user unix domain socket. `serve` always binds the
		// socket (the loopback HTTP endpoint is opt-in and, when present, is shared across instances), so no
		// fetch/loopback fallback is wired up — a fallback could silently cross-talk to a different instance (e.g. the
		// real daemon on the shared loopback port) whenever the intended socket is unavailable. `baseUrl` is still
		// resolved because the base client uses it to build static asset URLs (`icon()`), not for API requests.
		const baseUrl = options.baseUrl ?? createLoopbackBaseUrl(options.host ?? "127.0.0.1", options.loopbackPort ?? 43118)
		const socketTransport = new NodeSocketPlaintorchCoreTransport(options.socketPath ?? defaultSocketPath)
		super({
			...options,
			baseUrl,
			transports: [socketTransport]
		})
	}
}

class NodeSocketPlaintorchCoreTransport implements PlaintorchCoreTransport {
	public constructor(private readonly socketPath: string) { }

	timeoutMs = defaultRequestTimeoutMs

	/**
	 * A bounded keep-alive pool for one-shot requests.
	 *
	 * Reusing connections keeps a resync burst from opening a fresh pipe connection per request — repeated
	 * setup-and-teardown races the server's pool of pipe instances and shows up as `read EPIPE` — and the
	 * `maxSockets` cap keeps the burst from opening an unbounded number at once; anything past it queues on the
	 * agent. The long-lived feed does not use this; it gets its own connection so it never holds a slot.
	 */
	private readonly agent = new Agent({ keepAlive: true, maxSockets: 8 })

	public async send(request: PlaintorchCoreRequest): Promise<PlaintorchCoreResponse | undefined> {
		const first = await this.attempt(request)
		if (first.response || !first.timedOut || !isRetryable(request)) {
			return first.response
		}

		console.warn(`PLAINTORCH core did not answer ${request.method} ${request.path} within ${this.timeoutMs}ms; retrying once`)
		const second = await this.attempt(request)
		if (!second.response) {
			console.error(`PLAINTORCH core request failed after a retry: ${request.method} ${request.path} via ${this.socketPath}`)
		}

		return second.response
	}

	private async attempt(request: PlaintorchCoreRequest): Promise<{ response?: PlaintorchCoreResponse, timedOut: boolean }> {
		return await new Promise<{ response?: PlaintorchCoreResponse, timedOut: boolean }>((resolve) => {
			const payload = request.body === undefined ? undefined : JSON.stringify(request.body)
			let settled = false
			const settle = (response: PlaintorchCoreResponse | undefined, timedOut = false) => {
				if (!settled) {
					settled = true
					clearTimeout(timer)
					resolve({ response, timedOut })
				}
			}

			const httpRequest = sendRequest(
				{
					socketPath: this.socketPath,
					path: request.path,
					method: request.method,
					// A bounded keep-alive pool rather than the process-wide default agent — see the field.
					agent: this.agent,
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
					// Drain the body before settling, whatever the caller intends to do with it. A keep-alive
					// socket is not returned to the pool until its response is fully read, so a body left
					// unconsumed — a boolean write that ignores its result, an error page behind a non-ok
					// status — pins the connection. Enough pinned connections exhaust `maxSockets`, after which
					// every request queues with no socket and fails on its timeout until the server's own
					// keep-alive timeout closes them: the core looks like it hung and then recovered.
					readNodeResponseText(response)
						.then((text) => settle(wrapNodeResponse(response.statusCode, text, response.headers)))
						.catch(() => settle(undefined))
				}
			)

			const timer = setTimeout(() => {
				httpRequest.destroy()
				settle(undefined, true)
			}, this.timeoutMs)

			httpRequest.on("error", (error) => {
				// Surface transport failures instead of swallowing them — a silent connection error here is exactly what
				// makes the plugin look like it is "making no API calls" when the core is unreachable.
				if (!settled) {
					console.error(`PLAINTORCH core request failed: ${request.method} ${request.path} via ${this.socketPath}`, error)
				}

				settle(undefined)
			})

			if (payload !== undefined) {
				httpRequest.write(payload)
			}

			httpRequest.end()
		})
	}

	/**
	 * Opens a long-lived response over the socket, yielding the body as it arrives.
	 *
	 * The ordinary request path buffers to the end, which never comes for a feed that stays open; this
	 * consumes the response as a stream instead.
	 */
	public async stream(request: PlaintorchCoreRequest, signal: AbortSignal): Promise<AsyncIterable<string> | undefined> {
		return await new Promise<AsyncIterable<string> | undefined>((resolve) => {
			let settled = false
			// Bounds only the connect, not the stream: a feed that stays open all day is the point of it.
			const timer = setTimeout(() => {
				httpRequest.destroy()
				settle(undefined)
			}, defaultStreamConnectTimeoutMs)

			const settle = (value: AsyncIterable<string> | undefined) => {
				if (!settled) {
					settled = true
					clearTimeout(timer)
					resolve(value)
				}
			}

			const httpRequest = sendRequest(
				{
					socketPath: this.socketPath,
					path: request.path,
					method: request.method,
					// Its own dedicated connection, never pooled: a feed held open for the session would
					// otherwise occupy a pooled slot for its entire lifetime and starve the one-shot requests.
					agent: false,
					headers: {
						Accept: "text/event-stream",
						...request.headers
					}
				},
				(response) => {
					const status = response.statusCode ?? 0
					if (status < 200 || status >= 300) {
						response.resume()
						settle(undefined)
						return
					}

					response.setEncoding("utf8")
					settle(toLines(response as AsyncIterable<string>))
				}
			)

			httpRequest.on("error", () => settle(undefined))
			signal.addEventListener("abort", () => httpRequest.destroy(), { once: true })
			httpRequest.end()
		})
	}
}

function wrapNodeResponse(
	status: number | undefined,
	text: string,
	headers: Record<string, string | string[] | undefined> = {}
): PlaintorchCoreResponse {
	const code = status ?? 0
	// The body is already read and buffered by the time this is built, which is what guarantees the socket
	// was drained and released rather than left pinning a slot in the keep-alive pool.
	return {
		ok: code >= 200 && code < 300,
		status: code,
		async text() {
			return text
		},
		header(name) {
			// Node lowercases header names.
			const value = headers[name.toLowerCase()]
			return Array.isArray(value) ? value[0] : value
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