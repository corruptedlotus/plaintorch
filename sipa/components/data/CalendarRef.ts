import { Controller, type ReactiveElement } from '@a11d/lit'
import { DeclarativeCalendar, preferredCalendar, resolveEntityCalendar, type PreferenceView } from '@pleiades/sdk'
import type { OrbitCalendarName } from '../../orbits'
import { DerivedRef } from './DerivedRef'
import { getCore } from './coreProvider'

/**
 * Resolves the calendar a component reads time on: the one its entity names ({@link explicit}), else the vault's
 * preferred calendar (PEP116), else the core's default, Pleiadean, while the preference is still loading. It is the
 * client mirror of how the core picks the calendar an orbit resolves on, so a humanised orbit or a month-grained
 * moment says what the core means by it.
 *
 * The preference comes from the shared `core.repos.preferences` record, observed only while it can decide anything —
 * while {@link needed} holds and the entity names no calendar of its own — so a chip that never reads the calendar
 * costs nothing. The core announces a preference write on the change feed, so a calendar switch reaches every open
 * surface.
 */
export class CalendarRef extends Controller {
	private readonly record: DerivedRef<PreferenceView[]>

	public constructor(
		host: ReactiveElement,
		/** The calendar the entity names itself, when it does. Read on every access and on every host update. */
		private readonly explicit: () => DeclarativeCalendar | null | undefined = () => undefined,
		/** Whether the host reads the calendar at all right now. Read on every host update. */
		private readonly needed: () => boolean = () => true
	) {
		super(host)
		this.record = new DerivedRef(host, getCore().repos.preferences, () => this.needed() && this.explicit() == null ? '' : undefined)
	}

	/** The calendar to read time on. */
	public get calendar(): DeclarativeCalendar {
		return resolveEntityCalendar(this.explicit(), preferredCalendar(this.record.value))
	}

	/** {@link calendar} by the name the orbit humaniser takes. */
	public get orbitCalendar(): OrbitCalendarName {
		return orbitCalendarName(this.calendar)
	}
}

/** The orbit humaniser's name for a calendar. */
export function orbitCalendarName(calendar: DeclarativeCalendar): OrbitCalendarName {
	return calendar === DeclarativeCalendar.Gregorian ? 'gregorian' : 'pleiadean'
}
