import { component, html, type HTMLTemplateResult } from "@a11d/lit"
import { DeclarativeCalendar } from "@pleiades/sdk"
import type { IconName } from "../PleiadesIcon"
import { CalendarRef } from "../data/CalendarRef"
import { fuzzyFilter } from "./fuzzy"
import { SelectBase, type SelectOption } from "./SelectBase"

/** How one calendar is named and drawn. */
export interface CalendarDescriptor {
	readonly label: string
	readonly icon: IconName
}

export const calendarDescriptors: Record<DeclarativeCalendar, CalendarDescriptor> = {
	[DeclarativeCalendar.Gregorian]: { label: 'Gregorian', icon: 'lucide:sun' },
	[DeclarativeCalendar.Pleiadean]: { label: 'Pleiadean', icon: 'lucide:sparkles' },
}

const calendars = [DeclarativeCalendar.Gregorian, DeclarativeCalendar.Pleiadean]

/**
 * Picks the calendar a fate's or decree's orbit resolves on, as an inline select. Unset (`undefined`) is the vault
 * **default** — the preferred calendar (PEP116) — which the list leads with and names ("Default (Pleiadean)"), so a
 * chosen calendar can be returned to it. The value itself is the choice, not what it resolves to.
 */
@component('p7t-calendar-select')
export class CalendarSelect extends SelectBase<DeclarativeCalendar> {
	override placeholder = 'Calendar…'

	/** What the default resolves to right now: the vault's preferred calendar. */
	private readonly preferred = new CalendarRef(this)

	protected override async search(query: string): Promise<readonly SelectOption<DeclarativeCalendar>[]> {
		const defaultOption: SelectOption<DeclarativeCalendar> = { key: 'default', resolve: () => undefined, template: this.defaultTemplate }
		return [
			...(fuzzyFilter(query, ['default'], () => `Default ${calendarDescriptors[this.preferred.calendar].label}`).length > 0 ? [defaultOption] : []),
			...fuzzyFilter(query, calendars, calendar => calendarDescriptors[calendar].label).map(calendar => ({
				key: `calendar:${calendar}`,
				value: calendar,
				template: this.chipTemplate(calendar)
			}))
		]
	}

	protected override renderValue(value: DeclarativeCalendar): HTMLTemplateResult {
		// The core sends an unset calendar as null.
		return value === null || value === undefined ? this.renderEmpty() : this.chipTemplate(value)
	}

	/** An unset calendar is the default, named for what it resolves to — not a bare placeholder. */
	protected override renderEmpty(): HTMLTemplateResult {
		return this.defaultTemplate
	}

	private get defaultTemplate() {
		const preferred = calendarDescriptors[this.preferred.calendar]
		return html`<p7t-icon-item small icon='lucide:calendar-cog'>Default (${preferred.label})</p7t-icon-item>`
	}

	private chipTemplate(calendar: DeclarativeCalendar) {
		const descriptor = calendarDescriptors[calendar]
		return html`<p7t-icon-item small .icon=${descriptor.icon}>${descriptor.label}</p7t-icon-item>`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-calendar-select': CalendarSelect
	}
}
