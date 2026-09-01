import { component } from "@a11d/lit"
import { PleiadeanDate } from "@pleiades/sdk"
import { gregorianDateLabel } from "../system/PleiadeanDateView"
import { EditableTemporalPart } from "./EditableTemporalPart"

/**
 * Editable calendar date, held as an ISO `YYYY-MM-DD` string. Idle renders the Pleiadean reading; clicking
 * opens a native date picker. An empty value clears the date.
 *
 * Both readings go through the shared calendar calculator ({@link PleiadeanDate.fromISO}, UTC-anchored) and the
 * shared {@link gregorianDateLabel}, so this field shows the exact same day as the read-only schedule chip — the
 * two used to disagree by a day in zones east of UTC because this one parsed the string as *local* midnight.
 */
@component('p7t-editable-date')
export class EditableDate extends EditableTemporalPart {
	protected override readonly inputType = 'date'

	protected override toDisplayText(value: string | undefined): string {
		if (!value) return 'No date'
		return PleiadeanDate.tryFromISO(value)?.toString() ?? value
	}

	/** The same day in the device's default calendar and locale, spelled out in full, as the hover reading. */
	protected override get idleTooltip(): string {
		return gregorianDateLabel(PleiadeanDate.tryFromISO(this.value))
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-editable-date': EditableDate
	}
}
