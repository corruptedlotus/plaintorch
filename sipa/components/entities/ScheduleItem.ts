import { component, css, html, nothing, property } from '@a11d/lit'
import { PleiadeanDate, type DeclarativeCalendar } from '@pleiades/sdk'
import { humanizeOrbit } from '../../orbits'
import { CalendarRef } from '../data/CalendarRef'
import { InfoItem } from '../design/InfoItem'
import { gregorianDateLabel } from '../system/PleiadeanDateView'
import { localeTimeLabel } from '../system/TimeView'
import '../system/PleiadeanDateView'
import '../system/TimeView'

/**
 * The schedule a fate-like entity carries (PEP100) — a recurring **orbit** or a fixed **date/time**, never both —
 * drawn read-only, the display counterpart of {@link EditableSchedule}. It prefers the orbit when both are
 * present and humanizes the orbit notation for the face.
 *
 * The schedule is composed from **three chips**, each behind its own overridable template method so a subclass can
 * swap one out without touching the layout or the mode logic: {@link orbitTemplate}, {@link dateTemplate},
 * {@link timeTemplate}. Datetime mode lays the date chip and time chip side by side; orbit mode shows the orbit chip
 * alone. The date and time chips are the same {@link PleiadeanDateView}/{@link TimeView} a standalone date or time
 * uses (drawn `bare`, so the chip carries one unified tooltip), and the day is read through the shared calendar
 * calculator ({@link PleiadeanDate.fromISO}) so every surface agrees on the Pleiadean day. The orbit face is
 * length-aware: {@link max} caps its width with an ellipsis, the full reading staying in the tooltip.
 *
 * {@link EditableSchedule} extends this and overrides the three chip methods with their editable counterparts,
 * inheriting the mode logic, the calculator and the tooltips unchanged.
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

	/**
	 * The calendar the entity's orbit resolves on, when the entity names one (a declarative's own calendar). Unset, the
	 * orbit is read on the vault's preferred calendar, as the core resolves it.
	 */
	@property({ type: Number }) calendar?: DeclarativeCalendar

	/** The calendar the orbit is read on: {@link calendar}, else the vault's preferred one. */
	protected readonly calendars = new CalendarRef(this, () => this.calendar, () => this.scheduleMode === 'orbit')

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

		`
	}

	/** The fixed date as a Pleiadean day, anchored in UTC through the shared calculator; undefined when unset/malformed. */
	protected get pleiadean(): PleiadeanDate | undefined {
		return PleiadeanDate.tryFromISO(this.date)
	}

	/** Which shape the schedule is in — the orbit wins over a lingering date, and neither reads as "none". */
	protected get scheduleMode(): 'orbit' | 'datetime' | 'none' {
		return this.orbit ? 'orbit' : this.date ? 'datetime' : 'none'
	}

	/** The orbit chip: the humanized recurrence, length-capped by {@link max}. Overridden by the editable schedule. */
	protected orbitTemplate(): unknown {
		const clip = this.max > 0
		const { text } = humanizeOrbit(this.orbit, this.short, this.calendars.orbitCalendar)
		return html`
			<span class='label ${clip ? 'clip' : ''}' style=${clip ? `max-width:${this.max}ch` : nothing}>${text || this.orbit}</span>
		`
	}

	/** The date chip: the compact Pleiadean reading, drawn `bare` so the schedule owns the one tooltip. Overridable. */
	protected dateTemplate(): unknown {
		const pleiadean = this.pleiadean
		return pleiadean
			? html`<p7t-date-view short bare .date=${pleiadean}></p7t-date-view>`
			: html`<span>${this.date}</span>`
	}

	/** The time chip: the clock time (and range end, when present), drawn `bare`. Overridden by the editable schedule. */
	protected timeTemplate(): unknown {
		if (!this.time) {
			return nothing
		}

		return html`
			<span class='sep'>·</span>
			<p7t-time-view bare .time=${this.time}></p7t-time-view>
			${!this.endTime ? nothing : html`
				<span class='sep'>–</span>
				<p7t-time-view bare .time=${this.endTime}></p7t-time-view>
			`}
		`
	}

	protected override get content() {
		if (this.scheduleMode === 'orbit') {
			return html`
				<span class='schedule'>
					<p7t-icon icon='lucide:repeat'></p7t-icon>
					${this.orbitTemplate()}
				</span>
			`
		}

		if (this.scheduleMode === 'datetime') {
			return html`
				<span class='schedule'>
					<p7t-icon icon='lucide:calendar-clock'></p7t-icon>
					${this.dateTemplate()}
					${this.timeTemplate()}
				</span>
			`
		}

		return this.nullable ? this.nullGlyphTemplate : html`<span class='schedule none'>No schedule</span>`
	}

	protected override get tooltip() {
		if (this.scheduleMode === 'orbit') {
			// The full humanized reading (never truncated) over the raw notation it stands for.
			const { text, invalid } = humanizeOrbit(this.orbit, false, this.calendars.orbitCalendar)
			return html`
				<style>
					.schedule-tip .tip-phrase { font-weight: 400; }
					.schedule-tip .tip-raw {
						display: block;
						margin-block-start: .35em;
						font-family: var(--font-monospace);
						font-size: .9em;
						opacity: .6;
					}
				</style>
				<div class='schedule-tip'>
					${!text || invalid ? nothing : html`<div class='tip-phrase'>${text}</div>`}
					<code class='tip-raw'>${this.orbit}</code>
				</div>
			`
		}

		if (this.scheduleMode === 'datetime') {
			// The one unified reading: the full Gregorian date and the locale time (with the range end, if any).
			return this.datetimeLabel
		}

		return nothing
	}

	/** The full Gregorian date and locale time as one line — the shared datetime tooltip. */
	protected get datetimeLabel(): string {
		const dateLabel = gregorianDateLabel(this.pleiadean) || (this.date ?? '')
		if (!this.time) {
			return dateLabel
		}

		const end = this.endTime ? ` – ${localeTimeLabel(this.endTime)}` : ''
		return `${dateLabel} · ${localeTimeLabel(this.time)}${end}`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-schedule-item': ScheduleItem
	}
}
