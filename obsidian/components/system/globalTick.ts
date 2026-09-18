import { Controller, type ReactiveElement } from "@a11d/lit"

/** A tick listener; receives the current epoch milliseconds so every subscriber shares one clock read. */
type TickListener = (now: number) => void

/**
 * The cadence a subscriber wants: every second (relative-time chips, elapsed views) or every minute (labels that
 * only ever change by the minute, e.g. "3 minutes ago"). Both derive from the one underlying 1-second timer.
 */
export type TickClock = '1s' | '60s'

const listeners: Record<TickClock, Set<TickListener>> = { '1s': new Set(), '60s': new Set() }
let timer: ReturnType<typeof setInterval> | undefined
// Ticks elapsed since the current timer started; the 60s clock fires every 60th tick. Reset when the timer (re)starts.
let ticksSinceStart = 0

function pump() {
	const now = Date.now()
	ticksSinceStart++
	// Snapshots, so a listener that unsubscribes mid-tick (e.g. a view removed by the render it triggers) is safe.
	for (const listener of [...listeners['1s']]) {
		listener(now)
	}

	if (ticksSinceStart % 60 === 0) {
		for (const listener of [...listeners['60s']]) {
			listener(now)
		}
	}
}

/**
 * Subscribes to an app-wide tick and returns an unsubscribe. There is a single `setInterval` behind every live
 * display in the app — it starts with the first subscriber and stops with the last — rather than each elapsed view
 * or relative-time chip owning its own drifting interval. The `60s` clock is that same timer sampled every 60th
 * tick (not a second interval), so a minute-cadence subscriber costs nothing extra and stays in step with the
 * second clock. A subscriber joins the shared cadence in progress: its first `60s` tick lands on the next
 * minute boundary of the running timer, not 60s after it subscribed.
 */
export function subscribeTick(listener: TickListener, clock: TickClock = '1s'): () => void {
	const set = listeners[clock]
	set.add(listener)
	if (timer === undefined) {
		ticksSinceStart = 0
		timer = setInterval(pump, 1000)
	}

	return () => {
		set.delete(listener)
		if (listeners['1s'].size === 0 && listeners['60s'].size === 0 && timer !== undefined) {
			clearInterval(timer)
			timer = undefined
		}
	}
}

/**
 * A reactive controller that re-renders its host on every app-wide {@link subscribeTick | tick}. A host declares one
 * as a field and is done: `new TickController(this)` ticks for the host's whole on-screen life; passing an `active`
 * predicate — `new TickController(this, () => this.live)` — ticks only while that stays true, re-evaluated on each
 * render, so a chip subscribes to the shared clock solely while its live mode is on.
 */
export class TickController extends Controller {
	private unsubscribe?: () => void

	public constructor(host: ReactiveElement, private readonly active: () => boolean = () => true) {
		super(host)
	}

	public override hostConnected(): void {
		this.sync()
	}

	public override hostUpdated(): void {
		this.sync()
	}

	public override hostDisconnected(): void {
		this.stop()
	}

	private sync(): void {
		if (this.active()) {
			this.unsubscribe ??= subscribeTick(() => this.host.requestUpdate())
		}
		else {
			this.stop()
		}
	}

	private stop(): void {
		this.unsubscribe?.()
		this.unsubscribe = undefined
	}
}
