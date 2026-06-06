import { Component, component, css, html, property } from "@a11d/lit"
import * as icons from 'assets/icons'
import { getIcon, IconName as LucideIconName } from "obsidian"

export type IconName = keyof typeof icons | `lucide:${LucideIconName}`

/**
 * @csspart icon-frame - The element containing the icon's mask image.
 */
@component('p7t-icon')
export class PleiadesIcon extends Component {
	@property() icon: IconName = 'plaintorch'

	static override get styles() {
		return css`
			:host {
				display: flex;
				align-items: stretch;
				justify-content: stretch;
				width: 1.2em;
				height: 1.2em;
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

			svg {
				width: 100%;
				height: 100%;
				align-self: center;
				margin-inline: auto;
			}
		`
	}

	protected override get template() {
		
		if (this.icon in icons) {
			const iconSource = icons[this.icon as keyof typeof icons] ?? icons.plaintorch
			return html`
				<div part='icon-frame' style="mask-image: url('${iconSource}')"></div>
			`
		}
		else {
			const svg = getIcon(this.icon.replace('lucide:', ''))
			return html`${svg}`
		}
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-icon': PleiadesIcon
	}
}