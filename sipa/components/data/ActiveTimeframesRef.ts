import { Controller, type ReactiveElement } from '@a11d/lit'
import type { DerivedRepository, DirectiveTimeframeRecord } from '@pleiades/sdk'
import { DerivedRef } from './DerivedRef'
import { getCore } from './coreProvider'
import { subscribeTick } from '../system/globalTick'

/**
 * Binds a component to the timeframes active right now (PEP100 patch 2) — the one `core.repos.activeTimeframes`
 * record every surface shares, so the active-timeframe chips and the flare on an affined executive never disagree.
 *
 * A write already revalidates the record while it is observed; the passing of time does not, since a window opens
 * and closes on the clock alone. So while its host is connected this also re-reads the record on the shared
 * 60-second tick. Every holder asks on the same tick and the repository coalesces concurrent reads, which keeps a
 * list of flaring rows at one request a minute however long it is. A window edge therefore shows up to about a
 * minute late.
 */
export class ActiveTimeframesRef extends Controller {
	private readonly repository: DerivedRepository<DirectiveTimeframeRecord[]>
	private readonly record: DerivedRef<DirectiveTimeframeRecord[]>
	/** Ends the 60-second tick subscription; set while connected. */
	private unsubscribeTick?: () => void

	public constructor(host: ReactiveElement) {
		super(host)
		this.repository = getCore().repos.activeTimeframes
		this.record = new DerivedRef(host, this.repository)
	}

	/** The active timeframes as the core last returned them, in its order; empty until the record resolves. */
	public get timeframes(): readonly DirectiveTimeframeRecord[] {
		return this.record.value ?? []
	}

	/** Whether the timeframe with this id is active right now; an absent id never is. */
	public isActive(timeframeId: number | null | undefined): boolean {
		return timeframeId != null && this.timeframes.some(timeframe => timeframe.id === timeframeId)
	}

	public override hostConnected(): void {
		this.unsubscribeTick?.()
		// A failed re-read keeps the last listing; the next tick tries again.
		this.unsubscribeTick = subscribeTick(() => void this.repository.revalidateIfObserved().catch(() => undefined), '60s')
	}

	public override hostDisconnected(): void {
		this.unsubscribeTick?.()
		this.unsubscribeTick = undefined
	}
}
