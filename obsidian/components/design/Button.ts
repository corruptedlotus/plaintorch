import { Component, component, css, html, nothing, property } from "@a11d/lit"
import { IconName } from 'components'

/**
 * @attr large
 */
@component('p7t-button')
export class Button extends Component {
	@property({ type: Boolean, reflect: true }) disabled = false
	@property({ type: Boolean, reflect: true }) emphasis = false
	@property() icon?: IconName
	/**
	 * The button's accessible name, surfaced as a hover tooltip — the label an icon-only button has no room to
	 * show. Set it wherever the button is drawn as its glyph alone.
	 */
	@property() label?: string

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

				:host([disabled]) & {
					background-color: color-mix(in srgb, var(--text-normal) 5%, transparent);
					opacity: 0.6;
					pointer-events: none;
				}

				:host([large]) & {
					padding-inline: 6px 12px;
				}

				& p7t-icon {
					font-size: 1.4em;

					:host([large]) & {
						font-size: 1.8em;
					}
				}

				&:hover {
					background-color: color-mix(in srgb, var(--text-normal) 20%, transparent);
					border-color: color-mix(in srgb, var(--text-normal) 30%, transparent);
				}
			}
			
			:host::part(text) {
				display: flex;
				flex-direction: column;
				justify-content: center;
				align-items: flex-start;
			}

			:host([large])::part(text) {
				font-size: 0.95em;
				font-weight: 350;
			}
		`
	}

	protected override get template() {
		return html`
			<button title=${this.label ?? nothing} aria-label=${this.label ?? nothing}>
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