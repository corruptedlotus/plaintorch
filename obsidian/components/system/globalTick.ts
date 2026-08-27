import { Controller, type ReactiveElement } from "@a11d/lit"

/** A per-second listener; receives the current epoch milliseconds so every subscriber shares one clock read. */
type TickListener = (now: number) => void

const listeners = new Set<TickListener>()
let timer: ReturnType<typeof setInterval> | undefined

function pump() {
	const now = Date.now()
	// A snapshot, so a listener that unsubscribes mid-tick (e.g. a view removed by the render it triggers) is safe.
	for (const listener of [...listeners]) {
		listener(now)
	}
}

/**
 * Subscribes to the **one** app-wide 1-second tick and returns an unsubscribe. There is a single `setInterval`
 * behind every live display in the app — the timer starts with the first subscriber and stops with the last —
 * rather than each elapsed view or relative-time chip owning its own drifting interval.
 */
export function subscribeTick(listener: TickListener): () => void {
	listeners.add(listener)
	if (timer === undefined) {
		timer = setInterval(pump, 1000)
	}

	return () => {
		listeners.delete(listener)
		if (listeners.size === 0 && timer !== undefined) {
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
