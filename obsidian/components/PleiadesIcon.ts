import { Component, component, css, html, property } from "@a11d/lit"
import icons, { IconName } from '../assets/icons'

@component('p7t-icon')
export class PleiadesIcon extends Component {
	@property() icon: IconName = 'plaintorch'

	static override get styles() {
		return css`
			:host {
				display: flex;
				align-items: stretch;
				justify-content: stretch;
				width: 48px;
				height: 48px;
				position: relative;
			}

			div {
				width: 100%;
				height: 100%;
				mask-size: contain;
				mask-position: center;
				mask-repeat: no-repeat;
				mask-mode: alpha;
				background-color: currentColor;
			}
		`
	}

	protected override get template() {
		const iconSource = icons[this.icon] ?? icons.plaintorch

		return html`
			<div style="mask-image: url('${iconSource}')"></div>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-icon': PleiadesIcon
	}
}