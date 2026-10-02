import { Controller, type ReactiveControllerHost } from "@a11d/lit"
import { SegmentedInputController, type EditableSegment, type LiteralSegment, type SegmentedInputStep } from "@3mo/segmented-input"
import { OrbitUnit, PleiadeanDate } from "@pleiades/sdk"

/** A unit the Pleiadean segments edit, coarsest first. */
type PleiadeanUnit = 'year' | 'month' | 'day' | 'hour' | 'minute' | 'second'

/** One editable Pleiadean unit as the field renders it. */
export interface PleiadeanEditableSegment extends EditableSegment {
	readonly unit: PleiadeanUnit
	readonly value?: number
	readonly min: number
	readonly max: number
	/** What assistive technology reads where the text alone says less (the month's full name). */
	readonly valueText?: string
}

/** One Pleiadean unit or the literal between two of them. */
export type PleiadeanSegment = PleiadeanEditableSegment | LiteralSegment

export interface PleiadeanDateTimeControllerOptions {
	/** The civil moment (`YYYY-MM-DDTHH:mm:ss`, as an Epoch carries it), whatever calendar it is shown in. */
	readonly value?: string
	/** Which units are real, and so which segments show. */
	readonly granularity: OrbitUnit
	/** The field's name, which every segment's own label is read with. */
	readonly label?: string
	readonly disabled?: boolean
	/** Fired on commit — Enter or leaving the segments — with the moment cut to the start of its unit. */
	handleChange?(moment: string | undefined): void
}

type OptionsOrFactory<THost> = PleiadeanDateTimeControllerOptions | ((host: THost) => PleiadeanDateTimeControllerOptions)

const shortMonthNames = ['Nil', 'Sol', 'Xun', 'Tar', 'Lua', 'Tva'] as const
const pad = (value: number) => String(value).padStart(2, '0')

/** Today's civil day, as the Pleiadean day it is. */
function today(): PleiadeanDate {
	const now = new Date()
	return PleiadeanDate.fromISO(`${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`)
}

/** The units a granularity makes real, coarsest first; a week is edited as the day it starts on. */
function unitsOf(granularity: OrbitUnit): readonly PleiadeanUnit[] {
	switch (granularity) {
		case OrbitUnit.Year: return ['year']
		case OrbitUnit.Month: return ['month', 'year']
		case OrbitUnit.Week:
		case OrbitUnit.Day: return ['day', 'month', 'year']
		case OrbitUnit.Hour: return ['day', 'month', 'year', 'hour']
		case OrbitUnit.Minute: return ['day', 'month', 'year', 'hour', 'minute']
		default: return ['day', 'month', 'year', 'hour', 'minute', 'second']
	}
}

/**
 * A civil moment cut to the start of its unit on the Pleiadean calendar: the first of its year or month, the Saturday
 * its week starts on (as the app reads Pleiadean weeks), or the start of its day, hour or minute.
 */
export function pleiadeanMomentAt(moment: string, granularity: OrbitUnit): string {
	const date = PleiadeanDate.tryFromISO(moment.slice(0, 10))
	if (!date) {
		return moment
	}

	const time = moment.length > 10 ? moment.slice(11, 19) : '00:00:00'
	switch (granularity) {
		case OrbitUnit.Year: return `${new PleiadeanDate(date.year, 1, 1).toISODate()}T00:00:00`
		case OrbitUnit.Month: return `${new PleiadeanDate(date.year, date.month, 1).toISODate()}T00:00:00`
		case OrbitUnit.Week: {
			const day = date.toDate()
			// getUTCDay reads Saturday as 6: step back to it.
			day.setUTCDate(day.getUTCDate() - (day.getUTCDay() + 1) % 7)
			return `${day.toISOString().slice(0, 10)}T00:00:00`
		}
		case OrbitUnit.Day: return `${date.toISODate()}T00:00:00`
		case OrbitUnit.Hour: return `${date.toISODate()}T${time.slice(0, 2)}:00:00`
		case OrbitUnit.Minute: return `${date.toISODate()}T${time.slice(0, 5)}:00`
		default: return `${date.toISODate()}T${time}`
	}
}

/**
 * The segments of a civil moment on the **Pleiadean** calendar — six months of 61 days (the last 60 or 61), year 0
 * beginning 2023-03-21 — for a host that renders them, on 3MO's calendar-agnostic {@link SegmentedInputController}:
 * the same typing, stepping and keys the Gregorian date-time segments have, over units the Intl calendars cannot
 * express.
 *
 * They read as the app writes a Pleiadean day, `26/Sol 3`, with the month shown by its short name and typed as its
 * number (1–6), then a 24-hour time at the finer granularities. A week is edited as a day and committed as the
 * Saturday its week starts on. The value stays the civil moment string an Epoch stores; only its reading is Pleiadean.
 *
 * ```html
 * <span ${controller.input.group.ref()}>
 *     ${controller.segments.map(segment => html`<span ${controller.input.segment.ref(segment)}></span>`)}
 * </span>
 * ```
 */
export class PleiadeanDateTimeController<THost extends ReactiveControllerHost = ReactiveControllerHost> extends Controller {
	protected readonly options: PleiadeanDateTimeControllerOptions
	readonly input: SegmentedInputController<PleiadeanSegment, THost>

	private year = 0
	private month = 1
	private day = 1
	private hour = 0
	private minute = 0
	private second = 0
	private filled = new Set<PleiadeanUnit>()
	private syncedValue?: string

	constructor(host: THost, options: OptionsOrFactory<THost>) {
		super(host)
		this.options = typeof options === 'function' ? options(host) : options
		this.sync()
		const controller = this
		this.input = new SegmentedInputController<PleiadeanSegment, THost>(host, {
			get segments() { return controller.segments },
			get label() { return controller.options.label },
			get disabled() { return controller.options.disabled },
			accept: (segment, typed, character) => controller.accept(segment as PleiadeanEditableSegment, typed, character),
			isComplete: (segment, text) => controller.isSegmentComplete(segment as PleiadeanEditableSegment, text),
			handleSegmentInput: (segment, text) => controller.handleSegmentInput(segment as PleiadeanEditableSegment, text),
			handleStep: (segment, step) => controller.handleStep(segment as PleiadeanEditableSegment, step),
			handleCommit: () => controller.handleCommit(),
			stamp: (element, segment) => controller.stamp(element, segment as PleiadeanEditableSegment),
		})
	}

	private get units() {
		return unitsOf(this.options.granularity)
	}

	private get isEmpty() {
		return this.filled.size === 0
	}

	/** The segments, literals included, as the moment currently reads. */
	get segments(): readonly PleiadeanSegment[] {
		const units = this.units
		const segments: PleiadeanSegment[] = []
		const literal = (text: string) => segments.push({ key: `literal-${segments.length}`, editable: false, text })
		if (this.options.granularity === OrbitUnit.Week) {
			literal('Week of ')
		}

		for (const unit of units) {
			if (unit === 'month' && units.includes('day')) literal('/')
			if (unit === 'year' && units.includes('month')) literal(' ')
			if (unit === 'hour') literal(', ')
			if (unit === 'minute' || unit === 'second') literal(':')
			segments.push(this.segmentOf(unit))
		}

		if (this.options.granularity === OrbitUnit.Hour) {
			literal(':00')
		}

		return segments
	}

	private segmentOf(unit: PleiadeanUnit): PleiadeanEditableSegment {
		const filled = this.filled.has(unit)
		const value = this.unitValue(unit)
		const { min, max } = this.limits(unit)
		const base = { key: unit, unit, editable: true as const, filled, min, max, value, label: unit, inputMode: 'numeric' }
		switch (unit) {
			case 'month': return { ...base, capacity: 1, text: filled ? shortMonthNames[value - 1]! : 'mmm', valueText: filled ? new PleiadeanDate(this.year, value, 1).monthName : undefined }
			case 'year': return { ...base, capacity: 4, text: filled ? String(value) : 'y' }
			case 'day': return { ...base, capacity: 2, text: filled ? String(value) : 'dd' }
			default: return { ...base, capacity: 2, text: filled ? pad(value) : unit === 'hour' ? 'hh' : unit === 'minute' ? 'mm' : 'ss' }
		}
	}

	private unitValue(unit: PleiadeanUnit): number {
		return this[unit]
	}

	private limits(unit: PleiadeanUnit): { min: number, max: number } {
		switch (unit) {
			case 'year': return { min: 0, max: 9999 }
			case 'month': return { min: 1, max: 6 }
			case 'day': return { min: 1, max: PleiadeanDate.getDaysInMonth(this.year, this.month) }
			case 'hour': return { min: 0, max: 23 }
			default: return { min: 0, max: 59 }
		}
	}

	override hostUpdate() {
		this.sync()
	}

	/** Takes up the host's value when it changes, all its units filled; with none, the units wait on today's reading. */
	private sync() {
		const value = this.options.value
		if (value === this.syncedValue) {
			return
		}

		this.syncedValue = value
		const date = value ? PleiadeanDate.tryFromISO(value.slice(0, 10)) : undefined
		const reference = date ?? today()
		this.year = reference.year
		this.month = reference.month
		this.day = reference.day
		const time = date && value!.length > 10 ? value!.slice(11, 19).split(':').map(Number) : [0, 0, 0]
		this.hour = time[0] ?? 0
		this.minute = time[1] ?? 0
		this.second = time[2] ?? 0
		this.filled = new Set(date ? this.units : [])
	}

	focus() {
		this.input.focus()
	}

	/** A digit which would take the unit past its largest value starts it over: a month holding "1" typed "8" reads "8" — refused, being no month. */
	private accept(segment: PleiadeanEditableSegment, typed: string, character: string): string | undefined {
		if (!/^[0-9]$/.test(character)) {
			return undefined
		}

		const text = typed + character
		if (Number(text) <= segment.max) {
			return text
		}

		return Number(character) <= segment.max ? character : undefined
	}

	private isSegmentComplete(segment: PleiadeanEditableSegment, text: string): boolean {
		return Number(text) * 10 > segment.max || text.length >= String(segment.max).length
	}

	/** Sets the unit, unless what is typed so far is not yet a value it could hold ("0" of "05"); emptied, it clears. */
	private handleSegmentInput(segment: PleiadeanEditableSegment, text: string) {
		if (!text) {
			this.filled.delete(segment.unit)
			return
		}

		const value = Number(text)
		if (!Number.isNaN(value) && value >= segment.min) {
			this.set(segment.unit, value)
		}
	}

	private handleStep(segment: PleiadeanEditableSegment, step: SegmentedInputStep) {
		const { min, max } = this.limits(segment.unit)
		const page = segment.unit === 'minute' || segment.unit === 'second' ? 15 : segment.unit === 'day' ? 7 : segment.unit === 'year' ? 10 : 1
		switch (step) {
			case 'increment': return this.step(segment.unit, 1)
			case 'decrement': return this.step(segment.unit, -1)
			case 'incrementPage': return this.step(segment.unit, page)
			case 'decrementPage': return this.step(segment.unit, -page)
			case 'min': return this.set(segment.unit, min)
			case 'max': return this.set(segment.unit, max)
		}
	}

	private set(unit: PleiadeanUnit, value: number) {
		if (this.options.disabled) {
			return
		}

		this[unit] = value
		this.filled.add(unit)
		// A shorter month or year leaves no room for the day it held.
		this.day = Math.min(this.day, PleiadeanDate.getDaysInMonth(this.year, this.month))
	}

	/** Steps a filled unit, wrapping within its range (a year just counts on); an unfilled one is only filled. */
	private step(unit: PleiadeanUnit, delta: number) {
		if (this.options.disabled) {
			return
		}

		if (this.filled.has(unit)) {
			const { min, max } = this.limits(unit)
			const next = unit === 'year'
				? Math.min(max, Math.max(min, this.year + delta))
				: min + ((this[unit] - min + delta) % (max - min + 1) + (max - min + 1)) % (max - min + 1)
			this[unit] = next
			this.day = Math.min(this.day, PleiadeanDate.getDaysInMonth(this.year, this.month))
		}

		this.filled.add(unit)
	}

	/** Enter, or leaving the segments: units left unfilled take today's reading, and the moment is handed on. */
	private handleCommit() {
		let value: string | undefined
		if (!this.isEmpty) {
			this.filled = new Set(this.units)
			const date = new PleiadeanDate(this.year, this.month, this.day).toISODate()
			value = pleiadeanMomentAt(`${date}T${pad(this.hour)}:${pad(this.minute)}:${pad(this.second)}`, this.options.granularity)
		}

		if (value !== this.options.value) {
			this.syncedValue = value
			this.options.handleChange?.(value)
			this.sync()
		}
	}

	private stamp(element: HTMLElement, segment: PleiadeanEditableSegment) {
		segment.filled && segment.value !== undefined
			? element.setAttribute('aria-valuenow', String(segment.value))
			: element.removeAttribute('aria-valuenow')
		element.setAttribute('aria-valuemin', String(segment.min))
		element.setAttribute('aria-valuemax', String(segment.max))
		element.setAttribute('aria-valuetext', segment.filled ? segment.valueText ?? segment.text : 'Empty')
	}
}
