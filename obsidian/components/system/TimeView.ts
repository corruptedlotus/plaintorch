import { Component, component, css, html, property } from "@a11d/lit"
import { nullGlyphStyle, nullGlyphTemplate } from "../design/nullGlyph"
import "../design/Tooltip"

/** A 'HH:MM[:SS]' time in the device's default locale (often 12-hour) — the tooltip counterpart to the 24-hour face. */
export function localeTimeLabel(time: string | undefined): string {
	const [hours, minutes] = (time ?? '').split(':')
	const date = new Date()
	date.setHours(Number(hours), Number(minutes), 0, 0)
	return Number.isNaN(date.getTime()) ? '' : date.toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' })
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

	static override get styles() {
		return css`
			:host {
				display: inline;
				font-weight: 250;
				font-variant-numeric: tabular-nums;
			}

			p7t-tooltip {
				display: inline;
			}

			${nullGlyphStyle}
		`
	}

	override get template() {
		if (!this.time) {
			return nullGlyphTemplate()
		}

		const face = html`<span>${this.time.slice(0, 5)}</span>`
		return this.bare ? face : html`<p7t-tooltip .text=${localeTimeLabel(this.time)}>${face}</p7t-tooltip>`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-time-view': TimeView
	}
}
