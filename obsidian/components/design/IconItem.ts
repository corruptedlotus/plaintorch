import { Component, component, css, html, property } from "@a11d/lit"
import { IconName } from "components/PleiadesIcon"

@component('p7t-icon-item')
export class IconItem<T> extends Component {
	@property() icon?: IconName
	@property() text!: string
	@property() data!: T
	@property({ type: Boolean, reflect: true }) small = false

	static override get styles() {
		return css`
			:host {
				display: flex;
				align-items: center;
				gap: 1ch;
			}

			p7t-icon {
				height: 2em;
				width: 2em;

				:host([small]) & {
					height: 1.2em;
					width: 1.2em;
				}
			}
		`
	}

	protected override get template() {
		return html`
			<p7t-icon part='icon' icon="${this.icon ?? 'plaintorch'}"></p7t-icon>
			<span>${this.text}</span>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-icon-item': IconItem<unknown>
	}
}