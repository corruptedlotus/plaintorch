import type { ChangeFeedSink } from "./changeFeed"
import { DerivedRepository, type DerivedFetcher, type DerivedRepositoryOptions } from "./derivedRepository"
import { EntityRepository, type EntityFetcher, type EntityRepositoryOptions } from "./entityRepository"
import type { EntityStore } from "./entityStore"
import { collectEntityKeys, entityKey, type EntityKey, type EntityTypeName } from "./identity"
import { InvalidationScheduler, type DependentTable, type InvalidationTarget } from "./invalidation"

/**
 * How often the periodic eviction sweep runs once started. Eviction is housekeeping with no urgency — a
 * long session only needs the identity map bounded, not trimmed to the second — so the default is coarse.
 */
export const defaultEvictionSweepIntervalMs = 60_000

export interface RepositorySetOptions {
	/** What a change to each entity type makes stale beyond itself. */
	dependents?: DependentTable
}

/**
 * The repositories of one store, and everything that has to know about all of them at once.
 *
 * A repository is created *through* the set — {@link entity} for a tracked type, {@link derived} for a view —
 * and that is what registers it: an entity repository becomes routable by its type name, and a derived one
 * becomes a record that is revalidated with the rest and counted as a root when evicting. Neither can be
 * added without the invalidation, the change feed and the sweep finding it, which is the point: an entity type
 * nothing routes is never refreshed, and a listing the sweep cannot see has its entities evicted from under it.
 *
 * Use it directly, or extend it to give the repositories names.
 */
export class RepositorySet implements InvalidationTarget, ChangeFeedSink {
	/** Queues what a write makes stale and revalidates whatever of it is on screen. */
	public readonly invalidation: InvalidationScheduler

	private readonly byTypeName = new Map<EntityTypeName, EntityRepository<never>>()
	private readonly records: DerivedRepository<unknown>[] = []
	private evictionTimer?: ReturnType<typeof setInterval>

	public constructor(public readonly store: EntityStore, options: RepositorySetOptions = {}) {
		// Resolved lazily: the scheduler reaches back into the set it is handed to.
		this.invalidation = new InvalidationScheduler(store, () => this, options.dependents)
	}

	/**
	 * Creates the repository of one entity type and routes its type name to it.
	 *
	 * Refuses a type the store's policy has no model for. Such a type would be absorbed as a prototype-less
	 * plain object, which surfaces later and far away, as a missing getter or a value that is not an instance
	 * of its class; failing here names the cause.
	 */
	public entity<T extends object>(
		typeName: EntityTypeName,
		fetcher: EntityFetcher<T>,
		options: Omit<EntityRepositoryOptions, "invalidation"> = {}
	): EntityRepository<T> {
		if (!this.store.policy.models.isRegistered(typeName)) {
			throw new Error(
				`Entity type "${typeName}" is routed to a repository but the store's policy has no model registered for it, `
				+ "so absorption would track it as a prototype-less plain object."
			)
		}

		const repository = new EntityRepository<T>(this.store, typeName, fetcher, { ...options, invalidation: this.invalidation })
		this.byTypeName.set(typeName, repository as EntityRepository<never>)
		return repository
	}

	/** Creates a derived repository over this set's store and counts it among the set's records. */
	public derived<T>(fetcher: DerivedFetcher<T>, options?: DerivedRepositoryOptions): DerivedRepository<T> {
		const repository = new DerivedRepository<T>(this.store, fetcher, options)
		this.records.push(repository as DerivedRepository<unknown>)
		return repository
	}

	/**
	 * Resolves the repository responsible for a runtime type name.
	 *
	 * Lets a generic surface — one that only knows which kind of entity it is rendering — resolve and
	 * observe it without a switch over every type.
	 */
	public forTypeName<T extends object>(typeName: string | undefined): EntityRepository<T> | undefined {
		return typeName === undefined
			? undefined
			: this.byTypeName.get(typeName) as EntityRepository<T> | undefined
	}

	/**
	 * Revalidates every derived record that something is observing.
	 *
	 * Used as the coarse fallback when a promptness signal is unavailable — on surface activation, or when
	 * a change feed reconnects after missing events.
	 */
	public async revalidateObservedRecords(): Promise<void> {
		await Promise.all(this.records.map(async (record) => await record.revalidateObserved()))
	}

	/** @inheritdoc */
	public async revalidateRecordsIfObserved(): Promise<void> {
		await this.revalidateObservedRecords()
	}

	/**
	 * @inheritdoc
	 *
	 * Routed by type name so the scheduler can name a dependency of any kind without knowing which
	 * repository serves it.
	 */
	public async revalidateEntityIfObserved(key: EntityKey): Promise<void> {
		const separator = key.indexOf(":")
		const typeName = key.slice(0, separator)
		const id = key.slice(separator + 1)
		await this.forTypeName(typeName)?.revalidateIfObserved(id)
	}

	/**
	 * Revalidates everything currently on screen — entities and records alike.
	 *
	 * The coarse recovery path, for when promptness cannot be relied on: a change feed reconnecting after
	 * missing events, or a host waking a surface back up.
	 */
	public async revalidateObserved(): Promise<void> {
		await Promise.all([
			...[...this.byTypeName.values()].map(async (repository) => await repository.revalidateObserved()),
			this.revalidateObservedRecords()
		])
	}

	/** Queues an entity, and everything that depends on it, for revalidation. */
	public invalidate(typeName: string, id: string): void {
		this.invalidation.invalidate(typeName, id)
	}

	/**
	 * Gives up the local claim on an identity, so the next read of it applies whatever its age.
	 *
	 * Used for changes the backend declares authoritative. A write still in flight keeps its own protection —
	 * this only concedes the ordering, it does not interrupt anything.
	 */
	public acceptAuthority(typeName: string, id: string): void {
		this.store.acceptAuthority(entityKey(typeName, id))
	}

	/**
	 * Evicts entities the identity map no longer needs, bounding it over a long-lived session.
	 *
	 * The reachable set is everything a live view still holds — walked from every resolved derived record,
	 * whose cached value pins its entities' canonical instances whether or not it is observed. The store adds
	 * its own subscribed entities (and their nested references) and evicts the rest. This is the entity half
	 * of the eviction policy; the change-ledger half prunes itself as reads settle.
	 *
	 * Only entities held nowhere are dropped — one fetched for a surface since closed, or one dropped from a
	 * listing that has since refreshed. Anything a derived view or subscription still reaches is kept, so no
	 * surface can be left holding an instance a later fetch would duplicate. Returns how many were evicted.
	 */
	public sweep(): number {
		const reachable = new Set<EntityKey>()
		for (const record of this.records) {
			for (const value of record.resolvedValues()) {
				collectEntityKeys(value, reachable, this.store.identify)
			}
		}

		return this.store.sweep(reachable)
	}

	/**
	 * Starts sweeping on a periodic interval.
	 *
	 * Host-started like a change feed: the mechanism is here, the host decides when a session is live enough
	 * to want it. Idempotent — a second start is ignored rather than stacking a second interval. The first
	 * sweep runs one interval in, not immediately, since nothing has accumulated at start.
	 */
	public startEvictionSweep(intervalMs: number = defaultEvictionSweepIntervalMs): void {
		if (this.evictionTimer !== undefined) {
			return
		}

		this.evictionTimer = setInterval(() => this.sweep(), intervalMs)
		// Housekeeping alone should never hold a Node process alive; harmless where unref does not exist.
		;(this.evictionTimer as { unref?: () => void }).unref?.()
	}

	/** Stops the periodic eviction sweep. Safe to call when it is not running. */
	public stopEvictionSweep(): void {
		if (this.evictionTimer === undefined) {
			return
		}

		clearInterval(this.evictionTimer)
		this.evictionTimer = undefined
	}

	/** Whether the periodic eviction sweep is currently running. */
	public get evictionSweepRunning(): boolean {
		return this.evictionTimer !== undefined
	}

	/** Marks everything as needing revalidation, without fetching anything. */
	public invalidateAll(): void {
		this.store.markAllStale()
		for (const record of this.records) {
			record.invalidate()
		}
	}
}
