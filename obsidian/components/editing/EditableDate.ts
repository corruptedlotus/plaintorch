import { component, css, eventListener, PropertyValues } from "@a11d/lit"
import { PleiadeanDate } from "@pleiades/sdk"
import { EditablePart } from "./EditableDataLink"

/**
 * Editable calendar date, held as an ISO `YYYY-MM-DD` string.
 *
 * Follows the same idle/editing split as the Orbit editable: idle renders the Pleiadean reading of the
 * date, and editing swaps to the raw ISO form so the user works against the value the core actually
 * stores. An empty value finishes as `undefined`, clearing the date.
 */
@component('p7t-editable-date')
export class EditableDate extends EditablePart<string> {
	override readonly contentEditable = 'plaintext-only'
	override readonly spellcheck = false

	static override get styles() {
		return css`
			${super.styles}

			:host {
				display: inline-flex;
				align-items: center;
				text-align: start;
				font-weight: 300;
				min-width: 2ch;
				white-space: nowrap;
			}

			:host([active]) {
				font-family: var(--font-monospace);
			}
		`
	}

	protected override updated(_changedProperties: PropertyValues) {
		this.textContent = this.active ? (this.value ?? '') : this.displayText
	}

	private get displayText() {
		if (!this.value) {
			return 'No date'
		}

		const parsed = new Date(this.value)
		return Number.isNaN(parsed.getTime())
			? this.value
			: PleiadeanDate.fromDate(parsed).toString()
	}

	@eventListener({ type: 'focus', target: this })
	protected handleFocus() {
		this.beginManualEditing()
	}

	@eventListener({ type: 'blur', target: this })
	@eventListener({ type: 'keyup', target: this })
	protected handleInput(e: KeyboardEvent | unknown) {
		if (e instanceof KeyboardEvent && !(e.key === 'Enter' && e.ctrlKey)) return
		this.blur()
		const raw = (this.textContent ?? '').trim()
		this.finishEditing(raw.length > 0 ? raw : undefined)
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-editable-date': EditableDate
	}
}
