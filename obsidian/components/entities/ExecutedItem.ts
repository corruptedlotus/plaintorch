import { Component, component, css, html, property } from "@a11d/lit"

/**
 * Status indicator for an executive's executed flag: the yes/no counterpart to `p7t-status-item`.
 *
 * Pair it with `p7t-editable` and a `doEdit` that negates the value to make it check/uncheck on click.
 */
@component('p7t-executed-item')
export class ExecutedItem extends Component {
	@property({ type: Boolean, reflect: true }) executed = false

	static override get styles() {
		return css`
			:host {
				display: grid;
				grid-template-columns: 2em auto;
				align-items: center;
				gap: .6ch;
				user-select: none;
				margin-inline-end: .4ch;
				color: color-mix(in srgb, var(--text-normal) 55%, transparent);
				transition: color .3s ease;
			}

			:host([executed]) {
				color: var(--p7t-flare-accent, var(--interactive-accent));
			}

			p7t-icon {
				height: 2em;
				width: 2em;
			}
		`
	}

	protected override get template() {
		return html`
			<p7t-icon part='icon' icon=${this.executed ? 'state-done' : 'state-zero'}></p7t-icon>
			<span>${this.executed ? 'Executed' : 'Not Executed'}</span>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-executed-item': ExecutedItem
	}
}
