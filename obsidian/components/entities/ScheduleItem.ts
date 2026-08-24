import { component, css, html, nothing, property } from '@a11d/lit'
import { humanizeOrbit } from 'orbits'
import { InfoItem } from '../design/InfoItem'
import '../system/DatetimeView'

/**
 * The schedule a fate-like entity carries (PEP100) — a recurring **orbit** or a fixed **date/time**, never both —
 * drawn read-only, the display counterpart of {@link EditableOrbitDatetime}. It prefers the orbit when both are
 * present and humanizes the orbit notation for the face.
 *
 * The date/time face is not drawn here: it composes {@link PleiadeanDateView} and {@link TimeView}, the same views a
 * standalone date or time uses, so a schedule reads the same Pleiadean date (with its Gregorian tooltip) and locale
 * time as they do. The orbit face is length-aware: {@link max} caps its width with an ellipsis, the full reading
 * staying in the tooltip.
 */
@component('p7t-schedule-item')
export class ScheduleItem extends InfoItem {
	@property() orbit?: string
	@property() date?: string
	@property() time?: string
	/** The end of a datetime range (e.g. a fate's event window); shown as "start – end" when present. */
	@property() endTime?: string

	/**
	 * Caps the orbit label at this many characters' width, tailing an ellipsis past it (the full phrase stays in the
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

			.sep {
				opacity: .5;
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

	protected override get content() {
		if (this.mode === 'orbit') {
			const clip = this.max > 0
			const { text } = humanizeOrbit(this.orbit, this.short)
			return html`
				<span class='schedule'>
					<p7t-icon icon='lucide:repeat'></p7t-icon>
					<span class='label ${clip ? 'clip' : ''}' style=${clip ? `max-width:${this.max}ch` : nothing}>${text || this.orbit}</span>
				</span>
			`
		}

		if (this.mode === 'datetime') {
			return html`
				<span class='schedule'>
					<p7t-icon icon='lucide:calendar-clock'></p7t-icon>
					<p7t-datetime-view .date=${this.date} .time=${this.time} .endTime=${this.endTime}></p7t-datetime-view>
				</span>
			`
		}

		return this.nullable ? this.nullGlyphTemplate : html`<span class='schedule none'>No schedule</span>`
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

		// The composed datetime view carries the one unified tooltip (the full Gregorian date and locale time).
		return nothing
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-schedule-item': ScheduleItem
	}
}
