import { Component, component, css, html, property } from "@a11d/lit"
import { PleiadeanDate } from "@pleiades/sdk"
import { getOrdinalSuffix } from "@pleiades/sdk/helpers"
import "../design/Tooltip"

/**
 * The day in the device's default calendar and locale, spelled out in full — the Gregorian counterpart to the
 * Pleiadean face and the **one** Gregorian date formatter every chip shares, so a date's hover reads the same
 * everywhere. Anchored in UTC to match the calendar's UTC day; an absent date reads as the empty string.
 */
export function gregorianDateLabel(date: PleiadeanDate | undefined): string {
	if (!date) {
		return ''
	}

	try {
		// The Pleiadean date is a whole day anchored in UTC, so format it in UTC to keep the day from shifting.
		return date.toDate().toLocaleDateString(undefined, { dateStyle: 'full', timeZone: 'UTC' })
	}
	catch {
		return ''
	}
}

@component('p7t-date-view')
export class PleiadeanDateView extends Component {

	@property() date = PleiadeanDate.fromDate(new Date())

	/** Renders the face without its own tooltip, so a composer (e.g. {@link DatetimeView}) can wrap it in a shared one. */
	@property({ type: Boolean }) bare = false

	/**
	 * Draws the terse reading ({@link PleiadeanDate.toString}, e.g. "26/Sol 3") rather than the spelled-out face —
	 * what a compact surface (a schedule chip, a banner date) wants, where the long face has no room.
	 */
	@property({ type: Boolean }) short = false

	static override get styles() {
		return css`
			:host {
				display: inline;
				font-weight: 250;
			}

			p7t-tooltip {
				display: inline;
			}

			.day-suffix {
				font-size: .6em;
			}

			.year-specs {
				display: inline-flex;
				flex-direction: column;
				font-size: .45em;
				vertical-align: text-bottom;
				line-height: 1.2em;
			}

			.year-suffix {
				text-transform: uppercase;
				font-weight: 700;
				margin-bottom: -.1em;
			}

			.year-type {
				text-transform: uppercase;
				font-weight: 100;
			}
		`
	}

	override get template() {
		const face = this.short ? html`<span>${this.date.toString()}</span>` : html`
			<span>${this.date.day}</span><span class='day-suffix'>${getOrdinalSuffix(this.date.day)}</span>
			<span> of </span>
			<span class='month'>${this.date.monthName},</span>
			${this.date.year === 0 ? html`
				<span>Year ZERO</span>
			` : html`
				<span>${Math.abs(this.date.year)}</span>
				<div class='year-specs'>
					<span class='year-suffix'>
						${this.date.year > 0 ? 'A.U.' : 'B.U.'}
					</span>
					<span class='year-type'>${this.date.yearType}</span>
				</div>
			`}
		`

		return this.bare ? face : html`<p7t-tooltip .text=${gregorianDateLabel(this.date)}>${face}</p7t-tooltip>`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-date-view': PleiadeanDateView
	}
}