import { component, css, html, nothing, property } from '@a11d/lit'
import { humanizeOrbit } from 'orbits'
import { InfoItem } from '../design/InfoItem'

/** 'YYYY-MM-DD' → a short 'Mon 12' label, parsed as local time so the day never shifts across a timezone. */
function formatDate(date: string): string {
	const parsed = new Date(`${date}T00:00:00`)
	return Number.isNaN(parsed.getTime()) ? date : parsed.toLocaleDateString(undefined, { weekday: 'short', day: 'numeric' })
}

/** 'YYYY-MM-DD' → a full 'Monday, 12 August 2025' label, for the tooltip. */
function formatDateLong(date: string): string {
	const parsed = new Date(`${date}T00:00:00`)
	return Number.isNaN(parsed.getTime())
		? date
		: parsed.toLocaleDateString(undefined, { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' })
}

/** 'HH:MM[:SS]' → 'HH:MM'. */
const formatTime = (time: string): string => time.slice(0, 5)

/**
 * The schedule a fate-like entity carries (PEP100) — a recurring **orbit** or a fixed **date/time**, never both —
 * drawn read-only, the display counterpart of {@link EditableOrbitDatetime}. It prefers the orbit when both are
 * present and humanizes the orbit notation for the face.
 *
 * A humanized orbit can run long ("Wednesdays of every 3 weeks at 12:00"), so the face is length-aware: set
 * {@link max} to cap its width and it tails an ellipsis, keeping the chip a chip. The full phrase is always in the
 * tooltip — the humanized reading over the raw notation — so nothing is lost to the truncation.
 */
@component('p7t-schedule-item')
export class ScheduleItem extends InfoItem {
	@property() orbit?: string
	@property() date?: string
	@property() time?: string
	/** The end of a datetime range (e.g. a fate's event window); shown as "start – end" when present. */
	@property() endTime?: string

	/**
	 * Caps the label at this many characters' width, tailing an ellipsis past it (the full phrase stays in the
	 * tooltip). `0`, the default, never truncates — the face grows to the whole reading.
	 */
	@property({ type: Number }) max = 0

	/**
	 * Draws the orbit in its terse reading ("Every 3 Wed @12:00") rather than the full one. Off by default, so
	 * roomy surfaces keep the long phrase; the tooltip always carries the full reading regardless.
	 */
	@property({ type: Boolean }) short = false

	static override get styles() {
		return css`
			${super.styles}

			.schedule {
				display: inline-flex;
				align-items: center;
				gap: .4ch;
				font-weight: 300;
				min-width: 0;
			}

			.label {
				min-width: 0;
			}

			.label.clip {
				overflow: hidden;
				text-overflow: ellipsis;
				white-space: nowrap;
			}

			.none {
				opacity: .5;
			}

			p7t-icon {
				width: 1.1em;
				height: 1.1em;
				opacity: .7;
				flex: 0 0 auto;
			}

			.tip-phrase {
				font-weight: 400;
			}

			.tip-raw {
				display: block;
				margin-block-start: .35em;
				font-family: var(--font-monospace);
				font-size: .9em;
				opacity: .6;
			}
		`
	}

	private get mode(): 'orbit' | 'datetime' | 'none' {
		return this.orbit ? 'orbit' : this.date ? 'datetime' : 'none'
	}

	/** The " · HH:MM" (or " · HH:MM – HH:MM" range) suffix beside a date, or empty when timeless. */
	private get timeLabel(): string {
		if (!this.time) return ''
		return this.endTime ? ` · ${formatTime(this.time)} – ${formatTime(this.endTime)}` : ` · ${formatTime(this.time)}`
	}

	protected override get content() {
		const clip = this.max > 0
		const labelStyle = clip ? `max-width:${this.max}ch` : nothing

		if (this.mode === 'orbit') {
			const { text } = humanizeOrbit(this.orbit, this.short)
			return html`
				<span class='schedule'>
					<p7t-icon icon='lucide:repeat'></p7t-icon>
					<span class='label ${clip ? 'clip' : ''}' style=${labelStyle}>${text || this.orbit}</span>
				</span>
			`
		}

		if (this.mode === 'datetime') {
			return html`
				<span class='schedule'>
					<p7t-icon icon='lucide:calendar-clock'></p7t-icon>
					<span class='label ${clip ? 'clip' : ''}' style=${labelStyle}>${formatDate(this.date!)}${this.timeLabel}</span>
				</span>
			`
		}

		return html`<span class='schedule none'>No schedule</span>`
	}

	protected override get tooltip() {
		if (this.mode === 'orbit') {
			// The full humanized reading (never truncated) over the raw notation it stands for.
			const { text, invalid } = humanizeOrbit(this.orbit)
			return html`
				<div>
					${!text || invalid ? nothing : html`<div class='tip-phrase'>${text}</div>`}
					<code class='tip-raw'>${this.orbit}</code>
				</div>
			`
		}

		if (this.mode === 'datetime') {
			return `${formatDateLong(this.date!)}${this.timeLabel}`
		}

		return nothing
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-schedule-item': ScheduleItem
	}
}
