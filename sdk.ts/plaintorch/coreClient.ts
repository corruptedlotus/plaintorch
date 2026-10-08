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
import { PlaintorchActivitiesSdk } from "./activities/activitiesSdk"
import { PlaintorchLoreSdk } from "./lore/loreSdk"
import { PlaintorchDependenciesSdk } from "./dependencies/dependenciesSdk"
import { PlaintorchSchedulesSdk } from "./schedules/schedulesSdk"
import { PlaintorchSystemSdk } from "./system/systemSdk"
import { PlaintorchPreferencesSdk } from "./preferences/preferencesSdk"
import { PlaintorchMediaSdk } from "./media/mediaSdk"
import { createAbsorbingReviver, EntityStore, PlaintorchRepositories, type AbsorptionContext } from "./repository"
import {
	forceCompensationValue,
	isStateEnforcementProblem,
	StateDecisionOutcome,
	stateEnforcementHeader,
	type StateEnforcementHandler,
	type StateEnforcementProblem,
	type StateEnforcementScope
} from "./enforcement/models"


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
/** Response header the core sets to `false` when a write's note is still draining past the note-queue timeout (PEP110). */
const noteReadyHeader = "X-Note-Ready"
/** The status the core answers a change its state enforcement stopped with (PEP103). */
const stateEnforcementStatus = 409
export class PlaintorchCoreClient {
	private readonly baseUrl: string
	private readonly transports: PlaintorchCoreTransport[]
	private lastWriteNotePendingFlag = false
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
	public readonly media: PlaintorchMediaSdk
	public readonly directives: PlaintorchDirectivesSdk
	public readonly objectives: PlaintorchObjectivesSdk
	public readonly declaratives: PlaintorchDeclarativesSdk
	public readonly onrush: PlaintorchOnrushSdk
	public readonly polaris: PlaintorchPolarisSdk
	/** One-shot search over the objectives and decrees a Polaris cycle accepts. */
	public readonly activities: PlaintorchActivitiesSdk
	public readonly lore: PlaintorchLoreSdk
	public readonly dependencies: PlaintorchDependenciesSdk
	/** Planned starts and finishes of backlog items, and the recorded states they reached (PEP101, PEP103). */
	public readonly schedules: PlaintorchSchedulesSdk
	/** Vault-bound user preferences (PEP116). */
	public readonly preferences: PlaintorchPreferencesSdk
	/**
	 * Answers the core's state enforcement (PEP103): a write the core holds back for confirmation is put to it, and is
	 * repeated forcing the compensations once it agrees; a refusal is told to it. Without one, a held-back write is
	 * cancelled. Either way the call itself fails as a write the core turned down always has.
	 */
	public stateEnforcement: StateEnforcementHandler | undefined
	/** The open {@link withStateEnforcement} scopes, innermost last. */
	private readonly enforcementScopes: StateEnforcementScope[] = []
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
		this.system = new PlaintorchSystemSdk(this)
		this.media = new PlaintorchMediaSdk(this)
		this.directives = new PlaintorchDirectivesSdk(this)
		this.objectives = new PlaintorchObjectivesSdk(this)
		this.declaratives = new PlaintorchDeclarativesSdk(this)
		this.onrush = new PlaintorchOnrushSdk(this)
		this.polaris = new PlaintorchPolarisSdk(this)
		this.activities = new PlaintorchActivitiesSdk(this)
		this.lore = new PlaintorchLoreSdk(this)
		this.dependencies = new PlaintorchDependenciesSdk(this)
		this.schedules = new PlaintorchSchedulesSdk(this)
		this.preferences = new PlaintorchPreferencesSdk(this)
		// Constructed last: the repositories delegate to the SDKs above.
		this.repos = new PlaintorchRepositories(this, { resolutionFreshnessMs: options.cacheTtlMs })
	}

	/**
	 * Runs `run` with the writes it sends under `scope` (PEP103): forced, or answered by the caller instead of the
	 * client-wide {@link stateEnforcement} handler. The scope covers the requests `run` sends before its first `await`.
	 * Every SDK call sends its request synchronously, so wrapping the one call a scope is meant for is exact, and a write
	 * made after awaiting something else is never caught by mistake:
	 *
	 * ```ts
	 * let refusal: StateEnforcementProblem | undefined
	 * const shifted = await client.withStateEnforcement({ answer: problem => refusal = problem },
	 * 	() => client.objectives.shiftWorkflow(id, { status }))
	 * ```
	 */
	public withStateEnforcement<T>(scope: StateEnforcementScope, run: () => Promise<T>): Promise<T> {
		this.enforcementScopes.push(scope)
		try {
			return run()
		}
		finally {
			this.enforcementScopes.pop()
		}
	}

	public icon(icon: string): string {
		return `${this.baseUrl}/assets/icons/${encodeURIComponent(icon)}.svg`
	}

	/**
	 * Whether the most recent write's note did not land within the core's note-queue timeout and is finishing in the
	 * background (PEP110). Read it immediately after awaiting a mutation — before issuing another write — to decide
	 * whether to wait for the note (poll a resolution) rather than open a file that may not be on disk yet. Reads
	 * (GETs) leave it untouched, so re-resolving between the write and this read is safe.
	 */
	public get lastWriteNotePending(): boolean {
		return this.lastWriteNotePendingFlag
	}

	/**
	 * @param context Marks a read as authoritative, for changes the core declares a client must not hold
	 * off. Ordinary reads leave it unset and are discarded for any entity changed locally since they went
	 * out.
	 */
	public async getJson<T>(path: string, context?: AbsorptionContext): Promise<T | undefined> {
		// Opened before the request leaves, so a change made while it is in flight supersedes it; closed in the
		// finally so the store can prune the change ledger once this response — and everything older — settles.
		const issuedAt = this.store.beginRead()
		try {
			return await this.readJsonResponse<T>(
				await this.send({
					method: "GET",
					path
				}),
				{ issuedAt, ...context }
			)
		}
		finally {
			this.store.endRead(issuedAt)
		}
	}

	public async postForJson<T>(path: string, body: unknown): Promise<T | undefined> {
		const issuedAt = this.store.beginRead()
		try {
			return await this.readJsonResponse<T>(
				await this.send({
					method: "POST",
					path,
					body
				}),
				{ issuedAt }
			)
		}
		finally {
			this.store.endRead(issuedAt)
		}
	}

	public async putForJson<T>(path: string, body: unknown): Promise<T | undefined> {
		const issuedAt = this.store.beginRead()
		try {
			return await this.readJsonResponse<T>(
				await this.send({
					method: "PUT",
					path,
					body
				}),
				{ issuedAt }
			)
		}
		finally {
			this.store.endRead(issuedAt)
		}
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
		// Read before the first await: a scope covers only what is sent while it is open (see withStateEnforcement).
		const scope = this.enforcementScopes[this.enforcementScopes.length - 1]
		if (scope?.force && request.method !== "GET") {
			request = { ...request, headers: { ...request.headers, [stateEnforcementHeader]: forceCompensationValue } }
		}

		for (const transport of this.transports) {
			const response = await transport.send(request)
			if (response?.status === stateEnforcementStatus) {
				// The core itself answered: another transport would only reach it again.
				const problem = await readStateEnforcementProblem(response)
				if (problem) {
					return await this.answerStateEnforcement(request, problem, scope)
				}
			}

			if (response?.ok) {
				// A write records whether its note is still draining; reads leave the flag for the write before them.
				if (request.method !== "GET") {
					this.lastWriteNotePendingFlag = response.header(noteReadyHeader) === "false"
				}

				Promise.resolve().then(async () => {
					const payload = await response.text()
					console.debug('PLAINTORCH called', request.path, payload ? JSON.parse(payload) : undefined)
				}).catch(() => { })
				return response
			}
		}

		return undefined
	}

	/**
	 * Answers a write the core's state enforcement stopped (PEP103): one held back for confirmation is put to the handler
	 * and, once it agrees, sent again forcing the compensations; a refusal is told to the handler. A write that was already
	 * forced is never put again. A write sent in a scope that answers for itself is handed back to that scope instead.
	 */
	private async answerStateEnforcement(request: PlaintorchCoreRequest, problem: StateEnforcementProblem, scope: StateEnforcementScope | undefined): Promise<PlaintorchCoreResponse | undefined> {
		if (scope?.answer) {
			scope.answer(problem)
			return undefined
		}

		const forced = request.headers?.[stateEnforcementHeader] === forceCompensationValue
		if (problem.outcome === StateDecisionOutcome.Confirm && !forced) {
			const confirmed = await this.stateEnforcement?.confirm(problem).catch(() => false)
			return confirmed
				? await this.send({ ...request, headers: { ...request.headers, [stateEnforcementHeader]: forceCompensationValue } })
				: undefined
		}

		this.stateEnforcement?.refused?.(problem)
		return undefined
	}

	private async readJsonResponse<T>(
		response: PlaintorchCoreResponse | undefined,
		context?: AbsorptionContext
	): Promise<T | undefined> {
		if (!response) {
			return undefined
		}

		const payload = await response.text()
		if (!payload || payload.trim().length === 0) {
			return undefined
		}

		// A reviver per response, because whether it may overwrite local state depends on when it was issued.
		return JSON.parse(payload, createAbsorbingReviver(this.store, context)) as T
	}

	private async sendForSuccess(request: PlaintorchCoreRequest): Promise<boolean> {
		const response = await this.send(request)
		// Consume the body even though only success matters here: a transport with a keep-alive pool does not
		// release a connection until its response is read, so a boolean write that returns a body — a DELETE
		// answered with 200 and a payload, say — would otherwise pin a socket. The node transport already
		// drains on its own; this covers any transport that does not.
		await response?.text()
		return response !== undefined
	}
}

/** Reads the body of a 409 as a state enforcement answer, or nothing when it is a conflict of another kind. */
async function readStateEnforcementProblem(response: PlaintorchCoreResponse): Promise<StateEnforcementProblem | undefined> {
	try {
		const body: unknown = JSON.parse(await response.text())
		return isStateEnforcementProblem(body) ? body : undefined
	} catch {
		return undefined
	}
}
