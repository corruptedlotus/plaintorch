import type { PlaintorchCoreClient } from "../coreClient"
import type { EntityTypeName } from "./identity"
import type { PlaintorchRepositories } from "./repositories"

/** What the core announces when an entity changes. */
export interface EntityChangeEvent {
	type: string
	id: string
	operation: "Added" | "Modified" | "Deleted"
	/**
	 * Whether this must be applied even over an edit made since the read went out. Set by the core for the
	 * vault reconciling a hand-edited file, and for removals.
	 */
	critical?: boolean
}

export interface ChangeFeedOptions {
	/** Path of the feed endpoint. */
	path?: string
	/** Delay before the first reconnection attempt, doubling up to {@link maximumRetryDelayMs}. */
	initialRetryDelayMs?: number
	/** Ceiling on the reconnection delay. */
	maximumRetryDelayMs?: number
}

const defaults = {
	path: "/api/system/changes",
	initialRetryDelayMs: 1_000,
	maximumRetryDelayMs: 30_000
}

/**
 * Keeps the store in step with writes the frontend did not make.
 *
 * Strictly an optimization: every guarantee of the repository system except promptness holds with this
 * switched off, which is why a feed that cannot be established or that drops is never an error — it just
 * leaves revalidation to freshness and to whatever the host wires up on surface activation.
 */
export class PlaintorchChangeFeed {
	private readonly options: Required<ChangeFeedOptions>
	private controller?: AbortController
	private running = false
	private live = false
	private retryDelayMs: number

	public constructor(
		private readonly client: PlaintorchCoreClient,
		private readonly repositories: PlaintorchRepositories,
		options: ChangeFeedOptions = {}
	) {
		this.options = { ...defaults, ...options }
		this.retryDelayMs = this.options.initialRetryDelayMs
	}

	/** Whether the feed is currently connected. */
	public get connected(): boolean {
		return this.live
	}

	/** Begins listening, reconnecting for as long as it stays started. */
	public start(): void {
		if (this.running) {
			return
		}

		this.running = true
		void this.run()
	}

	/** Stops listening and abandons any open connection. */
	public stop(): void {
		this.running = false
		this.live = false
		this.controller?.abort()
		this.controller = undefined
	}

	private async run(): Promise<void> {
		while (this.running) {
			const connected = await this.consume()
			if (!this.running) {
				return
			}

			// A connection that delivered nothing is indistinguishable from an unreachable core, so both
			// back off; a connection that worked resets the delay so a restart is picked up promptly.
			this.retryDelayMs = connected
				? this.options.initialRetryDelayMs
				: Math.min(this.retryDelayMs * 2, this.options.maximumRetryDelayMs)

			await delay(this.retryDelayMs)
		}
	}

	private async consume(): Promise<boolean> {
		const controller = new AbortController()
		this.controller = controller

		let lines: AsyncIterable<string> | undefined
		try {
			lines = await this.client.openStream(this.options.path, controller.signal)
		}
		catch {
			lines = undefined
		}

		if (!lines) {
			return false
		}

		this.live = true
		// Anything that happened while disconnected was missed, so nothing already loaded can be trusted
		// until it is revalidated. Marking everything stale is cheap; only what is on screen is refetched.
		this.repositories.invalidateAll()
		void this.repositories.revalidateObserved()

		try {
			let data = ""
			for await (const line of lines) {
				if (line.startsWith(":")) {
					continue
				}

				if (line.length === 0) {
					this.dispatch(data)
					data = ""
					continue
				}

				if (line.startsWith("data:")) {
					data += line.slice("data:".length).trimStart()
				}
			}

			return true
		}
		catch {
			return true
		}
		finally {
			this.live = false
		}
	}

	private dispatch(data: string): void {
		if (data.length === 0) {
			return
		}

		let change: EntityChangeEvent | undefined
		try {
			change = JSON.parse(data) as EntityChangeEvent
		}
		catch {
			return
		}

		if (!change?.type || !change.id) {
			return
		}

		// A local edit normally outranks a refresh, so the echo of this client's own write cannot revert it.
		// A critical change is the core saying otherwise — the vault reconciling a hand-edited file is the
		// authority on that entity, and a removal leaves nothing for an edit to be about — so the local
		// claim is given up and the revalidation that follows is allowed to win.
		if (change.critical) {
			this.repositories.acceptAuthority(change.type, change.id)
		}

		this.repositories.invalidation.invalidate(change.type as EntityTypeName, change.id)
	}
}

function delay(milliseconds: number): Promise<void> {
	return new Promise((resolve) => setTimeout(resolve, milliseconds))
}
