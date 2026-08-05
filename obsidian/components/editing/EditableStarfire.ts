import { component, css, eventListener, html, PropertyValues } from "@a11d/lit"
import { EditablePart } from "./EditableDataLink";

@component('p7t-editable-starfire')
export class EditableStarfire extends EditablePart<number> {
	override readonly contentEditable = 'plaintext-only'
	override readonly spellcheck = false

	static override get styles() {
		return css`
			${super.styles}

			:host {
				display: flex;
				align-items: center;
				align-self: center;
				gap: .5ch;
				font-weight: 300;
				margin: .2em;
			}

			p7t-icon {
				width: 1.8em;
				height: 1.8em;
			}

			slot {
				display: inline;
				margin-inline-end: .4ch;
			}
		`
	}

	protected override updated(_changedProperties: PropertyValues) {
		// See EditablePlainText: syncing the text mid-edit would collapse the caret, so it waits until idle.
		if (this.active) return
		this.textContent = this.value?.toString() ?? ''
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
		// Empty or non-numeric input clears the value rather than settling on NaN — the field may be blanked.
		const parsed = this.textContent ? parseInt(this.textContent, 10) : Number.NaN
		this.finishEditing(Number.isNaN(parsed) ? undefined : parsed)
	}

	protected override get template() {
		return html`
			<p7t-icon icon='starfire'></p7t-icon>
			<slot class='text'>${this.value}</slot>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-editable-starfire': EditableStarfire
	}
}