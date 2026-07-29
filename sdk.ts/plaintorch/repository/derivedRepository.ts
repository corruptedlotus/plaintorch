import { isEquivalent } from "./equivalence"
import type { EntitySubscriber, EntitySubscription } from "./entityStore"

/** Resolves one derived record from the core. */
export type DerivedFetcher<T> = (key: string) => Promise<T | undefined>

export interface DerivedRepositoryOptions {
	/** How long a resolved record is served before it is revalidated. */
	freshnessMs?: number
}

interface DerivedRecord<T> {
	value: T | undefined
	resolvedAt: number
	resolved: boolean
	stale: boolean
	readonly subscribers: Set<EntitySubscriber>
}

const defaultFreshnessMs = 15_000

/**
 * Caches views over entities rather than entities themselves — the briefing, a note resolution, a PUCK
 * resolution.
 *
 * These have no identity of their own, so they are cached under a key of the caller's choosing. The
 * entities they contain are still absorbed on the way through the client, so a record and the surfaces
 * showing its contents individually agree without either knowing about the other.
 */
export class DerivedRepository<T> {
	private readonly records = new Map<string, DerivedRecord<T>>()
	private readonly inFlight = new Map<string, Promise<T | undefined>>()
	private readonly freshnessMs: number

	public constructor(
		private readonly fetcher: DerivedFetcher<T>,
		options: DerivedRepositoryOptions = {}
	) {
		this.freshnessMs = options.freshnessMs ?? defaultFreshnessMs
	}

	/** Returns the cached record without contacting the core. */
	public peek(key = ""): T | undefined {
		return this.records.get(key)?.value
	}

	/**
	 * Resolves a record, serving it from cache while it is still fresh.
	 */
	public async get(key = "", options: { force?: boolean } = {}): Promise<T | undefined> {
		const pending = this.inFlight.get(key)
		if (pending) {
			return await pending
		}

		const record = this.records.get(key)
		if (!options.force && record?.resolved && !record.stale && Date.now() - record.resolvedAt < this.freshnessMs) {
			return record.value
		}

		const request = this.fetcher(key)
			.then((value) => {
				this.commit(key, value)
				return value
			})
			.finally(() => {
				this.inFlight.delete(key)
			})

		this.inFlight.set(key, request)
		return await request
	}

	/** Resolves a record, always contacting the core. */
	public async refresh(key = ""): Promise<T | undefined> {
		return await this.get(key, { force: true })
	}

	/** Observes a record. */
	public subscribe(key: string, subscriber: EntitySubscriber): EntitySubscription {
		const record = this.records.get(key) ?? this.create(key)
		record.subscribers.add(subscriber)
		return () => {
			record.subscribers.delete(subscriber)
		}
	}

	/** Determines whether anything is currently observing a record. */
	public hasSubscribers(key = ""): boolean {
		const record = this.records.get(key)
		return !!record && record.subscribers.size > 0
	}

	/** Marks a record — or every record — as needing revalidation. */
	public invalidate(key?: string): void {
		if (key === undefined) {
			for (const record of this.records.values()) {
				record.stale = true
			}
			return
		}

		const record = this.records.get(key)
		if (record) {
			record.stale = true
		}
	}

	/**
	 * Revalidates a record only when something is observing it.
	 */
	public async revalidateIfObserved(key = ""): Promise<void> {
		this.invalidate(key)
		if (this.hasSubscribers(key)) {
			await this.refresh(key)
		}
	}

	/** Revalidates every observed record, leaving the rest stale. */
	public async revalidateObserved(): Promise<void> {
		const observed = [...this.records.entries()].filter(([, record]) => record.subscribers.size > 0)
		this.invalidate()
		await Promise.all(observed.map(async ([key]) => await this.refresh(key)))
	}

	private commit(key: string, value: T | undefined): void {
		const record = this.records.get(key) ?? this.create(key)
		const changed = !record.resolved || !isEquivalent(record.value, value)
		record.value = value
		record.resolvedAt = Date.now()
		record.resolved = true
		record.stale = false

		if (changed) {
			for (const subscriber of [...record.subscribers]) {
				subscriber()
			}
		}
	}

	private create(key: string): DerivedRecord<T> {
		const record: DerivedRecord<T> = {
			value: undefined,
			resolvedAt: 0,
			resolved: false,
			stale: true,
			subscribers: new Set()
		}
		this.records.set(key, record)
		return record
	}
}
