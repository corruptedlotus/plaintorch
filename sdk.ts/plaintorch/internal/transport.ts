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
	json<T>(): Promise<T>
}

export interface PlaintorchCoreTransport {
	send(request: PlaintorchCoreRequest): Promise<PlaintorchCoreResponse | undefined>
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
}

export function createLoopbackBaseUrl(host: string, port: number): string {
	return `http://${host}:${port}`
}

function wrapFetchResponse(response: Response): PlaintorchCoreResponse {
	return {
		ok: response.ok,
		status: response.status,
		async text() {
			return await response.text()
		},
		async json<T>() {
			return (await response.json()) as T
		}
	}
}