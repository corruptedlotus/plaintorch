import { entityKey, type EntityKey, type EntityTypeName } from "./identity"
import type { EntityStore, EntitySubscriber, EntitySubscription } from "./entityStore"

/** Resolves one entity of a repository's type from the core. */
export type EntityFetcher<T> = (id: string) => Promise<T | undefined>

export interface EntityRepositoryOptions {
	/** How long a resolved entity is served from the store before it is revalidated. */
	freshnessMs?: number
}

const defaultFreshnessMs = 30_000

/**
 * Coordinates reads and writes of one entity type against the shared store.
 *
 * The store owns identity; this owns when the core is actually contacted. Concurrent readers of the same
 * identity share a single request, which is what keeps two banners for the same note — one in the editor,
 * one in reading mode — from issuing the same fetch twice.
 */
export class EntityRepository<T extends object> {
	private readonly inFlight = new Map<EntityKey, Promise<T | undefined>>()
	private readonly resolvedAt = new Map<EntityKey, number>()
	private readonly mutating = new Set<EntityKey>()
	private readonly freshnessMs: number

	public constructor(
		protected readonly store: EntityStore,
		public readonly typeName: EntityTypeName,
		private readonly fetcher: EntityFetcher<T>,
		options: EntityRepositoryOptions = {}
	) {
		this.freshnessMs = options.freshnessMs ?? defaultFreshnessMs
	}

	/** Builds the store identity of an entity of this type. */
	public key(id: string): EntityKey {
		return entityKey(this.typeName, id)
	}

	/** Returns the canonical instance without contacting the core. */
	public peek(id: string): T | undefined {
		return this.store.peek<T>(this.key(id))
	}

	/**
	 * Resolves an entity, serving it from the store while it is still fresh.
	 */
	public async get(id: string, options: { force?: boolean } = {}): Promise<T | undefined> {
		const key = this.key(id)
		const pending = this.inFlight.get(key)
		if (pending) {
			return await pending
		}

		if (!options.force && this.isFresh(key)) {
			return this.store.peek<T>(key)
		}

		const request = this.fetcher(id)
			.then((result) => {
				this.resolvedAt.set(key, Date.now())
				// The response was absorbed on the way through the client, so the canonical instance is
				// authoritative and already carries anything a richer earlier fetch had loaded.
				return this.store.peek<T>(key) ?? result
			})
			.finally(() => {
				this.inFlight.delete(key)
			})

		this.inFlight.set(key, request)
		return await request
	}

	/** Resolves an entity, always contacting the core. */
	public async refresh(id: string): Promise<T | undefined> {
		return await this.get(id, { force: true })
	}

	/**
	 * Revalidates an identity only when something is observing it.
	 *
	 * Invalidation always marks an identity stale, but fetching it is pointless when nothing displays it;
	 * the next reader resolves it lazily instead.
	 */
	public async revalidateIfObserved(id: string): Promise<void> {
		const key = this.key(id)
		this.invalidate(id)
		if (this.store.hasSubscribers(key) && !this.mutating.has(key)) {
			await this.refresh(id)
		}
	}

	/** Marks an identity as needing revalidation on next read. */
	public invalidate(id: string): void {
		const key = this.key(id)
		this.resolvedAt.delete(key)
		this.store.markStale(key)
	}

	/** Observes an entity of this type. */
	public subscribe(id: string, subscriber: EntitySubscriber): EntitySubscription {
		return this.store.subscribe(this.key(id), subscriber)
	}

	/** Determines whether a write against an identity is currently in flight. */
	public isMutating(id: string): boolean {
		return this.mutating.has(this.key(id))
	}

	/**
	 * Runs a write against an entity, republishing it afterwards.
	 *
	 * While the write is in flight the identity is held so a change feed event — including the echo of
	 * this very write — cannot refetch over it and clobber an edit the user is still making.
	 */
	public async mutate<R>(id: string, operation: () => Promise<R>): Promise<R> {
		const key = this.key(id)
		this.mutating.add(key)
		try {
			const result = await operation()
			this.resolvedAt.set(key, Date.now())
			return result
		}
		finally {
			this.mutating.delete(key)
		}
	}

	/**
	 * Announces an in-place edit of the canonical instance to everything observing it.
	 */
	public touch(id: string): void {
		this.store.touch(this.key(id))
	}

	private isFresh(key: EntityKey): boolean {
		if (this.store.isStale(key) || !this.store.has(key)) {
			return false
		}

		const resolvedAt = this.resolvedAt.get(key)
		return resolvedAt !== undefined && Date.now() - resolvedAt < this.freshnessMs
	}
}
