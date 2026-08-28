import { entityKey, type EntityKey, type EntityTypeName } from "./identity"
import type { EntityStore, EntitySubscriber, EntitySubscription } from "./entityStore"
import type { InvalidationScheduler } from "./invalidation"
import { EntityDraft } from "./draft"
import { runWrite } from "./mutation"

/** Resolves one entity of a repository's type from the core. */
export type EntityFetcher<T> = (id: string) => Promise<T | undefined>

export interface EntityRepositoryOptions {
	/** How long a resolved entity is served from the store before it is revalidated. */
	freshnessMs?: number
	/** Queues what a successful write makes stale. */
	invalidation?: InvalidationScheduler
}

export interface MutateOptions<R> {
	/**
	 * Field values to restore if the write is rejected, captured before the edit was applied.
	 *
	 * Two-way bindings apply an edit to the canonical instance before anything is sent, so the caller is
	 * the only one who can capture the previous state in time.
	 */
	rollbackTo?: Record<string, unknown>
	/** Whether a result counts as success. Defaults to {@link isSuccessfulMutation}. */
	succeeded?: (result: R) => boolean
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
	private readonly freshnessMs: number
	private readonly invalidation?: InvalidationScheduler

	public constructor(
		protected readonly store: EntityStore,
		public readonly typeName: EntityTypeName,
		private readonly fetcher: EntityFetcher<T>,
		options: EntityRepositoryOptions = {}
	) {
		this.freshnessMs = options.freshnessMs ?? defaultFreshnessMs
		this.invalidation = options.invalidation
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
	 * Opens an isolated working copy of an entity for the fork-and-commit editing mode.
	 *
	 * Undefined when the entity has not been resolved into the store yet — there is nothing to copy.
	 */
	public fork(id: string): EntityDraft<T> | undefined {
		const canonical = this.store.peek<T>(this.key(id))
		if (!canonical) {
			return undefined
		}

		return new EntityDraft<T>(this.store, this.key(id), this.typeName, id, canonical, this.invalidation)
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
		if (this.store.hasSubscribers(key) && !this.store.isWriting(key)) {
			await this.refresh(id)
		}
	}

	/** Revalidates every entity of this type that something is observing, leaving the rest stale. */
	public async revalidateObserved(): Promise<void> {
		const observed = [...this.resolvedAt.keys()].filter((key) => this.store.hasSubscribers(key))
		await Promise.all(observed.map(async (key) => {
			const id = key.slice(key.indexOf(":") + 1)
			if (!this.store.isWriting(key)) {
				await this.refresh(id)
			}
		}))
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
		return this.store.isWriting(this.key(id))
	}

	/**
	 * Runs an imperative write — the path a non-binding action (a canvas edit, an add-to-Polaris) takes.
	 * Undoes it if the core rejects it, and revalidates what it changed on success.
	 *
	 * The shared cycle in {@link runWrite} holds the identity in the store for the duration, so a
	 * revalidation — including the echo of this very write on the change feed — cannot refetch over it.
	 */
	public async mutate<R>(id: string, operation: () => Promise<R>, options: MutateOptions<R> = {}): Promise<R> {
		const { result, ok } = await runWrite(this.store, this.invalidation, this.typeName, id, operation, options)
		if (ok) {
			this.resolvedAt.set(this.key(id), Date.now())
		}

		return result
	}

	/**
	 * Runs a write whose edit is already applied to the canonical instance — the store-direct path the
	 * reference's immediate commit takes. The same guard, invalidation and rollback as {@link mutate},
	 * reporting only whether the core accepted it since a binding's edit is already on screen.
	 */
	public async commit(id: string, send: () => Promise<unknown>, rollbackTo?: Record<string, unknown>): Promise<boolean> {
		const { ok } = await runWrite(this.store, this.invalidation, this.typeName, id, send, { rollbackTo })
		if (ok) {
			this.resolvedAt.set(this.key(id), Date.now())
		}

		return ok
	}

	/** Captures the current field values of an entity so a rejected write can be undone. */
	public snapshot(id: string): Record<string, unknown> | undefined {
		return this.store.snapshot(this.key(id))
	}

	/** Restores field values captured by {@link snapshot}. */
	public rollback(id: string, snapshot: Record<string, unknown> | undefined): void {
		if (snapshot) {
			this.store.restore(this.key(id), snapshot)
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
