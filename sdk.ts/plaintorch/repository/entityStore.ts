import { identify, typeNameFromKey, type EntityKey, type EntityTypeName } from "./identity"
import { isEquivalent } from "./equivalence"

/** Notified when the canonical instance behind an identity changes. */
export type EntitySubscriber = () => void

/** Releases a subscription. */
export type EntitySubscription = () => void

interface EntityRecord {
	readonly key: EntityKey
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
/**
 * What a caller knows about a response when handing it to the store.
 */
export interface AbsorptionContext {
	/**
	 * The store's revision at the moment the request was issued. A response is discarded for any identity
	 * changed since, because it was already out of date before it arrived.
	 */
	readonly issuedAt?: number
	/**
	 * Whether this must be applied even over an unsettled local change. Reserved for changes the core
	 * declares authoritative: the vault reconciling a hand-edited file, and removals.
	 */
	readonly authoritative?: boolean
}

export class EntityStore {
	private readonly records = new Map<EntityKey, EntityRecord>()
	private readonly globalSubscribers = new Set<EntitySubscriber>()
	/** Subscribers to all entities of one type — for filtered, live-collection views (query refs). */
	private readonly typeSubscribers = new Map<EntityTypeName, Set<EntitySubscriber>>()
	/** Keys grouped by type, so a type's members enumerate without scanning the whole store. */
	private readonly typeIndex = new Map<EntityTypeName, Set<EntityKey>>()
	/** Types changed since the last structural flush, notified as a batch on the microtask. */
	private readonly dirtyTypes = new Set<EntityTypeName>()
	/** Set while a coalesced structural notification (store-wide and per-type) is pending on the microtask. */
	private structuralFlushScheduled = false
	/**
	 * Advances on every local change, so a response can be compared against the state it was issued under.
	 *
	 * A revision counter rather than a clock: two events in the same millisecond still order correctly, and
	 * nothing depends on the two sides agreeing about time.
	 */
	private revision = 0
	private readonly changedAt = new Map<EntityKey, number>()
	/** Identities with a write in flight, held so a revalidation does not refetch over an edit mid-write. */
	private readonly writing = new Set<EntityKey>()

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
	public absorb<T>(value: T, payloadKeys?: readonly string[], context?: AbsorptionContext): T {
		const key = identify(value)
		if (key === undefined) {
			return value
		}

		if (this.isSuperseded(key, context)) {
			// The local value is newer than this response. Hand back what is already held rather than
			// reverting an edit the user has made since the request went out.
			return (this.records.get(key)?.value as T) ?? value
		}

		const existing = this.records.get(key)
		if (!existing) {
			const record: EntityRecord = {
				key,
				value: value as object,
				version: 0,
				stale: false,
				subscribers: new Set()
			}
			this.records.set(key, record)
			this.indexKey(key)
			// A new member of its type appeared — let structural and per-type observers recompute.
			this.announce(record)
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

	/** The current revision, to be captured before a request is issued. */
	public get currentRevision(): number {
		return this.revision
	}

	/**
	 * Records that an identity was changed locally, so responses issued before now stop applying to it.
	 *
	 * Called at both ends of a write: on entry, so a read taken while it is in flight is discarded, and on
	 * completion, so one taken during it is too.
	 */
	public noteLocalChange(key: EntityKey): void {
		this.changedAt.set(key, ++this.revision)
	}

	/**
	 * Marks a write against an identity as in flight, and notes the local change.
	 *
	 * The hold lets a repository skip a revalidation while the write runs; the note is what supersedes a read
	 * taken before it. This is the single home of the in-flight guard — every write path opens it here.
	 */
	public beginWrite(key: EntityKey): void {
		this.writing.add(key)
		this.noteLocalChange(key)
	}

	/** Ends the write, noting the local change again so a read taken *during* it is superseded too. */
	public endWrite(key: EntityKey): void {
		this.noteLocalChange(key)
		this.writing.delete(key)
	}

	/** Whether a write against an identity is currently in flight. */
	public isWriting(key: EntityKey): boolean {
		return this.writing.has(key)
	}

	/**
	 * Gives up the local claim on an identity, so the next response applies to it whatever its age.
	 *
	 * For changes the core declares authoritative — the vault reconciling a hand-edited file, or a removal.
	 * Clearing the claim is what makes the following read win, rather than threading a flag from the feed
	 * down through every fetcher.
	 */
	public acceptAuthority(key: EntityKey): void {
		this.changedAt.delete(key)
	}

	/**
	 * Whether a response is older than the local state of an identity.
	 */
	private isSuperseded(key: EntityKey, context?: AbsorptionContext): boolean {
		if (context?.authoritative || context?.issuedAt === undefined) {
			return false
		}

		const changed = this.changedAt.get(key)
		return changed !== undefined && changed > context.issuedAt
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
	 * Observes every entity of one type — a field edit on any member, a new member, or (once eviction lands)
	 * a removal.
	 *
	 * This is what a filtered, live collection subscribes to instead of the whole store, so a change to an
	 * unrelated type does not re-run it. Fired as a batch on the microtask, like the store-wide subscription.
	 */
	public subscribeType(typeName: EntityTypeName, subscriber: EntitySubscriber): EntitySubscription {
		let subscribers = this.typeSubscribers.get(typeName)
		if (!subscribers) {
			subscribers = new Set()
			this.typeSubscribers.set(typeName, subscribers)
		}

		subscribers.add(subscriber)
		return () => {
			subscribers.delete(subscriber)
		}
	}

	/**
	 * Observes several types at once under one subscription — for a structural view built from a fixed set of
	 * kinds, such as the entity grid's directives and incentives or the dependency graph's node types. The
	 * scoped alternative to {@link subscribeAll}: a change to a kind the view does not show never reaches it.
	 */
	public subscribeTypes(typeNames: readonly EntityTypeName[], subscriber: EntitySubscriber): EntitySubscription {
		const subscriptions = typeNames.map((typeName) => this.subscribeType(typeName, subscriber))
		return () => {
			for (const unsubscribe of subscriptions) {
				unsubscribe()
			}
		}
	}

	/** Every resolved entity of a type currently held, for a query to scan and filter. */
	public entitiesOfType<T>(typeName: EntityTypeName): T[] {
		const keys = this.typeIndex.get(typeName)
		if (!keys) {
			return []
		}

		const entities: T[] = []
		for (const key of keys) {
			const value = this.records.get(key)?.value
			if (value) {
				entities.push(value as T)
			}
		}

		return entities
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

		// The instance was edited in place, which is a local change like any other: a response already in
		// flight predates it and must not be allowed to put the old value back.
		this.noteLocalChange(key)
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

		this.noteLocalChange(key)

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
			key,
			value: undefined,
			version: 0,
			stale: true,
			subscribers: new Set()
		}
		this.records.set(key, record)
		this.indexKey(key)
		return record
	}

	/** Records a key under its type, so the type's members can be enumerated without a full scan. */
	private indexKey(key: EntityKey): void {
		const typeName = typeNameFromKey(key)
		let keys = this.typeIndex.get(typeName)
		if (!keys) {
			keys = new Set()
			this.typeIndex.set(typeName, keys)
		}

		keys.add(key)
	}

	/**
	 * Tells everything observing an identity that it changed, and schedules a store-wide notification.
	 *
	 * Per-identity subscribers fire synchronously — they are cheap and targeted, and a surface editing its
	 * own field should update at once. The set is copied first: a subscriber is free to release its
	 * subscription while being notified, which a live iteration would not survive.
	 */
	private announce(record: EntityRecord): void {
		for (const subscriber of [...record.subscribers]) {
			subscriber()
		}

		this.scheduleStructuralAnnounce(typeNameFromKey(record.key))
	}

	/**
	 * Notifies structural subscribers — store-wide and per-type — once per microtask rather than once per
	 * change.
	 *
	 * A single response absorbs many entities, each announcing as it lands, and a structural subscriber reacts
	 * to any of them — so notifying it per change makes it recompute N times for one response. Coalescing
	 * collapses the burst into one notification: the store-wide subscribers fire once, and each type that
	 * changed notifies its own subscribers once.
	 *
	 * Nothing is scheduled when nothing observes the store structurally, the common case, so there is no
	 * per-change overhead otherwise.
	 */
	private scheduleStructuralAnnounce(typeName: EntityTypeName): void {
		if (this.globalSubscribers.size === 0 && this.typeSubscribers.size === 0) {
			return
		}

		this.dirtyTypes.add(typeName)
		if (this.structuralFlushScheduled) {
			return
		}

		this.structuralFlushScheduled = true
		queueMicrotask(() => {
			this.structuralFlushScheduled = false
			// Read at flush time, so a subscription released before the flush is honoured, and one added
			// during the burst is included.
			const types = [...this.dirtyTypes]
			this.dirtyTypes.clear()

			for (const subscriber of [...this.globalSubscribers]) {
				subscriber()
			}

			for (const changed of types) {
				const subscribers = this.typeSubscribers.get(changed)
				if (subscribers) {
					for (const subscriber of [...subscribers]) {
						subscriber()
					}
				}
			}
		})
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
