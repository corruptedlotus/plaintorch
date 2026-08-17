import { component, css, html, nothing, property } from '@a11d/lit'
import { humanizeOrbit } from 'orbits'
import { InfoItem } from '../design/InfoItem'

/** 'YYYY-MM-DD' → a short 'Mon 12' label, parsed as local time so the day never shifts across a timezone. */
function formatDate(date: string): string {
	const parsed = new Date(`${date}T00:00:00`)
	return Number.isNaN(parsed.getTime()) ? date : parsed.toLocaleDateString(undefined, { weekday: 'short', day: 'numeric' })
}

/** 'HH:MM[:SS]' → 'HH:MM'. */
const formatTime = (time: string): string => time.slice(0, 5)

/**
 * The schedule a fate-like entity carries (PEP100) — a recurring **orbit** or a fixed **date/time**, never both —
 * drawn read-only, the display counterpart of {@link EditableOrbitDatetime}. It prefers the orbit when both are
 * present, humanizes the orbit notation for the face, and keeps the raw notation (or the full date) as the tooltip.
 */
@component('p7t-schedule-item')
export class ScheduleItem extends InfoItem {
	@property() orbit?: string
	@property() date?: string
	@property() time?: string

	static override get styles() {
		return css`
			${super.styles}

			.schedule {
				display: inline-flex;
				align-items: center;
				gap: .4ch;
				font-weight: 300;
			}

			.none {
				opacity: .5;
			}

			p7t-icon {
				width: 1.1em;
				height: 1.1em;
				opacity: .7;
			}
		`
	}

	private get mode(): 'orbit' | 'datetime' | 'none' {
		return this.orbit ? 'orbit' : this.date ? 'datetime' : 'none'
	}

	protected override get content() {
		if (this.mode === 'orbit') {
			const { text } = humanizeOrbit(this.orbit)
			return html`
				<span class='schedule'>
					<p7t-icon icon='lucide:repeat'></p7t-icon>
					<span>${text || this.orbit}</span>
				</span>
			`
		}

		if (this.mode === 'datetime') {
			return html`
				<span class='schedule'>
					<p7t-icon icon='lucide:calendar-clock'></p7t-icon>
					<span>${formatDate(this.date!)}${!this.time ? '' : ` · ${formatTime(this.time)}`}</span>
				</span>
			`
		}

		return html`<span class='schedule none'>No schedule</span>`
	}

	protected override get tooltip() {
		if (this.mode === 'orbit') {
			return this.orbit
		}

		if (this.mode === 'datetime') {
			return `${this.date}${!this.time ? '' : ` ${this.time}`}`
		}

		return nothing
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-schedule-item': ScheduleItem
	}
}
