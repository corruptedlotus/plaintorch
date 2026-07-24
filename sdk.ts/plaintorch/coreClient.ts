import { ModelValueConstructor } from "@a11d/api-dotnet"
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
import { PlaintorchDependenciesSdk } from "./dependencies/dependenciesSdk"
import { PlaintorchSystemSdk } from "./system/systemSdk"
import { apiValueConstructor, ApiValueConstructor } from '@a11d/api'


export interface PlaintorchCoreClientOptions {
	baseUrl?: string
	loopbackPort?: number
	host?: string
	cacheTtlMs?: number
	headers?: Record<string, string>
	transports?: PlaintorchCoreTransport[]
}

const defaultLoopbackPort = 43118
const defaultHost = "127.0.0.1"
export class PlaintorchCoreClient {
	private readonly baseUrl: string
	private readonly transports: PlaintorchCoreTransport[]
	public readonly system: PlaintorchSystemSdk
	public readonly directives: PlaintorchDirectivesSdk
	public readonly objectives: PlaintorchObjectivesSdk
	public readonly declaratives: PlaintorchDeclarativesSdk
	public readonly onrush: PlaintorchOnrushSdk
	public readonly polaris: PlaintorchPolarisSdk
	public readonly lore: PlaintorchLoreSdk
	public readonly dependencies: PlaintorchDependenciesSdk
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
		this.system = new PlaintorchSystemSdk(this, options.cacheTtlMs ?? 15_000)
		this.directives = new PlaintorchDirectivesSdk(this)
		this.objectives = new PlaintorchObjectivesSdk(this)
		this.declaratives = new PlaintorchDeclarativesSdk(this)
		this.onrush = new PlaintorchOnrushSdk(this)
		this.polaris = new PlaintorchPolarisSdk(this)
		this.lore = new PlaintorchLoreSdk(this)
		this.dependencies = new PlaintorchDependenciesSdk(this)
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

		return new ModelValueConstructor().construct(JSON.parse(payload)) as T
	}

	private async sendForSuccess(request: PlaintorchCoreRequest): Promise<boolean> {
		return (await this.send(request)) !== undefined
	}
}

export const plaintorchCoreClient = new PlaintorchCoreClient()