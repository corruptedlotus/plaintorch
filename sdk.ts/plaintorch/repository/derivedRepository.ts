import { detachFrom } from "./detach"
import type { EntityRemoval, EntityStore, EntitySubscriber, EntitySubscription } from "./entityStore"
import { unresolved, type Unresolved } from "./outcome"

/**
 * Resolves one derived record from the backend.
 *
 * `undefined` is an answer: the view is empty, or there is nothing under that key. {@link unresolved} is the
 * absence of one — the backend could not be reached — and leaves the record as it was.
 */
export type DerivedFetcher<T> = (key: string) => Promise<T | undefined | Unresolved>

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
 *
 * The store is what tells a record whether it changed: a re-resolved view is compared with the entities
 * inside it taken by identity, so a listing of the same members in a fresh array notifies nobody.
 */
export class DerivedRepository<T> {
	private readonly records = new Map<string, DerivedRecord<T>>()
	private readonly inFlight = new Map<string, Promise<T | undefined>>()
	private readonly freshnessMs: number

	public constructor(
		private readonly store: EntityStore,
		private readonly fetcher: DerivedFetcher<T>,
		options: DerivedRepositoryOptions = {}
	) {
		this.freshnessMs = options.freshnessMs ?? defaultFreshnessMs
		store.subscribeRemovals((removal) => this.release(removal))
	}

	/**
	 * Lets go of a removed entity, when the store's policy says its holders should.
	 *
	 * A listing that held it is replaced by one without it, and its observers are told, so the row is gone the
	 * moment the entity is — not when the listing is next refetched. A record that *was* the entity, such as
	 * "the current one", resolves to nothing and is left stale, since which entity it now names is a question
	 * only the backend can answer.
	 */
	private release(removal: EntityRemoval): void {
		const { instance } = removal
		if (!removal.detach || instance === undefined) {
			return
		}

		for (const record of this.records.values()) {
			if (!record.resolved || record.value === undefined) {
				continue
			}

			if (record.value === instance) {
				record.value = undefined
				record.stale = true
			}
			else {
				const detached = detachFrom(record.value, instance, this.store.identify)
				if (!detached.changed) {
					continue
				}

				record.value = detached.value
			}

			for (const subscriber of [...record.subscribers]) {
				subscriber()
			}
		}
	}

	/** Returns the cached record without contacting the backend. */
	public peek(key = ""): T | undefined {
		return this.records.get(key)?.value
	}

	/**
	 * Whether a record has ever been answered for. A record resolved to nothing is still resolved, which is
	 * what tells "the backend says this is empty" from "not loaded yet".
	 */
	public isResolved(key = ""): boolean {
		return this.records.get(key)?.resolved ?? false
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
				if (value === unresolved) {
					// The backend did not answer. The last resolved value stays on screen rather than being
					// replaced by emptiness, nobody is told of a change no one made, and the record is left
					// stale so the next read tries again.
					const kept = this.records.get(key)
					if (kept) {
						kept.stale = true
					}

					return kept?.value
				}

				this.commit(key, value)
				return value
			})
			.finally(() => {
				this.inFlight.delete(key)
			})

		this.inFlight.set(key, request)
		return await request
	}

	/** Resolves a record, always contacting the backend. */
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

	/**
	 * Every resolved cached value, observed or not.
	 *
	 * A cached view holds its entities' canonical instances for as long as the cache does, so eviction must
	 * treat all of these as roots — even an unobserved listing still pins what it contains, and dropping a
	 * store entity it holds would strand a divergent copy.
	 */
	public resolvedValues(): T[] {
		const values: T[] = []
		for (const record of this.records.values()) {
			if (record.resolved && record.value !== undefined) {
				values.push(record.value)
			}
		}

		return values
	}

	/**
	 * Drops the records nothing observes any more and whose value would not be served again anyway — stale,
	 * past their freshness window, or never resolved. Returns how many were dropped.
	 *
	 * This is what bounds a repository keyed by something open-ended, such as a path: without it every key ever
	 * asked for is kept for the life of the session, and each keeps the entities inside it from being evicted.
	 * A record still being fetched is left alone, since its reader is about to be handed the result.
	 */
	public prune(): number {
		const now = Date.now()
		let dropped = 0
		for (const [key, record] of this.records) {
			if (record.subscribers.size > 0 || this.inFlight.has(key)) {
				continue
			}

			if (!record.resolved || record.stale || now - record.resolvedAt >= this.freshnessMs) {
				this.records.delete(key)
				dropped++
			}
		}

		return dropped
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
		const changed = !record.resolved || !this.store.isEquivalent(record.value, value)
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
