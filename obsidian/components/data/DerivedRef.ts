import { Controller, type ReactiveElement } from '@a11d/lit'
import type { DerivedRepository, EntitySubscription } from '@pleiades/sdk'

/**
 * Binds a component to one derived record — the briefing, a note resolution, a PUCK resolution.
 *
 * The same lifecycle as an entity reference, for views that have no identity of their own. The entities
 * inside a record are canonical regardless, so a surface showing the record and a surface showing one of
 * its contents stay in agreement without either refetching for the other.
 */
export class DerivedRef<T> extends Controller {
	private subscription?: EntitySubscription
	private observedKey?: string
	private resolving = false
	private failure?: unknown

	public constructor(
		host: ReactiveElement,
		private readonly repository: DerivedRepository<T>,
		private readonly source: () => string | undefined = () => ''
	) {
		super(host)
	}

	/** The cached record, or `undefined` until it resolves. */
	public get value(): T | undefined {
		const key = this.source()
		return key === undefined ? undefined : this.repository.peek(key)
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

	/** Re-resolves the record from the core regardless of freshness. */
	public async refresh(): Promise<void> {
		const key = this.source()
		if (key === undefined) {
			return
		}

		await this.run(async () => await this.repository.refresh(key))
	}

	private sync(): void {
		const key = this.source()
		if (key === this.observedKey) {
			return
		}

		this.release()
		this.observedKey = key
		if (key === undefined) {
			return
		}

		this.subscription = this.repository.subscribe(key, () => this.host.requestUpdate())
		void this.run(async () => await this.repository.get(key))
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
