export interface PlaintorchCoreRequest {
	method: "GET" | "POST" | "PUT" | "DELETE"
	path: string
	body?: unknown
	headers?: Record<string, string>
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