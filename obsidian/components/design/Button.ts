import { Component, component, css, html, property } from "@a11d/lit"
import { IconName } from 'components'

@component('p7t-button')
export class Button extends Component {
	@property({ type: Boolean, reflect: true }) disabled = false
	@property({ type: Boolean, reflect: true }) emphasis = false
	@property() icon?: IconName

	static override get styles() {
		return css`
			:host {
				display: flex;
				align-items: center;
			}

			button {
				background-color: color-mix(in srgb, var(--text-normal) 10%, transparent);
				outline: none;
				border: 1px solid transparent;
				display: flex;
				align-items: center;
				gap: 1ch;
				/*margin: 5px;*/
				vertical-align: middle;
				padding: 6px 12px;
				font-family: var(--font-interface);
				border-radius: 6px;
				font-size: inherit;
				transition: .3s ease;
				cursor: pointer;

				& p7t-icon {
					width: 2em;
					height: 2em;
				}

				&:hover {
					background-color: color-mix(in srgb, var(--text-normal) 20%, transparent);
					border-color: color-mix(in srgb, var(--text-normal) 30%, transparent);
				}

			}
			
			:host::part(text) {
				display: block;
			}
		`
	}

	protected override get template() {
		return html`
			<button>
				${this.icon ? html`<p7t-icon part='icon' .icon=${this.icon}></p7t-icon>` : ''}
				<slot part='text'></slot>
			</button>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-button': Button
	}
}