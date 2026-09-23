import { AsyncDirective, directive, nothing, PartType, type ElementPart, type PartInfo, type ReactiveElement } from "@a11d/lit"
import { subscribeTick, type TickClock } from "./globalTick"

/**
 * Keeps the element it is attached to re-rendering on the app-wide clock, without that element having to declare a
 * {@link TickController} of its own:
 *
 * ```ts
 * html`<p7t-elapsed-view ${timebound()} .epoch=${start}></p7t-elapsed-view>` // every second
 * html`<p7t-relative-time ${timebound('60s')} .date=${when}></p7t-relative-time>` // every minute
 * ```
 *
 * On each tick it calls `requestUpdate()` on the attached element, so any custom element that renders a time-relative
 * value stays fresh from the outside in. It subscribes only while attached and connected — unsubscribing when the
 * element leaves the DOM and resubscribing if it returns — and shares the one underlying timer (see
 * {@link subscribeTick}), so a `60s` binding samples the same clock every 60th tick rather than owning an interval.
 *
 * It must sit on an element (`html\`<el ${timebound()}>\``), and that element must be a reactive custom element —
 * a plain `<div>` has nothing to re-render.
 */
class TimeboundDirective extends AsyncDirective {
	private element?: ReactiveElement
	private clock: TickClock = '1s'
	private unsubscribe?: () => void

	public constructor(partInfo: PartInfo) {
		super(partInfo)
		if (partInfo.type !== PartType.ELEMENT) {
			throw new Error('`timebound` can only be attached to an element, e.g. html`<p7t-elapsed-view ${timebound()}>`.')
		}
	}

	public render(_clock: TickClock = '1s'): typeof nothing {
		return nothing
	}

	public override update(part: ElementPart, [clock = '1s']: [TickClock?]): typeof nothing {
		const element = part.element as Partial<ReactiveElement>
		if (typeof element.requestUpdate !== 'function') {
			throw new Error('`timebound` must be attached to a reactive custom element (one with `requestUpdate`), not a plain element.')
		}

		this.element = element as ReactiveElement
		// (Re)subscribe on the first update or whenever the requested cadence changes.
		if (this.unsubscribe === undefined || clock !== this.clock) {
			this.clock = clock
			this.subscribe()
		}

		return this.render(clock)
	}

	protected override disconnected(): void {
		this.unsubscribe?.()
		this.unsubscribe = undefined
	}

	protected override reconnected(): void {
		this.subscribe()
	}

	private subscribe(): void {
		this.unsubscribe?.()
		this.unsubscribe = subscribeTick(() => this.element?.requestUpdate(), this.clock)
	}
}

/**
 * Re-renders the attached (reactive) element on the app-wide clock — `'1s'` (default) every second, `'60s'` every
 * minute. See {@link TimeboundDirective}.
 */
export const timebound = directive(TimeboundDirective)
