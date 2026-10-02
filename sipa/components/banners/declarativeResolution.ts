import { css, html } from "@a11d/lit"
import type { DeclarativeCalendar } from "@pleiades/sdk"
import type { CalendarSelect } from "../editing/CalendarSelect"
import type { TimeZoneSelect } from "../editing/TimeZoneSelect"
import "../editing/CalendarSelect"
import "../editing/TimeZoneSelect"

/** What a declarative's resolution row changes: its calendar, or its time zone (`null` returns either to its default). */
export interface DeclarativeResolutionUpdate {
	calendar?: DeclarativeCalendar | null
	timeZone?: string | null
}

/** The row's layout, shared by the fate and decree banners. */
export const declarativeResolutionStyles = css`
	.resolution {
		display: flex;
		flex-wrap: wrap;
		align-items: center;
		gap: .3em 1.2em;
		margin-top: .4em;
		font-size: .9em;
		font-weight: 300;
	}

	.resolution .caption {
		font-size: .7em;
		text-transform: uppercase;
		letter-spacing: .08em;
		opacity: .6;
		margin-inline-end: .4em;
	}
`

/**
 * A fate's or decree's resolution row: the calendar its orbit resolves on — overriding the vault default, or
 * returning to it — and the time zone its occurrences are meant in (floating when unset). Each choice is handed to
 * {@link save} as the update it makes; changing a one-off fate's calendar has the core rewrite its literal so the fate
 * keeps its day.
 */
export function declarativeResolutionTemplate(
	entity: { calendar?: DeclarativeCalendar | null, timeZone?: string | null },
	save: (update: DeclarativeResolutionUpdate) => void,
) {
	return html`
		<div class='resolution'>
			<span>
				<span class='caption'>Calendar</span>
				<p7t-calendar-select
					.value=${entity.calendar ?? undefined}
					@change=${(e: Event) => save({ calendar: (e.target as CalendarSelect).value ?? null })}>
				</p7t-calendar-select>
			</span>
			<span>
				<span class='caption'>Time zone</span>
				<p7t-time-zone-select
					.value=${entity.timeZone ?? undefined}
					@change=${(e: Event) => save({ timeZone: (e.target as TimeZoneSelect).value ?? null })}>
				</p7t-time-zone-select>
			</span>
		</div>
	`
}
