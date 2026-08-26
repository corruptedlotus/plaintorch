import { Component, component, css, html, nothing, property } from "@a11d/lit"
import { PleiadeanDate } from "@pleiades/sdk"
import { gregorianDateLabel } from "./PleiadeanDateView"
import { localeTimeLabel } from "./TimeView"
import "./PleiadeanDateView"
import "./TimeView"
import "../design/Tooltip"

/**
 * A composed date + time reading (PEP100) that carries **one** tooltip. The date is drawn in the same compact
 * Pleiadean format an editable date shows ({@link PleiadeanDate.toString}), the time through {@link TimeView} bare;
 * they are wrapped in a single tooltip carrying the full Gregorian date and the locale time together, rather than a
 * separate hover for the date and for the time. Any surface that shows a date (with or without a time) shares this —
 * the schedule chip's datetime face, a banner's dates — so a moment reads the same everywhere.
 */
@component('p7t-datetime-view')
export class DatetimeView extends Component {
	/** The date as an ISO 'YYYY-MM-DD' string. */
	@property() date?: string
	@property() time?: string
	/** The end of a time range, drawn after an en dash and folded into the one tooltip. */
	@property() endTime?: string

	static override get styles() {
		return css`
			:host {
				display: inline-flex;
				align-items: center;
				gap: .4ch;
			}

			.sep {
				opacity: .5;
			}
		`
	}

	/** The fixed date as a Pleiadean date, or undefined when it cannot be parsed. Anchored in UTC via the calculator. */
	private get pleiadean(): PleiadeanDate | undefined {
		return PleiadeanDate.tryFromISO(this.date)
	}

	override get template() {
		const pleiadean = this.pleiadean
		return html`
			<p7t-tooltip .text=${this.unifiedLabel(pleiadean)}>
				${pleiadean ? html`<p7t-date-view short bare .date=${pleiadean}></p7t-date-view>` : html`<span>${this.date}</span>`}
				${!this.time ? nothing : html`
					<span class='sep'>·</span>
					<p7t-time-view bare .time=${this.time}></p7t-time-view>
					${!this.endTime ? nothing : html`
						<span class='sep'>–</span>
						<p7t-time-view bare .time=${this.endTime}></p7t-time-view>
					`}
				`}
			</p7t-tooltip>
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

declare global {
	interface HTMLElementTagNameMap {
		'p7t-datetime-view': DatetimeView
	}
}
