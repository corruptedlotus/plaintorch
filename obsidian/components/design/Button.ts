import { Component, component, css, eventListener, html, nothing, property } from "@a11d/lit"
import { IconName } from 'components'
import './Tooltip'

/**
 * @attr large
 */
@component('p7t-button')
export class Button extends Component {
	@property({ type: Boolean, reflect: true }) disabled = false
	@property({ type: Boolean, reflect: true }) emphasis = false
	/** A borderless, transparent variant with only a subtle hover — for icon-only affordances that shouldn't read as a solid button. */
	@property({ type: Boolean, reflect: true }) ghost = false
	/** Marks a destructive action; it reads in the error colour on hover. */
	@property({ type: Boolean, reflect: true }) danger = false
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
				pointer-events: none;
			}

			button {
				pointer-events: auto;
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
					padding-inline: 16px;
				}

				& p7t-icon {
					font-size: 1.2em;

					:host([large]) & {
						font-size: 1.5em;
					}
				}

				&:hover {
					background-color: color-mix(in srgb, var(--text-normal) 20%, transparent);
					border-color: color-mix(in srgb, var(--text-normal) 30%, transparent);
				}

				/* Ghost: no chrome at rest, only a faint hover — for icon-only affordances. */
				:host([ghost]) & {
					background-color: transparent;
					border-color: transparent;
					padding: .3em;
				}

				:host([ghost]) &:hover {
					background-color: color-mix(in srgb, var(--text-normal) 14%, transparent);
					border-color: transparent;
				}

				/* Danger: a destructive action reads in the error colour on hover. */
				:host([danger]) &:hover {
					color: var(--text-error, crimson);
					background-color: color-mix(in srgb, var(--text-error, crimson) 12%, transparent);
					border-color: transparent;
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
		const button = html`
			<button part='button' aria-label=${this.label ?? nothing}>
				${this.icon ? html`<p7t-icon part='icon' .icon=${this.icon}></p7t-icon>` : ''}
				${![...this.childNodes].filter(x => x.nodeType === Node.ELEMENT_NODE || (x.nodeType === Node.TEXT_NODE && x.textContent?.trim())).length ? nothing : html`<slot part='text'></slot>`}
			</button>
		`

		// The label is the tooltip too — through p7t-tooltip, not a native title, so it reads the same as every
		// other tooltip in the app. Only wrapped when there is a label (icon-only buttons); text buttons render bare.
		return this.label
			? html`<p7t-tooltip .text=${this.label}>${button}</p7t-tooltip>`
			: button
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-button': Button
	}
}