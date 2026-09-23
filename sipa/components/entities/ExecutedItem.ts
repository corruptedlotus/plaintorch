import { component, css, property } from "@a11d/lit"
import { IconName, InfoItem } from ".."

/**
 * Status indicator for an executive's executed flag: the yes/no counterpart to `p7t-status-item`.
 *
 * Pair it with `p7t-editable` and a `doEdit` that negates the value to make it check/uncheck on click.
 */
@component('p7t-executed-item')
export class ExecutedItem extends InfoItem {
	@property({ type: Boolean, reflect: true }) executed = false

	static override get styles() {
		return css`
			${super.styles}
			
			:host([executed]) {
				color: var(--p7t-flare-accent, var(--interactive-accent));
			}
		`
	}

	override get bulletIcon() : IconName {
		return this.executed ? 'state-done' : 'state-zero';
	}

	override get bulletText() {
		return this.executed ? 'Executed' : 'Not Executed'
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-executed-item': ExecutedItem
	}
}
