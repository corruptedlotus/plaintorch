import { Component, component, css, html, nothing, state } from "@a11d/lit"
import type { DirectiveTimeframeRecord } from "@pleiades/sdk"
import { core } from ".."
import { TickController } from "../system/globalTick"

/** How often the timeframe list is re-read, so a timeframe edited elsewhere shows up without a reopen. */
const refreshIntervalMs = 5 * 60_000

/**
 * The timeframe(s) in play right now, as small chips — every lunar directive's timeframes whose window of the
 * day contains the current time. An overnight window (a start later than its end) wraps midnight. Nothing
 * renders while no timeframe is active.
 *
 * Judged by time of day alone: a timeframe's orbit narrows which *days* it applies to, and that day-level
 * reading is not evaluated here yet, so a timeframe scoped to weekdays still shows on a weekend.
 */
@component('p7t-active-timeframes')
export class ActiveTimeframesItem extends Component {
	@state() private timeframes: DirectiveTimeframeRecord[] = []

	/** Re-renders every second, so a chip appears and vanishes on the minute its window opens or closes. */
	protected readonly tick = new TickController(this)
	private refreshTimer?: ReturnType<typeof setInterval>

	static override get styles() {
		return css`
			:host {
				display: inline-flex;
				align-items: center;
				gap: .4ch;
			}

			p7t-timeframe-item {
				padding: .05em .5ch;
				border-radius: 6px;
				background-color: color-mix(in srgb, var(--p7t-flare-accent, var(--interactive-accent)) 14%, transparent);
			}
		`
	}

	protected override connected() {
		void this.load()
		this.refreshTimer = setInterval(() => void this.load(), refreshIntervalMs)
	}

	protected override disconnected() {
		clearInterval(this.refreshTimer)
	}

	private async load() {
		this.timeframes = await core.directives.listAllTimeframes()
	}

	/** The timeframes whose daily window contains the given moment. */
	private activeAt(now: Date): DirectiveTimeframeRecord[] {
		const minute = now.getHours() * 60 + now.getMinutes()
		return this.timeframes.filter(timeframe => {
			const start = minutesOf(timeframe.startTime)
			const end = minutesOf(timeframe.endTime)
			if (start === undefined || end === undefined) {
				return false
			}

			return start <= end ? minute >= start && minute < end : minute >= start || minute < end
		})
	}

	protected override get template() {
		const active = this.activeAt(new Date())
		return html`
			${active.length === 0 ? nothing : active.map(timeframe => html`<p7t-timeframe-item small .timeframe=${timeframe}></p7t-timeframe-item>`)}
		`
	}
}

/** Minutes past midnight of an `HH:mm[:ss]` time, or nothing for a malformed one. */
function minutesOf(time: string | undefined): number | undefined {
	const match = /^(\d{1,2}):(\d{2})/.exec(time ?? '')
	return match ? Number(match[1]) * 60 + Number(match[2]) : undefined
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-active-timeframes': ActiveTimeframesItem
	}
}
