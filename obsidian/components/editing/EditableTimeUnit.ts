import { component, css, html, property } from "@a11d/lit"
import { EditableNumericPart } from "./EditableNumericPart"

/** Fine adjustment step, in whole minutes: a quarter of an hour. */
const quarter = 15

/**
 * Editable counterpart of `p7t-time-unit`, operating on whole-minute working time units.
 *
 * Editing begins on focus, revealing a stepper on either side. Steppers and the arrow keys fine-adjust by
 * quarters (snapping onto the grid); typing digits sets the hour marker (`digits * 60`). Every adjustment
 * raises `preview`; the committed value is published through `edit`/`change` once editing ends.
 */
@component('p7t-editable-time-unit')
export class EditableTimeUnit extends EditableNumericPart {
	/** Paints the unit in the accent colour rather than the ambient text colour. */
	@property({ type: Boolean, reflect: true }) accent = false

	override max = 24 * 60
	override digitLimit = 2

	static override get styles() {
		return css`
			${super.styles}

			:host {
				padding: .1em;
			}

			:host([accent]) {
				color: var(--p7t-flare-accent, var(--interactive-accent));
			}

			/* Without the accent the arc should follow the ambient text colour instead. */
			:host(:not([accent])) {
				--p7t-time-unit-arc: currentColor;
			}
		`
	}

	protected override get template() {
		const empty = this.nullable && (this.value === undefined || this.value === null)
		return html`
			${empty
				? this.nullDisplayTemplate
				: html`<p7t-time-unit .value=${Math.max(0, this.value ?? 0)}></p7t-time-unit>`}
			${this.stepperTemplate()}
		`
	}

	// Snap onto the quarter grid in the direction of travel so odd values tidy up as they move.
	protected override stepValue(direction: 1 | -1) {
		const current = Math.max(this.min, this.value ?? this.min)
		const base = direction > 0 ? Math.floor(current / quarter) : Math.ceil(current / quarter)
		this.setValue((base + direction) * quarter)
	}

	protected override applyDigits(buffer: string): number {
		return Number(buffer) * 60
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-editable-time-unit': EditableTimeUnit
	}
}
