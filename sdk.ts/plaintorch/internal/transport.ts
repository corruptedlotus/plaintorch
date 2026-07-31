export interface PlaintorchCoreRequest {
	method: "GET" | "POST" | "PUT" | "DELETE"
	path: string
	body?: unknown
	headers?: Record<string, string>
}

/**
 * How long a request may go unanswered before it is treated as failed.
 *
 * There was no bound at all, and an unanswered request never settles. Combined with the request dedup a
 * repository keeps, one stalled call poisoned that identity for the rest of the session: every later read of
 * it joined the same promise and waited forever, and any surface action awaiting a read hung with it.
 */
export const defaultRequestTimeoutMs = 10_000

/** How long establishing a long-lived stream may take. The stream itself is then unbounded by design. */
export const defaultStreamConnectTimeoutMs = 10_000

/**
 * Whether a request can be safely sent again after a timeout.
 *
 * Only reads. A write that timed out may well have been applied, and repeating it could duplicate the
 * effect — reporting the failure is the honest outcome there.
 */
export function isRetryable(request: PlaintorchCoreRequest): boolean {
	return request.method === "GET"
}

export interface PlaintorchCoreResponse {
	ok: boolean
	status: number
	text(): Promise<string>
}

export interface PlaintorchCoreTransport {
	send(request: PlaintorchCoreRequest): Promise<PlaintorchCoreResponse | undefined>
	/**
	 * Opens a long-lived response and yields its body a line at a time.
	 *
	 * Optional: a transport that cannot hold a response open simply omits it, and the caller falls back to
	 * revalidating on its own schedule.
	 */
	stream?(request: PlaintorchCoreRequest, signal: AbortSignal): Promise<AsyncIterable<string> | undefined>
}

export interface FetchPlaintorchCoreTransportOptions {
	baseUrl: string
	headers?: Record<string, string>
}

export class FetchPlaintorchCoreTransport implements PlaintorchCoreTransport {
	private readonly baseUrl: string
	private readonly headers: Record<string, string>
	public constructor(options: FetchPlaintorchCoreTransportOptions) {
		this.baseUrl = options.baseUrl.replace(/\/+$/, "")
		this.headers = options.headers ?? {}
	}

	public async send(request: PlaintorchCoreRequest): Promise<PlaintorchCoreResponse | undefined> {
		if (typeof fetch !== "function") {
			return undefined
		}

		try {
			const response = await fetch(`${this.baseUrl}${request.path}`, {
				method: request.method,
				headers: {
					Accept: "application/json",
					...this.headers,
					...request.headers,
					...(request.body === undefined ? {} : { "Content-Type": "application/json" })
				},
				body: request.body === undefined ? undefined : JSON.stringify(request.body)
			})
			return wrapFetchResponse(response)
		} catch {
			return undefined
		}
	}

	public async stream(request: PlaintorchCoreRequest, signal: AbortSignal): Promise<AsyncIterable<string> | undefined> {
		if (typeof fetch !== "function") {
			return undefined
		}

		try {
			const response = await fetch(`${this.baseUrl}${request.path}`, {
				method: request.method,
				headers: { Accept: "text/event-stream", ...this.headers, ...request.headers },
				signal
			})
			return !response.ok || !response.body ? undefined : toLines(readWebStream(response.body))
		} catch {
			return undefined
		}
	}
}

async function* readWebStream(body: ReadableStream<Uint8Array>): AsyncIterable<string> {
	const reader = body.getReader()
	const decoder = new TextDecoder()
	try {
		for (;;) {
			const { done, value } = await reader.read()
			if (done) {
				return
			}

			yield decoder.decode(value, { stream: true })
		}
	}
	finally {
		reader.releaseLock()
	}
}

export function createLoopbackBaseUrl(host: string, port: number): string {
	return `http://${host}:${port}`
}

/**
 * Reassembles a stream of arbitrary chunks into whole lines.
 *
 * Chunk boundaries fall wherever the transport happens to put them, so a line can arrive in pieces and
 * several lines can arrive at once; neither is visible to a consumer of this.
 */
export async function* toLines(chunks: AsyncIterable<string>): AsyncIterable<string> {
	let buffer = ""
	for await (const chunk of chunks) {
		buffer += chunk
		let newline = buffer.indexOf("\n")
		while (newline >= 0) {
			yield buffer.slice(0, newline).replace(/\r$/, "")
			buffer = buffer.slice(newline + 1)
			newline = buffer.indexOf("\n")
		}
	}

	if (buffer.length > 0) {
		yield buffer
	}
}

function wrapFetchResponse(response: Response): PlaintorchCoreResponse {
	return {
		ok: response.ok,
		status: response.status,
		async text() {
			return await response.text()
		}
	}
}