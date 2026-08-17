import { component, css, html, property } from '@a11d/lit'
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

			.celestron {
				display: inline-flex;
				align-items: center;
				gap: 2px;
				font-weight: 300;
				line-height: .9;
			}

			p7t-icon {
				width: 20px;
				height: 20px;
			}

			:host([large]) {
				font-size: 1.5em;
				font-weight: 400;
			}

			:host([large]) p7t-icon {
				width: 1.1em;
				height: 1.1em;
			}
		`
	}

	protected override get content() {
		return html`
			<span class='celestron'>
				<span class='value'>${this.value}</span>
				<p7t-icon icon='starfire'></p7t-icon>
			</span>
		`
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
