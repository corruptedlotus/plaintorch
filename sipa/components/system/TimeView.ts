import { Component, component, css, html, property } from "@a11d/lit"
import { nullGlyphStyle, nullGlyphTemplate } from "../design/nullGlyph"
import { tooltip } from "../design/Tooltip"

/**
 * A 'HH:MM[:SS]' time in the device's default locale (often 12-hour) — the tooltip counterpart to the 24-hour face.
 * With `seconds` it carries the seconds too, for a moment at second granularity.
 */
export function localeTimeLabel(time: string | undefined, seconds = false): string {
	const [hours, minutes, secondsPart] = (time ?? '').split(':')
	const date = new Date()
	date.setHours(Number(hours), Number(minutes), seconds ? Number(secondsPart) || 0 : 0, 0)
	return Number.isNaN(date.getTime())
		? ''
		: date.toLocaleTimeString(undefined, seconds ? { hour: 'numeric', minute: '2-digit', second: '2-digit' } : { hour: 'numeric', minute: '2-digit' })
}

/**
 * A clock time (PEP100): 'HH:MM[:SS]' drawn as 'HH:MM', with the same time in the device's default locale (often
 * 12-hour) as the tooltip — the time counterpart of {@link PleiadeanDateView}. Date, time and schedule surfaces
 * share these two views so a moment reads the same way whether shown alone or composed inside a schedule chip.
 * An absent time reads as the shared null glyph.
 */
@component('p7t-time-view')
export class TimeView extends Component {
	@property() time?: string

	/** Renders the face without its own tooltip, so a composer (e.g. {@link DatetimeView}) can wrap it in a shared one. */
	@property({ type: Boolean }) bare = false

	/** Draws 'HH:MM:SS' rather than 'HH:MM' — a moment at second granularity, where the seconds are meaningful. */
	@property({ type: Boolean }) seconds = false

	static override get styles() {
		return css`
			:host {
				display: inline;
				font-weight: 250;
				font-variant-numeric: tabular-nums;
			}

			${nullGlyphStyle}
		`
	}

	override get template() {
		if (!this.time) {
			return nullGlyphTemplate()
		}

		const face = this.seconds ? `${this.time.slice(0, 5)}:${(this.time.slice(6, 8) || '00').padStart(2, '0')}` : this.time.slice(0, 5)
		return this.bare
			? html`<span>${face}</span>`
			: html`<span ${tooltip(localeTimeLabel(this.time, this.seconds))}>${face}</span>`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-time-view': TimeView
	}
}
