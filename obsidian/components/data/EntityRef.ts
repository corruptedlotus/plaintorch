import { Controller, type ReactiveElement } from '@a11d/lit'
import { EntityRepository, identify, type EntityKey, type EntitySubscription } from '@pleiades/sdk'
import { plaintorchNodeCoreClient } from '@pleiades/sdk/plaintorch/node'

/** Supplies the identifier a reference should resolve, re-read on every host update. */
export type EntityRefSource = () => string | undefined

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

	private sync(): void {
		const repository = this.repository()
		const id = this.source()
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
