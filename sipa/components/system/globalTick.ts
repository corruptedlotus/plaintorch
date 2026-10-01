import { Controller, type ReactiveElement } from "@a11d/lit"

/** A tick listener; receives the current epoch milliseconds so every subscriber shares one clock read. */
type TickListener = (now: number) => void

/**
 * The cadence a subscriber wants: every second (relative-time chips, elapsed views) or every minute (labels that
 * only ever change by the minute, e.g. "3 minutes ago"). Both derive from the one underlying 1-second timer.
 */
export type TickClock = '1s' | '60s'

/** A subscription bound to an element, which ticks only while that element is shown (see {@link subscribeElementTick}). */
interface ElementTicker {
	readonly element: Element
	readonly listener: TickListener
	readonly clock: TickClock
	/** Whether a tick fell due while the element was hidden and is still to be made up. */
	missed: boolean
}

const listeners: Record<TickClock, Set<TickListener>> = { '1s': new Set(), '60s': new Set() }
const elementTickers = new Set<ElementTicker>()
let timer: ReturnType<typeof setInterval> | undefined
// Ticks elapsed since the current timer started; the 60s clock fires every 60th tick. Reset when the timer (re)starts.
let ticksSinceStart = 0

function pump() {
	const now = Date.now()
	ticksSinceStart++
	const minute = ticksSinceStart % 60 === 0
	// Snapshots, so a listener that unsubscribes mid-tick (e.g. a view removed by the render it triggers) is safe.
	for (const listener of [...listeners['1s']]) {
		listener(now)
	}

	if (minute) {
		for (const listener of [...listeners['60s']]) {
			listener(now)
		}
	}

	// A minute ticker that owes a tick samples every second until its element shows again, so the make-up lands
	// within a second of the element coming back rather than on the next minute boundary.
	for (const ticker of [...elementTickers]) {
		if (ticker.clock === '1s' || minute || ticker.missed) {
			serviceElementTicker(ticker, now)
		}
	}
}

/** Runs an element ticker if its element is shown, or records the tick as missed if it is not. */
function serviceElementTicker(ticker: ElementTicker, now: number) {
	// False inside a `display: none` or `content-visibility: hidden` subtree (a dormant deck page, a ducked hero): the
	// browser skips drawing there, so a re-render would be work nobody sees.
	if (ticker.element.checkVisibility()) {
		ticker.missed = false
		ticker.listener(now)
	}
	else {
		ticker.missed = true
	}
}

function startTimer() {
	if (timer === undefined) {
		ticksSinceStart = 0
		timer = setInterval(pump, 1000)
	}
}

function stopTimerIfIdle() {
	if (listeners['1s'].size === 0 && listeners['60s'].size === 0 && elementTickers.size === 0 && timer !== undefined) {
		clearInterval(timer)
		timer = undefined
	}
}

/**
 * Subscribes to an app-wide tick and returns an unsubscribe. There is a single `setInterval` behind every live
 * display in the app — it starts with the first subscriber and stops with the last — rather than each elapsed view
 * or relative-time chip owning its own drifting interval. The `60s` clock is that same timer sampled every 60th
 * tick (not a second interval), so a minute-cadence subscriber costs nothing extra and stays in step with the
 * second clock. A subscriber joins the shared cadence in progress: its first `60s` tick lands on the next
 * minute boundary of the running timer, not 60s after it subscribed.
 *
 * A listener that only re-renders an element should use {@link subscribeElementTick} instead, which skips the ticks
 * that element cannot show.
 */
export function subscribeTick(listener: TickListener, clock: TickClock = '1s'): () => void {
	const set = listeners[clock]
	set.add(listener)
	startTimer()
	return () => {
		set.delete(listener)
		stopTimerIfIdle()
	}
}

/**
 * Subscribes an element's re-render to the app-wide tick (see {@link subscribeTick}), for as long as that element is
 * shown, and returns an unsubscribe.
 *
 * A tick that falls due while the element is hidden — in a `display: none` or `content-visibility: hidden` subtree,
 * such as a dormant page of a `p7t-page-deck` — is skipped, so hidden views cost nothing each second. The first
 * skipped tick is remembered and made up once the element shows again: within the next second, or at once when
 * whatever revealed it calls {@link catchUpTicks}.
 *
 * "Shown" is `Element.checkVisibility()`, which reads an element without a box of its own as hidden: an element
 * styled `display: contents` never ticks, so bind a boxed element instead.
 */
export function subscribeElementTick(element: Element, listener: TickListener, clock: TickClock = '1s'): () => void {
	const ticker: ElementTicker = { element, listener, clock, missed: false }
	elementTickers.add(ticker)
	startTimer()
	return () => {
		elementTickers.delete(ticker)
		stopTimerIfIdle()
	}
}

/**
 * Makes up at once every {@link subscribeElementTick | element tick} that was skipped while its element was hidden,
 * wherever that element now shows. A view that reveals hidden content calls this right after revealing it, so the
 * revealed readings are current in their first frame instead of up to a second stale.
 */
export function catchUpTicks(): void {
	const now = Date.now()
	for (const ticker of [...elementTickers]) {
		if (ticker.missed) {
			serviceElementTicker(ticker, now)
		}
	}
}

/**
 * A reactive controller that re-renders its host on every app-wide {@link subscribeTick | tick}. A host declares one
 * as a field and is done: `new TickController(this)` ticks for the host's whole on-screen life; passing an `active`
 * predicate — `new TickController(this, () => this.live)` — ticks only while that stays true, re-evaluated on each
 * render, so a chip subscribes to the shared clock solely while its live mode is on.
 *
 * The ticks are {@link subscribeElementTick | element ticks}: a host that is hidden (a dormant page, a collapsed
 * section) is not re-rendered until it shows again.
 */
export class TickController extends Controller {
	private unsubscribe?: () => void

	public constructor(private readonly element: ReactiveElement, private readonly active: () => boolean = () => true) {
		super(element)
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
			this.unsubscribe ??= subscribeElementTick(this.element, () => this.element.requestUpdate())
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
