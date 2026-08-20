import { component } from "@a11d/lit"
import { EditableTemporalPart } from "./EditableTemporalPart"

/**
 * Editable clock time, held as an ISO `HH:MM[:SS]` string. Idle renders `HH:MM`; clicking opens a native time
 * picker. An empty value clears the time.
 */
@component('p7t-editable-time')
export class EditableTime extends EditableTemporalPart {
	protected override readonly inputType = 'time'

	protected override toDisplayText(value: string | undefined): string {
		return value ? value.slice(0, 5) : 'No time'
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-editable-time': EditableTime
	}
}
