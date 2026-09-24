import { Agent, request as sendRequest } from "node:http"
import {
	defaultRequestTimeoutMs,
	defaultStreamConnectTimeoutMs,
	isRetryable,
	toLines,
	type PlaintorchCoreRequest,
	type PlaintorchCoreResponse,
	type PlaintorchCoreTransport
} from "./internal/transport"

/**
 * Talks to the core over its per-user local socket: a named pipe on Windows, an AF_UNIX socket elsewhere.
 *
 * Every one-shot request is bounded, drained and pooled (see the fields), a timed-out read is retried once, and
 * {@link stream} holds a long-lived response on a connection of its own. The Obsidian plugin uses it through the
 * node client; the standalone shell uses it directly in its main process to carry what its renderer sends across
 * the preload bridge.
 */
export class NodeSocketPlaintorchCoreTransport implements PlaintorchCoreTransport {
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