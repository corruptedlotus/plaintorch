import { Component, component, css, html, nothing, property } from "@a11d/lit"
import { PleiadeanDate } from "@pleiades/sdk"
import { TickController } from "./globalTick"
import { gregorianDateLabel } from "./PleiadeanDateView"
import { localeTimeLabel } from "./TimeView"
import "./PleiadeanDateView"
import "./TimeView"
import "../PleiadesIcon"
import { tooltip } from "../design/Tooltip"

/**
 * When set, the moment is flagged the error colour with a clock-alert icon on its designated condition: `past`
 * warns once the moment has passed (an overdue attentive), `future` while it is still ahead, `none` never.
 */
export type DatetimeWarn = 'past' | 'future' | 'none'

/**
 * A composed date + time reading (PEP100) that carries **one** tooltip. The date is drawn in the same compact
 * Pleiadean format an editable date shows ({@link PleiadeanDate.toString}), the time through {@link TimeView} bare;
 * they are wrapped in a single tooltip carrying the full Gregorian date and the locale time together, rather than a
 * separate hover for the date and for the time. Any surface that shows a date (with or without a time) shares this —
 * the schedule chip's datetime face, a banner's dates — so a moment reads the same everywhere.
 *
 * In {@link relative} mode the face reads as a human relative phrase instead — "3 minutes ago", "Yesterday at
 * 12:19", "In 2 hours", "Last Wednesday" — and re-renders every second off the shared app tick so it stays live.
 * The exact date/time stays in the tooltip regardless. Beyond a week either way the face falls back to the absolute
 * reading.
 */
@component('p7t-datetime-view')
export class DatetimeView extends Component {
	/** The date as an ISO 'YYYY-MM-DD' string (a full timestamp is also accepted, e.g. a resolution instant). */
	@property() date?: string
	@property() time?: string
	/** The end of a time range, drawn after an en dash and folded into the one tooltip. */
	@property() endTime?: string

	/** Draws the face as a live relative phrase ("in 2 hours", "Yesterday at 12:19") instead of the fixed reading. */
	@property({ type: Boolean }) relative = false

	/** Flags the moment (error colour + clock-alert) once it is {@link DatetimeWarn | past or future}; off by default. */
	@property() warn: DatetimeWarn = 'none'

	/** Ticks off the shared app clock while a live reading is on ({@link relative} phrase or a {@link warn} threshold). */
	protected readonly tick = new TickController(this, () => this.relative || this.warn !== 'none')

	static override get styles() {
		return css`
			:host {
				display: inline-flex;
				align-items: center;
				gap: .4ch;
			}

			.moment {
				display: inline-flex;
				align-items: center;
				gap: .4ch;
			}

			/* A flagged moment (overdue, or an armed future) turns the whole reading the error colour. */
			.moment.warn {
				color: var(--text-error);
			}

			.warn-icon {
				width: 1.1em;
				height: 1.1em;
			}

			.sep {
				opacity: .5;
			}

			.relative {
				white-space: nowrap;
			}
		`
	}

	/** The fixed date as a Pleiadean date, or undefined when it cannot be parsed. Anchored in UTC via the calculator. */
	private get pleiadean(): PleiadeanDate | undefined {
		return PleiadeanDate.tryFromISO(this.date)
	}

	override get template() {
		const pleiadean = this.pleiadean
		const warning = this.isWarning
		return html`
			<span class='moment ${warning ? 'warn' : ''}' ${tooltip(this.unifiedLabel(pleiadean))}>
				${this.faceTemplate(pleiadean)}
				${warning ? html`<p7t-icon class='warn-icon' icon='lucide:clock-alert'></p7t-icon>` : nothing}
			</span>
		`
	}

	/** Whether the {@link warn} threshold is met right now — the moment has passed (`past`) or is still ahead (`future`). */
	private get isWarning(): boolean {
		if (this.warn === 'none' || !this.date) {
			return false
		}

		const target = relativeTarget(this.date, this.time)
		if (!target) {
			return false
		}

		const future = target.at.getTime() >= Date.now()
		return this.warn === 'future' ? future : !future
	}

	/** The relative phrase when in relative mode and within its window; otherwise the absolute date + time reading. */
	private faceTemplate(pleiadean: PleiadeanDate | undefined) {
		if (this.relative && this.date) {
			const target = relativeTarget(this.date, this.time)
			const label = target ? relativeMomentLabel(target.at, target.hasTime, new Date()) : ''
			if (label) {
				return html`<span class='relative'>${label}</span>`
			}
			// Beyond the relative window (or unparseable): fall through to the absolute reading.
		}

		return this.absoluteFace(pleiadean)
	}

	private absoluteFace(pleiadean: PleiadeanDate | undefined) {
		return html`
			${pleiadean ? html`<p7t-date-view short bare .date=${pleiadean}></p7t-date-view>` : html`<span>${this.date}</span>`}
			${!this.time ? nothing : html`
				<span class='sep'>·</span>
				<p7t-time-view bare .time=${this.time}></p7t-time-view>
				${!this.endTime ? nothing : html`
					<span class='sep'>–</span>
					<p7t-time-view bare .time=${this.endTime}></p7t-time-view>
				`}
			`}
		`
	}

	/** The full Gregorian date and locale time (with the range end, if any) as one line — the shared tooltip. */
	private unifiedLabel(pleiadean: PleiadeanDate | undefined): string {
		const dateLabel = pleiadean ? gregorianDateLabel(pleiadean) : (this.date ?? '')
		if (!this.time) {
			return dateLabel
		}

		const end = this.endTime ? ` – ${localeTimeLabel(this.endTime)}` : ''
		return `${dateLabel} · ${localeTimeLabel(this.time)}${end}`
	}
}

const hourMs = 3_600_000
const dayMs = 86_400_000

function pad2(value: number): string {
	return String(value).padStart(2, '0')
}

/** The wall-clock instant a stored date (+ optional time) denotes, for relative comparison against the local now. */
function relativeTarget(date: string, time?: string): { at: Date; hasTime: boolean } | undefined {
	const bare = /^(\d{4})-(\d{2})-(\d{2})$/.exec(date.trim())
	if (bare) {
		// A bare calendar day + optional clock time is read as local wall-clock, so "Tomorrow at 18:00" means 18:00
		// where the reader is, not a timezone-shifted instant.
		const hasTime = !!time
		const parts = hasTime ? time!.split(':') : []
		const at = new Date(Number(bare[1]), Number(bare[2]) - 1, Number(bare[3]), Number(parts[0]) || 0, Number(parts[1]) || 0)
		return Number.isNaN(at.getTime()) ? undefined : { at, hasTime }
	}

	// A full timestamp (e.g. a resolution instant) already fixes an instant and carries its own time of day.
	const at = new Date(date)
	return Number.isNaN(at.getTime()) ? undefined : { at, hasTime: true }
}

/** Whole local-calendar-day difference from `now` to `at` (0 today, -1 yesterday, +1 tomorrow). */
function calendarDayDiff(now: Date, at: Date): number {
	const a = new Date(now.getFullYear(), now.getMonth(), now.getDate()).getTime()
	const b = new Date(at.getFullYear(), at.getMonth(), at.getDate()).getTime()
	return Math.round((b - a) / dayMs)
}

/**
 * A human relative reading of a moment — "3 minutes ago", "in 2 hours", "Yesterday at 12:19", "Tomorrow at 18:00",
 * "Last Wednesday". A time-of-day is appended as " at HH:MM" only when the source carried one. Returns '' beyond a
 * week in either direction, so the caller can fall back to the absolute reading.
 */
export function relativeMomentLabel(at: Date, hasTime: boolean, now: Date): string {
	const diff = at.getTime() - now.getTime()
	const future = diff >= 0
	const abs = Math.abs(diff)
	const atClause = hasTime ? ` at ${pad2(at.getHours())}:${pad2(at.getMinutes())}` : ''

	const minutes = Math.round(abs / 60_000)
	if (minutes < 1) {
		return future ? 'in a moment' : 'just now'
	}

	if (abs < hourMs) {
		const unit = minutes === 1 ? 'minute' : 'minutes'
		return future ? `in ${minutes} ${unit}` : `${minutes} ${unit} ago`
	}

	const days = calendarDayDiff(now, at)
	if (days === 0) {
		const hours = Math.round(abs / hourMs)
		const unit = hours === 1 ? 'hour' : 'hours'
		return future ? `in ${hours} ${unit}` : `${hours} ${unit} ago`
	}

	if (days === -1) {
		return `Yesterday${atClause}`
	}

	if (days === 1) {
		return `Tomorrow${atClause}`
	}

	const weekday = at.toLocaleDateString(undefined, { weekday: 'long' })
	if (days <= -2 && days >= -6) {
		return `Last ${weekday}${atClause}`
	}

	if (days >= 2 && days <= 6) {
		return `${weekday}${atClause}`
	}

	return ''
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-datetime-view': DatetimeView
	}
}
