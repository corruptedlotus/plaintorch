import { component, css, html, property } from '@a11d/lit'
import { IconName } from 'components/PleiadesIcon'
import { InfoItem } from '../design/InfoItem'

/**
 * A Celestron reading (PEP095) — the value beside its starfire glyph — drawn one unified way wherever a worth is
 * shown (an objective item, a banner, the grid, a canvas node, a briefing total). `large` sizes it up for a
 * headline; the reading with its unit is the built-in tooltip.
 */
@component('p7t-celestron-item')
export class CelestronItem extends InfoItem {
	@property({ type: Number }) value = 0
	@property({ type: Boolean, reflect: true }) large = false

	static override get styles() {
		return css`
			${super.styles}

			.info-bullet {
				font-weight: 300;
				line-height: .9;
			}

			:host([large]) {
				font-size: 1.5em;
				font-weight: 400;
			}
		`
	}

	/** The starfire glyph sits after the value, so the reading is "12 ✦". */
	protected override get bulletIcon(): IconName {
		return 'starfire'
	}

	protected override get iconTrailing(): boolean {
		return true
	}

	protected override get bulletText() {
		return html`<span class='value'>${this.value}</span>`
	}

	protected override get tooltip() {
		return `${this.value} Celestron`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-celestron-item': CelestronItem
	}
}
