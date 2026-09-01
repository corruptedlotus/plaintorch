import { component, css, property } from "@a11d/lit"
import { humanizeOrbit } from "orbits"
import { EditableTextPart } from "./EditableTextPart"

/**
 * Editable Orbit scheduling notation (PEP100). Idle shows the humanized definition; editing swaps to the raw
 * notation so the user works against the real syntax. Unparseable notation warns (delayed) and reverts on
 * commit; an empty value clears the schedule.
 */
@component('p7t-editable-orbit')
export class EditableOrbit extends EditableTextPart<string> {
	@property({ type: Boolean }) short = false

	override label = 'Orbit'

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

	protected override toDisplayText(value: string | undefined): string {
		const { text } = humanizeOrbit(value, this.short)
		return text || 'No schedule'
	}

	protected override toEditableText(value: string | undefined): string {
		return value ?? ''
	}

	protected override parseValue(raw: string): string | undefined {
		const trimmed = raw.trim()
		return trimmed.length > 0 ? trimmed : undefined
	}

	protected override validateContent(trimmed: string): string | undefined {
		if (trimmed.length === 0) return undefined
		try {
			const { text } = humanizeOrbit(trimmed)
			return text ? undefined : 'Invalid orbit notation.'
		}
		catch {
			return 'Invalid orbit notation.'
		}
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-editable-orbit': EditableOrbit
	}
}
