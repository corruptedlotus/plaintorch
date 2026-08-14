import { component } from "@a11d/lit"
import { EditableTextPart } from "./EditableTextPart"

/**
 * Free-text editable. Single-line by default (entity titles); add `multiline` for prose (an executive-order
 * summary). Add `required` to refuse an empty commit, and a `placeholder` for the empty state.
 */
@component('p7t-editable-plaintext')
export class EditablePlainText extends EditableTextPart<string> {
	protected override toDisplayText(value: string | undefined): string {
		return value ?? ''
	}

	protected override parseValue(raw: string): string {
		// Single-line collapses internal whitespace runs; multiline keeps its line structure but trims the ends.
		return this.multiline ? raw.trim() : raw.trim().replace(/\s+/g, ' ')
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-editable-plaintext': EditablePlainText
	}
}
