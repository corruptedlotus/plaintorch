import { component, css, eventListener, PropertyValues } from "@a11d/lit"
import { EditablePart } from "./EditableDataLink"
import { humanizeOrbit } from "orbits"

/**
 * Editable specific to Orbit scheduling notation (PEP100). When idle it renders the
 * human-readable definition produced by the vendored @pleiades/orbits humanizer; while
 * editing it swaps to the raw Orbit notation so the user works against the real syntax.
 * An empty value finishes as `undefined`, clearing the schedule.
 */
@component('p7t-editable-orbit')
export class EditableOrbit extends EditablePart<string> {
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
			}

			/* Raw notation reads better in a mono face while editing. */
			:host([active]) {
				font-family: var(--font-monospace);
				white-space: pre-wrap;
			}
		`
	}

	protected override updated(_changedProperties: PropertyValues) {
		// Editing shows the raw notation; idle shows the humanized definition.
		this.textContent = this.active ? (this.value ?? '') : this.displayText
	}

	private get displayText() {
		const { text } = humanizeOrbit(this.value)
		return text || 'No schedule'
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
		'p7t-editable-orbit': EditableOrbit
	}
}
