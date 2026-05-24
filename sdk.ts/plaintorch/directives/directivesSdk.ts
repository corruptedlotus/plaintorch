import type { PlaintorchCoreClient } from "../coreClient"
import type {
	CreateDirectiveRequest,
	Directive,
	InitDirectiveRequest,
	DirectiveUpdate,
	DirectiveWorkflowShift,
	DirectiveSummary
} from "./contracts"
export class PlaintorchDirectivesSdk {
	public constructor(private readonly client: PlaintorchCoreClient) { }

	public async get(directiveId: string): Promise<DirectiveSummary | undefined> {
		return await this.client.getJson<DirectiveSummary>(`/api/directives/${encodeURIComponent(directiveId)}`)
	}

	public async list(): Promise<Directive[]> {
		return (await this.client.getJson<Directive[]>("/api/directives")) ?? []
	}

	public async search(query: string, take = 10): Promise<DirectiveSummary[]> {
		return (await this.client.getJson<DirectiveSummary[]>(
			`/api/directives?q=${encodeURIComponent(query)}&take=${take}`
		)) ?? []
	}

	public async create(request: CreateDirectiveRequest): Promise<Directive | undefined> {
		return await this.client.postForJson<Directive>("/api/directives", request)
	}

	public async init(request: InitDirectiveRequest): Promise<Directive | undefined> {
		return await this.client.postForJson<Directive>("/api/directives/init", request)
	}

	public async update(directiveId: string, request: DirectiveUpdate): Promise<Directive | undefined> {
		return await this.client.putForJson<Directive>(`/api/directives/${encodeURIComponent(directiveId)}`, request)
	}

	public async shiftWorkflow(directiveId: string, request: DirectiveWorkflowShift): Promise<Directive | undefined> {
		return await this.client.postForJson<Directive>(
			`/api/directives/${encodeURIComponent(directiveId)}/workflow`,
			request
		)
	}

	public async delete(directiveId: string): Promise<boolean> {
		return await this.client.delete(`/api/directives/${encodeURIComponent(directiveId)}`)
	}
}