import { component, css, html } from "@a11d/lit"
import { EditableNumericPart } from "./EditableNumericPart"

/**
 * Editable Celestron (starfire) value: a non-negative whole number.
 *
 * Like the time-unit, it edits by focus — arrow up/down step by one, typed digits set the number directly,
 * Enter finishes, Escape reverts, Backspace clears to zero.
 */
@component('p7t-editable-starfire')
export class EditableStarfire extends EditableNumericPart {
	override digitLimit = 6

	static override get styles() {
		return css`
			${super.styles}

			:host {
				gap: .5ch;
				font-weight: 300;
				margin: .2em;
			}

			p7t-icon {
				font-size: 1.2em;
			}

			.value {
				margin-inline-end: .4ch;
			}
		`
	}

	protected override get template() {
		const empty = this.nullable && (this.value === undefined || this.value === null)
		return html`
			<p7t-icon icon='starfire'></p7t-icon>
			<span class='value'>${empty ? this.nullDisplayTemplate : (this.value ?? 'x')}</span>
			${this.stepperTemplate()}
		`
	}

	protected override applyDigits(buffer: string): number {
		return Number(buffer)
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-editable-starfire': EditableStarfire
	}
}
