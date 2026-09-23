import { component, css, html, property } from '@a11d/lit'
import { IconName } from '../PleiadesIcon'
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
	@property({ type: Boolean, reflect: true }) starfire = false

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

			:host([starfire]) {
				color: color-mix(in srgb, var(--p7t-flare-accent, var(--interactive-accent)) 20%, var(--text-normal));
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
		return html`<span class='value'>${this.starfire ? this.value * 2 : this.value}</span>`
	}

	protected override get tooltip() {
		return this.starfire ? `${this.value} Starfire` : `${this.value} Celestron`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-celestron-item': CelestronItem
	}
}
