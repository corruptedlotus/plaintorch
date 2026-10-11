import { collectEntityKeys, typeNameFromKey, type EntityIdentifier, type EntityKey, type EntityTypeName } from "./identity"
import { isEquivalent } from "./equivalence"
import { resolvePolicy, type EntityPolicy, type ResolvedEntityPolicy } from "./policy"
import { createAbsorbingReviver } from "./absorption"
import { detachFrom } from "./detach"

/** Notified when the canonical instance behind an identity changes. */
export type EntitySubscriber = () => void

/** Releases a subscription. */
export type EntitySubscription = () => void

/**
 * What the store knows of an identity's data.
 *
 * - `not-loaded` — nothing has answered for it yet: never asked for, still being fetched, or asked of a backend
 *   that did not answer.
 * - `loaded` — the store holds its canonical instance.
 * - `removed` — the backend answered that there is no such entity: it was deleted, or never existed.
 *
 * These are facts about the data, apart from whether a request is in flight or one failed.
 */
export type EntityState = "not-loaded" | "loaded" | "removed"

interface EntityRecord {
	readonly key: EntityKey
	/** Undefined while an identity is observed but has not been absorbed yet, and once it has been removed. */
	value: object | undefined
	/** Whether the backend answered that this identity does not exist. Cleared when it is absorbed again. */
	removed: boolean
	version: number
	stale: boolean
	/** When a repository last resolved this identity from the backend, for its freshness window. */
	resolvedAt?: number
	readonly subscribers: Set<EntitySubscriber>
}

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

/** What a store tells those listening for removals. */
export interface EntityRemoval {
	readonly key: EntityKey
	/** The instance that was canonical until now, or `undefined` when the identity was never loaded. */
	readonly instance: object | undefined
	/** Whether what holds the instance is to let go of it, as the store's policy says for this type. */
	readonly detach: boolean
}

/** Told when an identity is removed. */
export type EntityRemovalListener = (removal: EntityRemoval) => void

/**
 * Parses a response body into canonical instances, absorbing every entity in it under the read it was handed
 * out for. See {@link EntityStore.read}.
 */
export type AbsorbingParser = <T>(text: string) => T

/**
 * Holds exactly one canonical instance per entity identity.
 *
 * Responses are absorbed rather than handed out directly: an entity already in the store has the
 * incoming fields merged into the instance that is already on screen, so every surface holding a
 * reference is looking at current state by construction. Synchronizing surfaces is then only a matter
 * of telling them to re-render, which is what subscriptions are for.
 *
 * The store knows nothing about the domain it holds. What an entity is, how a payload becomes a model, and
 * which of a payload's fields are trusted are all told to it once, as its {@link EntityPolicy}.
 */
export class EntityStore {
	/** The domain policy this store was built with, defaults filled in. */
	public readonly policy: ResolvedEntityPolicy
	/**
	 * Resolves the store identity of a value, or `undefined` when it is not a tracked entity. The policy's
	 * identifier, held as a property so it can be handed on as a plain function.
	 */
	public readonly identify: EntityIdentifier
	private readonly records = new Map<EntityKey, EntityRecord>()
	private readonly globalSubscribers = new Set<EntitySubscriber>()
	/** Those holding entities outside the store's own records — derived repositories — told of each removal. */
	private readonly removalListeners = new Set<EntityRemovalListener>()
	/**
	 * The instances that were canonical when their entity was removed. Held weakly, so the mark lasts exactly
	 * as long as something still holds the instance, and costs nothing once nothing does.
	 */
	private readonly removedInstances = new WeakSet<object>()
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
	 * Response-absorbing reads currently in flight, counted by the revision they were issued at (a multiset:
	 * several reads can share one revision, and each must be released on its own).
	 *
	 * A {@link changedAt} marker at revision r only ever discards a response *issued before* r. So once no
	 * in-flight read was issued before r, that marker can never drop a response again — new reads capture the
	 * current, higher revision, so none will ever be older. The oldest revision here is therefore the ledger's
	 * prune cutoff; this is what keeps `changedAt` bounded over a long-lived session.
	 */
	private readonly outstandingReads = new Map<number, number>()

	public constructor(policy: EntityPolicy) {
		this.policy = resolvePolicy(policy)
		this.identify = this.policy.identify
	}

	/**
	 * Determines whether an existing value is indistinguishable from an incoming one, with entities compared
	 * by identity as this store recognizes them.
	 */
	public isEquivalent(a: unknown, b: unknown): boolean {
		return isEquivalent(a, b, this.identify)
	}

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
		const key = this.identify(value)
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
				removed: false,
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
			// Observed before it was ever absorbed, or removed and now back: either way the incoming instance
			// is adopted as it is, which keeps its constructed prototype intact.
			existing.value = value as object
			existing.removed = false
			existing.stale = false
			existing.version++
			this.announce(existing)
			return value
		}

		const changed = mergeInto(existing.value, value as object, this.identify, payloadKeys)
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
	 * Determines whether the store currently holds an instance for an identity.
	 */
	public has(key: EntityKey): boolean {
		return this.records.get(key)?.value !== undefined
	}

	/**
	 * What the store knows of an identity's data. An identity the store has never heard of is `not-loaded`.
	 */
	public stateOf(key: EntityKey): EntityState {
		const record = this.records.get(key)
		if (record?.value !== undefined) {
			return "loaded"
		}

		return record?.removed ? "removed" : "not-loaded"
	}

	/**
	 * Whether a value someone is holding is an entity that has been removed.
	 *
	 * This is the question for whatever holds an entity *by reference* rather than by identifier — a single
	 * reference on another entity, a property a component was handed — and so has an object in hand, not an
	 * id to look up. The instance that was canonical when its entity was removed stays marked for as long as
	 * anything holds it, whether or not the identity is still observed, and stays marked if the identity is
	 * later absorbed again: that is a new instance, and the old one is still of an entity that was removed.
	 * A copy of a removed identity that was never canonical — one a response issued before the removal carried
	 * in — reads as removed too, while the identity is.
	 */
	public wasRemoved(value: unknown): boolean {
		if (value === null || typeof value !== "object") {
			return false
		}

		if (this.removedInstances.has(value)) {
			return true
		}

		const key = this.identify(value)
		return key !== undefined && this.records.get(key)?.removed === true
	}

	/**
	 * Returns the change counter of an identity, which advances only when a field actually changed.
	 */
	public version(key: EntityKey): number {
		return this.records.get(key)?.version ?? 0
	}

	/**
	 * Records that the backend has no such entity: drops the canonical instance and tells everything observing
	 * the identity, which then reads it as `removed`.
	 *
	 * Whoever still holds the old instance must let go of it on that notification — it is no longer canonical,
	 * and an entity absorbed under the same identity later is a new instance. An identity nothing observes
	 * leaves no trace beyond its ledger marker.
	 *
	 * The removal is noted like a local change, so a response issued before it — a listing still in flight that
	 * names the entity — is discarded for this identity rather than bringing it back.
	 */
	public remove(key: EntityKey): void {
		const record = this.records.get(key)
		if (!record || record.removed) {
			return
		}

		const instance = record.value
		const existed = instance !== undefined
		if (instance) {
			this.removedInstances.add(instance)
		}

		this.noteLocalChange(key)
		record.value = undefined
		record.removed = true
		record.stale = true
		record.resolvedAt = undefined
		record.version++

		if (record.subscribers.size === 0) {
			this.records.delete(key)
			this.removeFromIndex(key)
		}

		// The collections that held it let go before anyone is told, so an observer re-rendering on the
		// notification already finds the entity gone from everything it reads.
		const detach = instance !== undefined && this.policy.removal(typeNameFromKey(key)) === "detach"
		const owners = detach ? this.detachFromOwners(instance) : []

		if (existed || record.subscribers.size > 0) {
			this.announce(record)
		}

		for (const owner of owners) {
			this.announce(owner)
		}

		for (const listener of [...this.removalListeners]) {
			listener({ key, instance, detach })
		}
	}

	/**
	 * Listens for removals. For whatever holds entities outside the store's own records — a derived repository
	 * and its cached views — so it can let go of a removed instance as the store's entities do.
	 */
	public subscribeRemovals(listener: EntityRemovalListener): EntitySubscription {
		this.removalListeners.add(listener)
		return () => {
			this.removalListeners.delete(listener)
		}
	}

	/**
	 * Takes a removed instance out of the collections of every entity that still holds it, and returns the
	 * records that changed. Each is left stale as well: losing a member is a change the backend made, and
	 * whatever else it changed about the owner has not been read yet.
	 */
	private detachFromOwners(instance: object): EntityRecord[] {
		const changed: EntityRecord[] = []
		for (const owner of this.records.values()) {
			if (owner.value && detachFrom(owner.value, instance, this.identify).changed) {
				owner.version++
				owner.stale = true
				changed.push(owner)
			}
		}

		return changed
	}

	/**
	 * Records that a repository has just resolved an identity from the backend. Kept on the record rather than
	 * by the repository, so the stamp is evicted with the entity it describes.
	 */
	public markResolved(key: EntityKey, at: number = Date.now()): void {
		const record = this.records.get(key)
		if (record?.value !== undefined) {
			record.resolvedAt = at
		}
	}

	/** When a repository last resolved an identity from the backend, or `undefined` if none has. */
	public resolvedAt(key: EntityKey): number | undefined {
		return this.records.get(key)?.resolvedAt
	}

	/** Every identity of a type the store has a record for, held or merely observed. */
	public keysOfType(typeName: EntityTypeName): EntityKey[] {
		return [...this.typeIndex.get(typeName) ?? []]
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

	/** How many local changes are still tracked for supersession — a long-session growth diagnostic. */
	public get pendingChangeCount(): number {
		return this.changedAt.size
	}

	/** How many response-absorbing reads are currently in flight — a diagnostic, and the prune's cutoff input. */
	public get inFlightReadCount(): number {
		let total = 0
		for (const count of this.outstandingReads.values()) {
			total += count
		}

		return total
	}

	/** How many entities the identity map currently holds — a long-session growth diagnostic. */
	public get recordCount(): number {
		return this.records.size
	}

	/**
	 * Opens a read: returns the revision to stamp it with (as {@link AbsorptionContext.issuedAt}) and records
	 * it as in flight. Bracket every request whose response is absorbed — a GET, or a write that returns the
	 * updated entity — with this and {@link endRead}, so the store knows the oldest response that could still
	 * arrive. This is {@link currentRevision} plus the bookkeeping that lets the change ledger be pruned.
	 */
	public beginRead(): number {
		const issuedAt = this.revision
		this.outstandingReads.set(issuedAt, (this.outstandingReads.get(issuedAt) ?? 0) + 1)
		return issuedAt
	}

	/**
	 * Closes a read opened by {@link beginRead}, whether it landed, was superseded, or failed — call it from a
	 * `finally`. Releasing the oldest in-flight read is what advances the prune cutoff, so this is where spent
	 * markers are dropped.
	 */
	public endRead(issuedAt: number): void {
		const count = this.outstandingReads.get(issuedAt)
		if (count === undefined) {
			return
		}

		if (count <= 1) {
			this.outstandingReads.delete(issuedAt)
		}
		else {
			this.outstandingReads.set(issuedAt, count - 1)
		}

		this.pruneSettledChanges()
	}

	/**
	 * Runs one response-absorbing request as a read: opened before the request leaves, so a change made while
	 * it is in flight supersedes it, and closed however it ends, so the change ledger can be pruned once this
	 * response — and everything older — has settled.
	 *
	 * The request is handed the parser to read its response body with, which turns it into canonical instances
	 * stamped with this read's revision. This is the whole of what a client owes the store for a GET, or for a
	 * write that returns the updated entity; {@link beginRead} and {@link endRead} are what it is made of.
	 *
	 * @param context Marks the read authoritative, for a change the backend declares a client must not hold
	 * off. Ordinary reads leave it unset and are discarded for any entity changed locally since they went out.
	 */
	public async read<T>(request: (parse: AbsorbingParser) => Promise<T>, context?: AbsorptionContext): Promise<T> {
		const issuedAt = this.beginRead()
		try {
			const reviver = createAbsorbingReviver(this, { issuedAt, ...context })
			return await request((text) => JSON.parse(text, reviver))
		}
		finally {
			this.endRead(issuedAt)
		}
	}

	/**
	 * Drops every change marker that can no longer supersede a response.
	 *
	 * A marker at revision r discards only responses issued before r. The oldest read still in flight is the
	 * cutoff: any marker at or below it is one no in-flight read predates, so it can never drop a response
	 * again. With no read in flight the cutoff is infinite and the whole ledger clears — safe, because a
	 * future read captures the current revision, which is at or above every marker already here.
	 */
	private pruneSettledChanges(): void {
		const cutoff = this.oldestInFlightRead()
		for (const [key, changedAt] of this.changedAt) {
			if (changedAt <= cutoff) {
				this.changedAt.delete(key)
			}
		}
	}

	/** The revision of the oldest read still in flight, or `Infinity` when none is. */
	private oldestInFlightRead(): number {
		let oldest = Infinity
		for (const issuedAt of this.outstandingReads.keys()) {
			if (issuedAt < oldest) {
				oldest = issuedAt
			}
		}

		return oldest
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
	 * Evicts entities nothing needs any more, bounding the identity map over a long-lived session.
	 *
	 * An entity is kept when it is observed (has subscribers), has a write in flight, or is still reachable.
	 * `externalRoots` are the keys a live view holds — a listing array, the briefing — which only the caller
	 * (the repositories) can see; the store adds the closure over its own subscribed entities, so a banner
	 * holding a sprint keeps the checkpoints nested inside it that no listing need carry. Everything else is
	 * dropped, with its type-index and change-ledger entries.
	 *
	 * Keeping the reachable *is* the canonical-instance guarantee: evicting an entity a live view still holds
	 * would let a later fetch mint a divergent second instance — the very bug the store exists to prevent.
	 * Returns how many entities were evicted.
	 */
	public sweep(externalRoots: ReadonlySet<EntityKey> = new Set()): number {
		const retained = new Set<EntityKey>(externalRoots)
		for (const record of this.records.values()) {
			if (record.subscribers.size > 0 && record.value) {
				collectEntityKeys(record.value, retained, this.identify)
			}
		}

		let evicted = 0
		for (const [key, record] of this.records) {
			if (record.subscribers.size > 0 || this.writing.has(key) || retained.has(key)) {
				continue
			}

			this.records.delete(key)
			this.removeFromIndex(key)
			this.changedAt.delete(key)
			evicted++
		}

		return evicted
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

		if (mergeInto(record.value, snapshot, this.identify)) {
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

		if (mergeInto(record.value, change, this.identify)) {
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
			removed: false,
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

	/** Removes a key from its type bucket, dropping the bucket once it empties, so eviction leaves no trace. */
	private removeFromIndex(key: EntityKey): void {
		const typeName = typeNameFromKey(key)
		const keys = this.typeIndex.get(typeName)
		if (!keys) {
			return
		}

		keys.delete(key)
		if (keys.size === 0) {
			this.typeIndex.delete(typeName)
		}
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
function mergeInto(target: object, source: object, identify: EntityIdentifier, keys?: readonly string[]): boolean {
	let changed = false
	for (const key of keys ?? Object.keys(source)) {
		if (!isAssignable(target, key)) {
			continue
		}

		const value = (source as Record<string, unknown>)[key]
		const current = (target as Record<string, unknown>)[key]
		if (isEquivalent(current, value, identify)) {
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
