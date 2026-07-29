import { PlaintorchDirectivesSdk } from "./directives/directivesSdk"
import {
	createLoopbackBaseUrl,
	FetchPlaintorchCoreTransport,
	type PlaintorchCoreRequest,
	type PlaintorchCoreResponse,
	type PlaintorchCoreTransport
} from "./internal/transport"
import { PlaintorchDeclarativesSdk } from "./declaratives/declarativesSdk"
import { PlaintorchObjectivesSdk } from "./objectives/objectivesSdk"
import { PlaintorchOnrushSdk } from "./onrush/onrushSdk"
import { PlaintorchPolarisSdk } from "./polaris/polarisSdk"
import { PlaintorchLoreSdk } from "./lore/loreSdk"
import { PlaintorchSystemSdk } from "./system/systemSdk"
import { createAbsorbingReviver, EntityStore, PlaintorchRepositories } from "./repository"


export interface PlaintorchCoreClientOptions {
	baseUrl?: string
	loopbackPort?: number
	host?: string
	/** How long a resolved note or PUCK lookup is served before it is revalidated. */
	cacheTtlMs?: number
	headers?: Record<string, string>
	transports?: PlaintorchCoreTransport[]
}

const defaultLoopbackPort = 43118
const defaultHost = "127.0.0.1"
export class PlaintorchCoreClient {
	private readonly baseUrl: string
	private readonly transports: PlaintorchCoreTransport[]
	private readonly reviver: (key: string, value: unknown) => unknown
	/**
	 * Canonical instances of every entity this client has seen. Populated by every response the client
	 * reads, so call sites that have not moved onto repositories still contribute to it.
	 */
	public readonly store: EntityStore
	/**
	 * Cached, observable reads over the domain SDKs. Use these for anything a surface displays and must
	 * keep current; the SDKs below stay the way to run a one-shot query or an imperative command.
	 */
	public readonly repos: PlaintorchRepositories
	public readonly system: PlaintorchSystemSdk
	public readonly directives: PlaintorchDirectivesSdk
	public readonly objectives: PlaintorchObjectivesSdk
	public readonly declaratives: PlaintorchDeclarativesSdk
	public readonly onrush: PlaintorchOnrushSdk
	public readonly polaris: PlaintorchPolarisSdk
	public readonly lore: PlaintorchLoreSdk
	public constructor(options: PlaintorchCoreClientOptions = {}) {
		const host = options.host ?? defaultHost
		const loopbackPort = options.loopbackPort ?? defaultLoopbackPort
		const resolvedBaseUrl = options.baseUrl ?? createLoopbackBaseUrl(host, loopbackPort)
		this.baseUrl = resolvedBaseUrl.replace(/\/+$/, "")
		this.transports = options.transports ?? [
			new FetchPlaintorchCoreTransport({
				baseUrl: this.baseUrl,
				headers: options.headers
			})
		]
		this.store = new EntityStore()
		this.reviver = createAbsorbingReviver(this.store)
		this.system = new PlaintorchSystemSdk(this)
		this.directives = new PlaintorchDirectivesSdk(this)
		this.objectives = new PlaintorchObjectivesSdk(this)
		this.declaratives = new PlaintorchDeclarativesSdk(this)
		this.onrush = new PlaintorchOnrushSdk(this)
		this.polaris = new PlaintorchPolarisSdk(this)
		this.lore = new PlaintorchLoreSdk(this)
		// Constructed last: the repositories delegate to the SDKs above.
		this.repos = new PlaintorchRepositories(this, { resolutionFreshnessMs: options.cacheTtlMs })
	}

	public icon(icon: string): string {
		return `${this.baseUrl}/assets/icons/${encodeURIComponent(icon)}.svg`
	}

	public async getJson<T>(path: string): Promise<T | undefined> {
		return await this.readJsonResponse<T>(
			await this.send({
				method: "GET",
				path
			})
		)
	}

	public async postForJson<T>(path: string, body: unknown): Promise<T | undefined> {
		return await this.readJsonResponse<T>(
			await this.send({
				method: "POST",
				path,
				body
			})
		)
	}

	public async putForJson<T>(path: string, body: unknown): Promise<T | undefined> {
		return await this.readJsonResponse<T>(
			await this.send({
				method: "PUT",
				path,
				body
			})
		)
	}

	public async delete(path: string): Promise<boolean> {
		return await this.sendForSuccess({
			method: "DELETE",
			path
		})
	}

	public async postJson(path: string, body: unknown): Promise<boolean> {
		return await this.sendForSuccess({
			method: "POST",
			path,
			body
		})
	}

	public async putJson(path: string, body: unknown): Promise<boolean> {
		return await this.sendForSuccess({
			method: "PUT",
			path,
			body
		})
	}

	/**
	 * Opens a long-lived response on the first transport able to hold one open.
	 *
	 * Returns nothing when no transport supports streaming, which callers must treat as a capability that
	 * is simply absent rather than as a failure.
	 */
	public async openStream(path: string, signal: AbortSignal): Promise<AsyncIterable<string> | undefined> {
		for (const transport of this.transports) {
			const stream = await transport.stream?.({ method: "GET", path }, signal)
			if (stream) {
				return stream
			}
		}

		return undefined
	}

	protected async send(request: PlaintorchCoreRequest): Promise<PlaintorchCoreResponse | undefined> {
		for (const transport of this.transports) {
			const response = await transport.send(request)
			if (response?.ok) {
				return response
			}
		}

		return undefined
	}

	private async readJsonResponse<T>(response: PlaintorchCoreResponse | undefined): Promise<T | undefined> {
		if (!response) {
			return undefined
		}

		const payload = await response.text()
		if (!payload || payload.trim().length === 0) {
			return undefined
		}

		return JSON.parse(payload, this.reviver) as T
	}

	private async sendForSuccess(request: PlaintorchCoreRequest): Promise<boolean> {
		return (await this.send(request)) !== undefined
	}
}