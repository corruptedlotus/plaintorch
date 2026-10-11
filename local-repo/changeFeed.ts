/** What the backend announces when an entity changes. */
export interface EntityChangeEvent {
	type: string
	id: string
	operation?: "Added" | "Modified" | "Deleted"
	/**
	 * Whether this must be applied even over an edit made since the read went out. Set by the backend for a
	 * change it is the authority on — a file it reconciled by hand-edit — and for removals.
	 */
	critical?: boolean
}

/** Where a feed gets its stream of lines from. */
export interface ChangeFeedSource {
	/**
	 * Opens the long-lived stream and yields it a line at a time, or resolves to nothing when no stream can be
	 * had — a capability that is absent or a backend that is unreachable. The signal is aborted when the feed
	 * stops.
	 */
	open(signal: AbortSignal): Promise<AsyncIterable<string> | undefined>
}

/** What a feed drives: the repositories that mark things stale and refetch what is on screen. */
export interface ChangeFeedSink {
	/** Marks everything as needing revalidation, without fetching anything. */
	invalidateAll(): void
	/** Revalidates everything currently observed. */
	revalidateObserved(): Promise<void>
	/** Gives up the local claim on an identity, so the next read of it applies whatever its age. */
	acceptAuthority(typeName: string, id: string): void
	/** Queues an entity, and what depends on it, for revalidation. */
	invalidate(typeName: string, id: string): void
	/** Records that an entity no longer exists. */
	remove(typeName: string, id: string): void
}

/** Reads one event's data into a change, or `undefined` when it does not describe one. */
export type ChangeParser = (data: string) => EntityChangeEvent | undefined

export interface ChangeFeedOptions {
	/** Delay before the first reconnection attempt, doubling up to {@link maximumRetryDelayMs}. */
	initialRetryDelayMs?: number
	/** Ceiling on the reconnection delay. */
	maximumRetryDelayMs?: number
	/** How an event's data becomes a change. Defaults to {@link parseJsonChange}. */
	parse?: ChangeParser
}

/**
 * Reads an event whose data is a JSON object carrying at least a `type` and an `id`.
 */
export const parseJsonChange: ChangeParser = (data) => {
	let change: EntityChangeEvent | undefined
	try {
		change = JSON.parse(data) as EntityChangeEvent
	}
	catch {
		return undefined
	}

	return !change?.type || !change.id ? undefined : change
}

const defaults = {
	initialRetryDelayMs: 1_000,
	maximumRetryDelayMs: 30_000,
	parse: parseJsonChange
}

/**
 * Keeps the store in step with writes the frontend did not make.
 *
 * Consumes a `text/event-stream` and turns each event into an invalidation. Strictly an optimization: every
 * guarantee of the repository system except promptness holds with this switched off, which is why a feed that
 * cannot be established or that drops is never an error — it just leaves revalidation to freshness and to
 * whatever the host wires up on surface activation.
 */
export class ChangeFeed {
	private readonly options: Required<ChangeFeedOptions>
	private controller?: AbortController
	private running = false
	private live = false
	private retryDelayMs: number

	public constructor(
		private readonly source: ChangeFeedSource,
		private readonly sink: ChangeFeedSink,
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

			// A connection that delivered nothing is indistinguishable from an unreachable backend, so both
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
			lines = await this.source.open(controller.signal)
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
		this.sink.invalidateAll()
		void this.sink.revalidateObserved()

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

		const change = this.options.parse(data)
		if (!change) {
			return
		}

		// A local edit normally outranks a refresh, so the echo of this client's own write cannot revert it.
		// A critical change is the backend saying otherwise — it is the authority on an entity it reconciled
		// itself, and a removal leaves nothing for an edit to be about — so the local claim is given up and
		// the revalidation that follows is allowed to win.
		if (change.critical) {
			this.sink.acceptAuthority(change.type, change.id)
		}

		// Queued before the removal: what depends on an entity is read off its canonical instance, which a
		// removal is about to drop.
		this.sink.invalidate(change.type, change.id)

		if (change.operation === "Deleted") {
			// Told at once rather than left to the refetch, so a surface showing the entity stops showing it
			// now. The refetch that follows confirms it, or brings the entity back if the announcement was wrong.
			this.sink.remove(change.type, change.id)
		}
	}
}

function delay(milliseconds: number): Promise<void> {
	return new Promise((resolve) => setTimeout(resolve, milliseconds))
}
