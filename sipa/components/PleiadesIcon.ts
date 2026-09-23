import { Component, component, css, html, property } from "@a11d/lit"
import * as icons from '../assets/icons'
import { getIcon, IconName as LucideIconName } from "obsidian"

export type IconName = keyof typeof icons | `lucide:${LucideIconName}`

/**
 * Recognises a media source (a resolved resource URL or a path) so a custom uploaded image renders in place of
 * a monochrome glyph mask (PEP105). A bundled glyph name and a `lucide:` name never contain a scheme or slash.
 */
function isMediaSource(value: string): boolean {
	return /^(app|https?|data|blob):/i.test(value) || value.includes('/')
}

/**
 * @csspart icon-frame - The element containing the icon's mask image.
 * @csspart icon-image - The full-colour image rendered for a media icon (PEP105).
 */
@component('p7t-icon')
export class PleiadesIcon extends Component {
	/**
	 * The icon to render: a bundled glyph name, a `lucide:` name, or — for a custom media icon (PEP105) — a
	 * resolved resource URL. The widened string keeps arbitrary URLs assignable while preserving name autocomplete.
	 */
	@property() icon: IconName | (string & {}) = 'plaintorch'

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
				padding: 6%;
				box-sizing: border-box;
				width: 100%;
				height: 100%;
				align-self: center;
				margin-inline: auto;
			}

			img {
				width: 100%;
				height: 100%;
				object-fit: contain;
				align-self: center;
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

		// A custom media icon is a full-colour image, not a tintable glyph mask (PEP105).
		if (isMediaSource(this.icon)) {
			return html`<img part='icon-image' src=${this.icon} alt='' />`
		}

		const svg = getIcon(this.icon.replace('lucide:', ''))
		return html`${svg}`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-icon': PleiadesIcon
	}
}