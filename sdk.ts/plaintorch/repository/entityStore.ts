import { identify, type EntityKey } from "./identity"
import { isEquivalent } from "./equivalence"

/** Notified when the canonical instance behind an identity changes. */
export type EntitySubscriber = () => void

/** Releases a subscription. */
export type EntitySubscription = () => void

interface EntityRecord {
	/** Undefined while an identity is observed but has not been absorbed yet. */
	value: object | undefined
	version: number
	stale: boolean
	readonly subscribers: Set<EntitySubscriber>
}

/**
 * Holds exactly one canonical instance per entity identity.
 *
 * Responses are absorbed rather than handed out directly: an entity already in the store has the
 * incoming fields merged into the instance that is already on screen, so every surface holding a
 * reference is looking at current state by construction. Synchronizing surfaces is then only a matter
 * of telling them to re-render, which is what subscriptions are for.
 */
export class EntityStore {
	private readonly records = new Map<EntityKey, EntityRecord>()
	private readonly globalSubscribers = new Set<EntitySubscriber>()

	/**
	 * Registers or merges a value and returns the canonical instance for its identity.
	 *
	 * Values that are not tracked entities are returned untouched, which lets the caller run this over
	 * every node of a response without knowing which ones matter.
	 *
	 * `payloadKeys` must be the keys the wire payload actually carried. Constructing a model widens a
	 * sparse response to the full shape of its class — declared fields appear as `undefined`, and
	 * initialized ones as their defaults — so without it a response that merely omits a field would
	 * overwrite the cached value with a default.
	 */
	public absorb<T>(value: T, payloadKeys?: readonly string[]): T {
		const key = identify(value)
		if (key === undefined) {
			return value
		}

		const existing = this.records.get(key)
		if (!existing) {
			this.records.set(key, {
				value: value as object,
				version: 0,
				stale: false,
				subscribers: new Set()
			})
			return value
		}

		if (!existing.value) {
			existing.value = value as object
			existing.stale = false
			existing.version++
			this.announce(existing)
			return value
		}

		const changed = mergeInto(existing.value, value as object, payloadKeys)
		existing.stale = false
		if (changed) {
			existing.version++
			this.announce(existing)
		}

		return existing.value as T
	}

	/**
	 * Returns the canonical instance for an identity without fetching it.
	 */
	public peek<T>(key: EntityKey): T | undefined {
		return this.records.get(key)?.value as T | undefined
	}

	/**
	 * Determines whether an identity has ever been absorbed.
	 */
	public has(key: EntityKey): boolean {
		return this.records.get(key)?.value !== undefined
	}

	/**
	 * Returns the change counter of an identity, which advances only when a field actually changed.
	 */
	public version(key: EntityKey): number {
		return this.records.get(key)?.version ?? 0
	}

	/**
	 * Observes an identity. The subscriber runs whenever the canonical instance changes.
	 */
	public subscribe(key: EntityKey, subscriber: EntitySubscriber): EntitySubscription {
		const record = this.records.get(key) ?? this.createPlaceholder(key)
		record.subscribers.add(subscriber)
		return () => {
			record.subscribers.delete(subscriber)
		}
	}

	/**
	 * Observes every identity at once.
	 *
	 * For surfaces whose shape depends on entity fields rather than on one entity — a tree built from
	 * parent references restructures when any member is reparented, which no per-identity subscription
	 * would report, since the entity that moved is still the same entity.
	 */
	public subscribeAll(subscriber: EntitySubscriber): EntitySubscription {
		this.globalSubscribers.add(subscriber)
		return () => {
			this.globalSubscribers.delete(subscriber)
		}
	}

	/**
	 * Determines whether anything is currently observing an identity.
	 *
	 * Invalidation uses this to refetch only what is on screen; everything else is left marked stale
	 * and resolved lazily if it is ever displayed.
	 */
	public hasSubscribers(key: EntityKey): boolean {
		const record = this.records.get(key)
		return !!record && record.subscribers.size > 0
	}

	/**
	 * Announces that the canonical instance of an identity was changed in place.
	 *
	 * Two-way bindings write straight through to the instance they were handed, so the change is already
	 * applied by the time anything hears about it and there is nothing left for {@link absorb} to detect.
	 * This is how such an edit still reaches the other surfaces showing the same entity.
	 */
	public touch(key: EntityKey): void {
		const record = this.records.get(key)
		if (!record?.value) {
			return
		}

		record.version++
		this.announce(record)
	}

	/** Marks an identity as needing revalidation on next read. */
	public markStale(key: EntityKey): void {
		const record = this.records.get(key)
		if (record) {
			record.stale = true
		}
	}

	/** Marks every known identity as needing revalidation, used when a change feed reconnects. */
	public markAllStale(): void {
		for (const record of this.records.values()) {
			record.stale = true
		}
	}

	/** Determines whether an identity needs revalidation. */
	public isStale(key: EntityKey): boolean {
		return this.records.get(key)?.stale ?? true
	}

	/** Returns every identity currently held. */
	public keys(): IterableIterator<EntityKey> {
		return this.records.keys()
	}

	/**
	 * Captures the current field values of an identity so an optimistic write can be undone.
	 */
	public snapshot(key: EntityKey): Record<string, unknown> | undefined {
		const record = this.records.get(key)
		return record?.value ? { ...record.value } : undefined
	}

	/**
	 * Applies field values captured by {@link snapshot}, rolling back a failed optimistic write.
	 */
	public restore(key: EntityKey, snapshot: Record<string, unknown>): void {
		const record = this.records.get(key)
		if (!record?.value) {
			return
		}

		if (mergeInto(record.value, snapshot)) {
			record.version++
			this.announce(record)
		}
	}

	/**
	 * Applies a partial change to the canonical instance of an identity, as an optimistic write does.
	 */
	public patch(key: EntityKey, change: Record<string, unknown>): void {
		const record = this.records.get(key)
		if (!record?.value) {
			return
		}

		if (mergeInto(record.value, change)) {
			record.version++
			this.announce(record)
		}
	}

	/**
	 * Registers an identity that is observed but not absorbed yet, so a component can subscribe before
	 * its first fetch settles. The first absorption adopts the incoming instance verbatim, which keeps
	 * the constructed model prototype intact.
	 */
	private createPlaceholder(key: EntityKey): EntityRecord {
		const record: EntityRecord = {
			value: undefined,
			version: 0,
			stale: true,
			subscribers: new Set()
		}
		this.records.set(key, record)
		return record
	}

	/**
	 * Tells everything observing an identity, and everything observing the store, that it changed.
	 *
	 * Subscriber sets are copied first: a subscriber is free to release its subscription while being
	 * notified, which a live iteration would not survive.
	 */
	private announce(record: EntityRecord): void {
		for (const subscriber of [...record.subscribers]) {
			subscriber()
		}

		for (const subscriber of [...this.globalSubscribers]) {
			subscriber()
		}
	}
}

/**
 * Copies the named fields of `source` onto `target`, reporting whether anything actually changed.
 *
 * Restricting the copy to keys the payload carried is what keeps a sparse response from erasing richer
 * cached fields, and unchanged values are left in place so array and object references stay stable
 * across refetches.
 */
function mergeInto(target: object, source: object, keys?: readonly string[]): boolean {
	let changed = false
	for (const key of keys ?? Object.keys(source)) {
		if (!isAssignable(target, key)) {
			continue
		}

		const value = (source as Record<string, unknown>)[key]
		const current = (target as Record<string, unknown>)[key]
		if (isEquivalent(current, value)) {
			continue
		}

		;(target as Record<string, unknown>)[key] = value
		changed = true
	}

	return changed
}

/**
 * Determines whether a key can be written on a target, so getter-only fields are left alone.
 */
function isAssignable(target: object, key: string): boolean {
	let current: object | null = target
	while (current) {
		const descriptor = Object.getOwnPropertyDescriptor(current, key)
		if (descriptor) {
			return descriptor.writable === true || typeof descriptor.set === "function"
		}

		current = Object.getPrototypeOf(current)
	}

	return true
}
