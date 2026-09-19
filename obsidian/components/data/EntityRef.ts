import { Controller, type ReactiveElement } from '@a11d/lit'
import { EntityDraft, EntityRepository, identify, type EntityKey, type EntitySubscription } from '@pleiades/sdk'
import { plaintorchNodeCoreClient } from '@pleiades/sdk/plaintorch/node'
import { Notice } from 'obsidian'
import { ReactiveBinder } from '../editing/ReactiveBinder'

/** Supplies the identifier a reference should resolve, re-read on every host update. */
export type EntityRefSource = () => string | undefined

/** Persists an already-applied edit. Returns the API result; an absent result is a rejection. */
export type EntityFieldPersist<T> = (entity: T) => Promise<unknown>

/**
 * Persists one committed field, given the entity and the key that changed. The key lets a `'*'` fallback send a
 * partial — just the changed field — rather than the whole entity, which serializes a cyclic graph once the
 * identity map has cross-linked a navigation back to its owner (an objective's directive lists that objective).
 */
export type EntityFieldWrite<T> = (entity: T, keyPath: string) => Promise<unknown>

/**
 * How each field of an entity is saved. A field name maps to the call that persists it; `'*'` is the fallback
 * for any field without its own entry. Each write is handed the changed key, so the fallback can send a partial.
 */
export type EntityPersistMap<T> = Record<string, EntityFieldWrite<T>> & { '*': EntityFieldWrite<T> }

/** Runs after a field commit settles, for surface-specific follow-up such as revealing a renamed note. */
export type EntityCommitReaction<T> = (keyPath: string, entity: T, saved: boolean) => void | Promise<void>

const warnedTags = new Set<string>()

function warnMissingRepository(tag: string) {
	if (warnedTags.has(tag)) {
		return
	}

	warnedTags.add(tag)
	console.warn(`PLAINTORCH: <${tag}> asks for an entity type no repository serves — check its entityTypeName against the runtime type the core emits as "@type".`)
}

/**
 * Binds a component to one canonical entity.
 *
 * Owns the whole lifecycle a component used to hand-roll: resolve, observe, re-render on change, release
 * on disconnect. Because every surface observing an identity is handed the same instance, an edit made
 * anywhere reaches all of them without any of them knowing the others exist.
 *
 * The identifier is a thunk rather than a constructor argument because it usually arrives as a property
 * after construction, and can change while the host stays mounted.
 */
export class EntityRef<T extends object> extends Controller {
	private subscription?: EntitySubscription
	private observedKey?: EntityKey
	private resolving = false
	private failure?: unknown
	/** Fields captured before a binding writes an edit, kept so a rejected write can be undone. */
	private editSnapshot?: Record<string, unknown>

	public constructor(
		host: ReactiveElement,
		private readonly repository: () => EntityRepository<T> | undefined,
		private readonly source: EntityRefSource
	) {
		super(host)
	}

	/** The canonical instance, or `undefined` until it resolves. */
	public get value(): T | undefined {
		const repository = this.repository()
		const id = this.source()
		return !repository || !id ? undefined : repository.peek(id)
	}

	/** Whether a resolution is currently in flight. */
	public get loading(): boolean {
		return this.resolving
	}

	/** The failure of the last resolution attempt, if it failed. */
	public get error(): unknown {
		return this.failure
	}

	public override hostConnected(): void {
		this.sync()
	}

	public override hostUpdate(): void {
		this.sync()
	}

	public override hostDisconnected(): void {
		this.release()
	}

	/** Re-resolves the entity from the core regardless of freshness. */
	public async refresh(): Promise<void> {
		const repository = this.repository()
		const id = this.source()
		if (!repository || !id) {
			return
		}

		await this.run(async () => await repository.refresh(id))
	}

	/**
	 * Opens an isolated working copy of this reference's entity for a multi-field or cancellable edit.
	 *
	 * The draft is edited freely and applies nothing to the shared store until its `commit`; `cancel` throws
	 * it away. This is the fork-and-commit editing mode, as opposed to the immediate write `bind` performs.
	 * Undefined when the reference has no repository or has not resolved its entity yet.
	 */
	public fork(): EntityDraft<T> | undefined {
		const repository = this.repository()
		const id = this.source()
		return repository && id ? repository.fork(id) : undefined
	}

	/**
	 * Captures the entity's fields before a two-way binding writes an edit into it.
	 *
	 * A binding applies the edit to the canonical instance before anything is sent, so this is the last moment
	 * the previous state still exists anywhere — it is what a rejected write is rolled back to.
	 */
	public beginEdit(): void {
		const repository = this.repository()
		const id = this.source()
		this.editSnapshot = repository && id ? repository.snapshot(id) : undefined
	}

	/**
	 * Broadcasts an in-place edit already applied to the canonical instance, sends it, and rolls it back if
	 * the core rejects it.
	 *
	 * The edit is on screen the instant a binding writes it; without the rollback a rejected write is
	 * indistinguishable from an accepted one, since a failed request reports itself by returning nothing. The
	 * optional reaction runs once the write has settled, for follow-up that depends on the outcome.
	 */
	public async commit(send: EntityFieldPersist<T>, reaction?: (saved: boolean) => void | Promise<void>): Promise<boolean> {
		const repository = this.repository()
		const id = this.source()
		const entity = this.value
		if (!repository || !id || !entity) {
			return false
		}

		// Publish the optimistic in-place edit to every other surface showing this entity.
		repository.touch(id)
		const snapshot = this.editSnapshot
		this.editSnapshot = undefined

		// The store-direct write path — the same guard, invalidation and rollback the repository gives an
		// imperative mutate, but the reference owns the cycle now rather than routing an edit through mutate.
		const saved = await repository.commit(id, async () => await send(entity), snapshot)
		if (!saved) {
			new Notice('PLAINTORCH could not save that change.')
		}

		await reaction?.(saved)
		return saved
	}

	/**
	 * A two-way binder whose edits are persisted through this reference.
	 *
	 * `sourceKey` is the host property the binding reads the entity from — it resolves to this reference's
	 * canonical value. `persist` says how each field is saved; `reaction`, if given, runs after each commit
	 * for surface-specific follow-up. This is the whole edit cycle — snapshot, write-through, broadcast, send,
	 * rollback — behind one `${ref.bind('field')}`.
	 */
	public binder(sourceKey: string, persist: EntityPersistMap<T>, reaction?: EntityCommitReaction<T>): ReactiveBinder<T> {
		return new ReactiveBinder<T>(this.host as unknown as ReactiveElement, sourceKey, {
			sourceUpdate: () => this.beginEdit(),
			sourceUpdated: (_, keyPath) => {
				const key = (keyPath as string | undefined) ?? '*'
				const write = persist[key] ?? persist['*']
				// Bind the changed key so a '*' fallback can persist just that field, not the whole entity.
				void this.commit((entity) => write(entity, key), reaction && ((saved) => reaction(key, this.value!, saved)))
			}
		})
	}

	private sync(): void {
		const repository = this.repository()
		const id = this.source()
		// A surface asking for an entity by a type name nothing serves would otherwise just render blank
		// forever, which is how a drifted type name stayed invisible: the banner looked empty rather than
		// broken. Say so once per name instead.
		if (id && !repository) {
			warnMissingRepository((this.host as unknown as Element).tagName?.toLowerCase() ?? 'unknown element')
		}

		const key = !repository || !id ? undefined : repository.key(id)
		if (key === this.observedKey) {
			return
		}

		this.release()
		this.observedKey = key
		if (!repository || !id || !key) {
			return
		}

		this.subscription = repository.subscribe(id, () => this.host.requestUpdate())
		void this.run(async () => await repository.get(id))
	}

	private async run(operation: () => Promise<unknown>): Promise<void> {
		this.resolving = true
		this.failure = undefined
		this.host.requestUpdate()
		try {
			await operation()
		}
		catch (error) {
			this.failure = error
		}
		finally {
			this.resolving = false
			this.host.requestUpdate()
		}
	}

	private release(): void {
		this.subscription?.()
		this.subscription = undefined
		this.observedKey = undefined
	}
}

/**
 * Observes whichever entity a component was handed, rather than one it resolves itself.
 *
 * List items receive their entity as a property from an aggregate. That instance is canonical, so the item
 * only needs to learn when it changes — a component displaying an objective inside the briefing updates
 * when a banner elsewhere edits it, without either fetching anything.
 */
export class EntityWatch extends Controller {
	private subscription?: EntitySubscription
	private observedKey?: EntityKey

	public constructor(host: ReactiveElement, private readonly source: () => unknown) {
		super(host)
	}

	public override hostConnected(): void {
		this.sync()
	}

	public override hostUpdate(): void {
		this.sync()
	}

	public override hostDisconnected(): void {
		this.release()
	}

	/**
	 * Announces an in-place edit of the observed entity to every other surface showing it.
	 *
	 * A two-way binding writes straight through to the canonical instance, so the change is applied before
	 * anything is sent and there is nothing left for absorption to detect.
	 */
	public publish(): void {
		if (this.observedKey !== undefined) {
			plaintorchNodeCoreClient.store.touch(this.observedKey)
		}
	}

	private sync(): void {
		const key = identify(this.source())
		if (key === this.observedKey) {
			return
		}

		this.release()
		this.observedKey = key
		if (key !== undefined) {
			this.subscription = plaintorchNodeCoreClient.store.subscribe(key, () => this.host.requestUpdate())
		}
	}

	private release(): void {
		this.subscription?.()
		this.subscription = undefined
		this.observedKey = undefined
	}
}

/**
 * A live, filtered collection of one entity type.
 *
 * Observes the type in the store — a field edit on any member, a new member, a removal — and re-renders its
 * host, so a view of "the active objectives" or "a directive's children" stays current as members change,
 * enter, or leave the predicate. It scans only its type (through the store's per-type index), not the whole
 * store, so an unrelated change never re-runs it — the scoped alternative to `subscribeAll` a structural view
 * reached for before.
 *
 * This is a view over what the store already holds, not a fetch: the surface loads the members (a listing, the
 * briefing) as usual, and this keeps a live filtered slice of them. The predicate and comparator are read on
 * every access, so one that closes over component state — a chosen filter — tracks it without re-subscribing.
 */
export class QueryRef<T extends object> extends Controller {
	private subscription?: EntitySubscription

	public constructor(
		host: ReactiveElement,
		private readonly repository: EntityRepository<T>,
		private readonly predicate?: (entity: T) => boolean,
		private readonly compare?: (a: T, b: T) => number
	) {
		super(host)
	}

	/** The type's members matching the predicate, in order — recomputed from the live store on each read. */
	public get items(): T[] {
		const all = plaintorchNodeCoreClient.store.entitiesOfType<T>(this.repository.typeName)
		const filtered = this.predicate ? all.filter(this.predicate) : all
		return this.compare ? filtered.sort(this.compare) : filtered
	}

	public override hostConnected(): void {
		this.subscription = plaintorchNodeCoreClient.store.subscribeType(
			this.repository.typeName,
			() => this.host.requestUpdate()
		)
	}

	public override hostDisconnected(): void {
		this.subscription?.()
		this.subscription = undefined
	}
}
