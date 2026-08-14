import { component } from "@a11d/lit"
import { PleiadeanDate } from "@pleiades/sdk"
import { EditableTemporalPart } from "./EditableTemporalPart"

/**
 * Editable calendar date, held as an ISO `YYYY-MM-DD` string. Idle renders the Pleiadean reading; clicking
 * opens a native date picker. An empty value clears the date.
 */
@component('p7t-editable-date')
export class EditableDate extends EditableTemporalPart {
	protected override readonly inputType = 'date'

	protected override toDisplayText(value: string | undefined): string {
		if (!value) return 'No date'
		// Parse as local midnight so the day never shifts across a timezone.
		const parsed = new Date(`${value}T00:00:00`)
		return Number.isNaN(parsed.getTime()) ? value : PleiadeanDate.fromDate(parsed).toString()
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-editable-date': EditableDate
	}
}
