import { component, css, property } from "@a11d/lit"
import type { DeclarativeCalendar } from "@pleiades/sdk"
import { humanizeOrbit } from "../../orbits"
import { CalendarRef } from "../data/CalendarRef"
import { EditableTextPart } from "./EditableTextPart"

/**
 * Editable Orbit scheduling notation (PEP100). Idle shows the humanized definition; editing swaps to the raw
 * notation so the user works against the real syntax. Unparseable notation warns (delayed) and reverts on
 * commit; an empty value clears the schedule. The definition is read on the calendar the orbit resolves on — its
 * entity's own {@link calendar}, else the vault's preferred one — so its months and weekdays are the core's.
 */
@component('p7t-editable-orbit')
export class EditableOrbit extends EditableTextPart<string> {
	@property({ type: Boolean }) short = false

	/**
	 * The calendar the orbit resolves on, when its entity names one (a declarative's own calendar). Unset — as for a
	 * timeframe, which has none — the orbit is read on the vault's preferred calendar.
	 */
	@property({ type: Number }) calendar?: DeclarativeCalendar

	/** The calendar the definition is read on: {@link calendar}, else the vault's preferred one. */
	private readonly calendars = new CalendarRef(this, () => this.calendar)

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
		const { text } = humanizeOrbit(value, this.short, this.calendars.orbitCalendar)
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
