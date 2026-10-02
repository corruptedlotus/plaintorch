import { component, css, eventListener, html, keyed, nothing, property, query, type PropertyValues } from "@a11d/lit"
import "@3mo/date-time"
import { FieldDateTimeController, FieldDateTimePrecision } from "@3mo/date-time-fields/controller"
import { DeclarativeCalendar, OrbitUnit } from "@pleiades/sdk"
import { EditablePart } from "./EditableDataLink"
import { PleiadeanDateTimeController, pleiadeanMomentAt } from "./PleiadeanDateTimeController"

const pad = (value: number, length = 2) => String(value).padStart(length, '0')

/** The segment precision a granularity reads at. The two share their units and their numbering, Year (1) to Second (7). */
function precisionOf(unit: OrbitUnit): FieldDateTimePrecision {
	return FieldDateTimePrecision.all.find(precision => precision.valueOf() === unit) ?? FieldDateTimePrecision.Day
}

/** A civil moment string (`YYYY-MM-DDTHH:mm:ss`, as an Epoch carries it) read as the local wall-clock date it names. */
function dateOfMoment(moment: string | undefined): DateTime | undefined {
	if (!moment) {
		return undefined
	}

	const date = new DateTime(moment.length === 10 ? `${moment}T00:00:00` : moment)
	return Number.isNaN(date.valueOf()) ? undefined : date
}

/** A local date as the civil moment string an Epoch carries, to the second and without a zone. */
function momentOfDate(date: Date): string {
	return `${pad(date.getFullYear(), 4)}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`
}

/** The start of the unit a date falls in at a precision — a week starts where the segments start it, on its first day. */
function startAt(date: DateTime, precision: FieldDateTimePrecision): DateTime {
	return precision === FieldDateTimePrecision.Week ? date.weekStart.dayStart : precision.getRange(date).start ?? date
}

/**
 * A civil moment cut down to the start of its unit at a granularity, on a calendar: the day of a day, the hour of an
 * hour, the first day of its week, month or year — a Gregorian week as the date-time segments count it, a Pleiadean
 * one from its Saturday. What the moment reads as once its granularity changes.
 */
export function momentAtGranularity(moment: string, unit: OrbitUnit, calendar = DeclarativeCalendar.Gregorian): string {
	if (calendar === DeclarativeCalendar.Pleiadean) {
		return pleiadeanMomentAt(moment, unit)
	}

	const date = dateOfMoment(moment)
	return date ? momentOfDate(startAt(date, precisionOf(unit))) : moment
}

/** The ISO week (`YYYY-Www`) a date falls in, as a native week input reads and writes it. */
function isoWeekOf(date: Date): string {
	// The Thursday of the date's Monday-first week names its year; week 1 is the one holding 4 January.
	const thursday = new Date(date.getFullYear(), date.getMonth(), date.getDate() + 3 - (date.getDay() + 6) % 7)
	const january4 = new Date(thursday.getFullYear(), 0, 4)
	const days = Math.round((thursday.valueOf() - january4.valueOf()) / 86_400_000)
	const week = 1 + Math.round((days - 3 + (january4.getDay() + 6) % 7) / 7)
	return `${thursday.getFullYear()}-W${pad(week)}`
}

/** The Monday of an ISO week (`YYYY-Www`). */
function mondayOfIsoWeek(value: string): Date | undefined {
	const match = /^(\d{4})-W(\d{2})$/.exec(value)
	if (!match) {
		return undefined
	}

	const january4 = new Date(Number(match[1]), 0, 4)
	return new Date(january4.getFullYear(), 0, 4 - (january4.getDay() + 6) % 7 + (Number(match[2]) - 1) * 7)
}

/** The native input standing in as the picker for a precision; a year has none, and is typed or stepped. */
function pickerTypeOf(precision: FieldDateTimePrecision): 'month' | 'week' | 'date' | 'datetime-local' | undefined {
	switch (precision) {
		case FieldDateTimePrecision.Year: return undefined
		case FieldDateTimePrecision.Month: return 'month'
		case FieldDateTimePrecision.Week: return 'week'
		case FieldDateTimePrecision.Day: return 'date'
		default: return 'datetime-local'
	}
}

/**
 * An editable civil moment at a granularity — an Epoch's `moment` read at its `granularity` — typed in segments on
 * 3MO's segmented-input controllers, as newer Mitra builds its date fields.
 *
 * The segments show exactly as much of the moment as the {@link granularity} makes real: a year alone, a month, a
 * week, a day, down to the hour, minute or second. Typing fills a segment and moves on, the arrows step the focused
 * one, and leaving the segments or pressing Enter commits the moment, cut to the start of its unit. The value stays
 * the civil string an Epoch carries (`YYYY-MM-DDTHH:mm:ss`, no zone), whatever calendar reads it.
 *
 * The {@link calendar} decides the reading. **Gregorian** runs on 3MO's {@link FieldDateTimeController}: the
 * language's order, digits and clock, and a native picker behind the calendar glyph and Alt+ArrowDown (month, week,
 * date or date-and-time by granularity; a year has none). **Pleiadean** runs on {@link PleiadeanDateTimeController},
 * over the same segmented input: `26/Sol 3` and a 24-hour time, weeks from their Saturday, and no picker yet (the
 * native ones are Gregorian only).
 *
 * It is an {@link EditablePart} like the rest: the value contract, `nullable` with its clear button, `disabled`, the
 * outline and the pulse while a segment has focus. Focusing the field focuses its segments, so a creation row's Tab
 * lands in it.
 */
@component('p7t-editable-datetime')
export class EditableDateTime extends EditablePart<string> {
	/** How much of the moment is real, and so which segments it shows. */
	@property({ type: Number }) granularity = OrbitUnit.Minute

	/** The calendar the moment is read and typed on. */
	@property({ type: Number }) calendar = DeclarativeCalendar.Gregorian

	/** The field's name, which every segment's own label is read with. */
	@property() label?: string

	@query('.native') private readonly nativeInput?: HTMLInputElement

	private get precision() {
		return precisionOf(this.granularity)
	}

	private get gregorian() {
		return this.calendar !== DeclarativeCalendar.Pleiadean
	}

	/**
	 * The segments' controller, one per calendar and granularity. The segments only take up the units of a value when
	 * the value itself changes, so a granularity that reveals new units over an unchanged moment (a day becoming a
	 * minute, at midnight) would leave them blank rather than reading 00:00; a fresh controller adopts the whole moment.
	 */
	private controller = this.createController()

	private createController(): FieldDateTimeController<this> | PleiadeanDateTimeController<this> {
		if (!this.gregorian) {
			return new PleiadeanDateTimeController(this, host => ({
				get value() { return host.value },
				get granularity() { return host.granularity },
				get label() { return host.label },
				get disabled() { return host.disabled },
				handleChange: moment => host.commitMoment(moment),
			}))
		}

		return new FieldDateTimeController(this, host => ({
			get value() { return dateOfMoment(host.value) },
			get precision() { return host.precision },
			get label() { return host.label },
			get disabled() { return host.disabled },
			handleChange: date => host.commit(date),
			get handlePickerOpen() { return host.disabled || !pickerTypeOf(host.precision) ? undefined : () => host.openPicker() },
		}))
	}

	protected override willUpdate(changed: PropertyValues<this>) {
		super.willUpdate(changed)
		// A calendar set before the first render (as a host binding sets it) still has to swap the controller the
		// constructor made for the default one, so the kind is compared rather than the change's previous value.
		const wrongKind = (this.controller instanceof PleiadeanDateTimeController) === this.gregorian
		const granularityChanged = changed.has('granularity') && changed.get('granularity') !== undefined
		if (wrongKind || granularityChanged) {
			this.removeController(this.controller)
			this.controller = this.createController()
		}
	}

	static override get styles() {
		return css`
			${super.styles}

			:host {
				display: inline-flex;
				align-items: center;
				justify-content: flex-start;
				gap: .4ch;
				min-width: 3ch;
				cursor: text;
			}

			.segments {
				white-space: nowrap;
				font-variant-numeric: tabular-nums;

				> * {
					outline: none;
					caret-color: transparent;
					user-select: none;
				}

				> [role] {
					border-radius: 3px;
					padding-inline: 1px;

					&:focus {
						background: color-mix(in srgb, var(--p7t-flare-accent, var(--interactive-accent)) 40%, transparent);
					}

					&[data-placeholder] {
						color: var(--text-faint);
					}
				}

				> [aria-hidden] {
					color: var(--text-muted);
				}
			}

			.picker {
				position: relative;
				display: inline-flex;
				cursor: pointer;
				color: var(--text-muted);

				& p7t-icon {
					width: 1em;
					height: 1em;
				}

				&:hover {
					color: var(--p7t-flare-accent, var(--interactive-accent));
				}
			}

			/* The native input only lends its picker, which opens where the input sits: over the glyph. */
			.native {
				position: absolute;
				inset: 0;
				width: 100%;
				height: 100%;
				opacity: 0;
				pointer-events: none;
				border: 0;
				padding: 0;
			}
		`
	}

	/** The segments of whichever controller reads the moment. */
	private get segmentsTemplate() {
		const { controller } = this
		if (controller instanceof PleiadeanDateTimeController) {
			return html`
				<span class='segments' ${controller.input.group.ref()}>
					${controller.segments.map(segment => html`<span ${controller.input.segment.ref(segment)}></span>`)}
				</span>
			`
		}

		return html`
			<span class='segments' ${controller.group.ref()}>
				${controller.segments.segments.map(segment => html`<span ${controller.segment.ref(segment)}></span>`)}
			</span>
		`
	}

	protected override get contentTemplate() {
		// Only the Gregorian reading has a picker: the native ones know no other calendar.
		const pickerType = this.gregorian ? pickerTypeOf(this.precision) : undefined
		// Keyed by calendar and granularity, so a new controller gets new segment elements and the old one's listeners go
		// with theirs.
		return html`
			${keyed(`${this.calendar}:${this.granularity}`, this.segmentsTemplate)}
			${!pickerType || this.disabled ? nothing : html`
				<span class='picker' title='Choose' @mousedown=${(e: Event) => e.preventDefault()} @click=${(e: Event) => { e.stopPropagation(); this.openPicker() }}>
					<p7t-icon icon=${pickerType === 'datetime-local' ? 'lucide:calendar-clock' : 'lucide:calendar'}></p7t-icon>
					<input class='native' tabindex='-1' aria-hidden='true'
						type=${pickerType}
						step=${this.nativeStep ?? nothing}
						.value=${this.nativeValue}
						@change=${(e: Event) => this.onNativePick((e.target as HTMLInputElement).value)}>
				</span>
			`}
		`
	}

	/** Focusing the field focuses its segments — the one a pointer or the last visit left the tab stop on. */
	public override focus() {
		this.controller.focus()
	}

	/** The field edits in its segments: a press anywhere else on it lands there. */
	public override startEditing() {
		if (!this.disabled && !this.matches(':focus-within')) {
			this.controller.focus()
		}
	}

	/** A segment taking focus is the edit beginning. */
	@eventListener({ type: 'focusin', target: this })
	protected handleFocusIn() {
		if (!this.active) {
			this.beginManualEditing()
		}
	}

	/** Leaving the field ends the edit; the controller has already committed what the segments held. */
	@eventListener({ type: 'focusout', target: this })
	protected handleFocusOut(e: FocusEvent) {
		if (!this.contains(e.relatedTarget as Node | null) && !this.renderRoot.contains(e.relatedTarget as Node | null)) {
			this.active = false
		}
	}

	private commit(date: Date | undefined) {
		this.commitMoment(date ? momentOfDate(startAt(new DateTime(date), this.precision)) : undefined)
	}

	/** Commits a moment already cut to its unit — as the Pleiadean segments hand it on. */
	private commitMoment(value: string | undefined) {
		if (value !== this.value) {
			this.finishEditing(value)
		}
	}

	/** Opens the native picker for the granularity; it needs the user's gesture, which a click or Alt+ArrowDown is. */
	private openPicker() {
		try {
			this.nativeInput?.showPicker()
		}
		catch {
			// Outside a gesture, or an engine without a picker for this type: the segments stay the way in.
		}
	}

	/** The moment in the form the native picker holds it. */
	private get nativeValue() {
		const date = dateOfMoment(this.value)
		if (!date) {
			return ''
		}

		const moment = momentOfDate(date)
		switch (pickerTypeOf(this.precision)) {
			case 'month': return moment.slice(0, 7)
			case 'week': return isoWeekOf(date)
			case 'date': return moment.slice(0, 10)
			case 'datetime-local': return this.precision === FieldDateTimePrecision.Second ? moment : moment.slice(0, 16)
			default: return ''
		}
	}

	/** Seconds per picker step: an hour or a minute is the finest a picked time reaches, a second only at second precision. */
	private get nativeStep() {
		switch (this.precision) {
			case FieldDateTimePrecision.Hour: return 3600
			case FieldDateTimePrecision.Minute: return 60
			case FieldDateTimePrecision.Second: return 1
			default: return undefined
		}
	}

	private onNativePick(raw: string) {
		if (!raw) {
			return
		}

		const picked = pickerTypeOf(this.precision) === 'week'
			? mondayOfIsoWeek(raw)
			: dateOfMoment(raw.length === 7 ? `${raw}-01` : raw)
		if (picked) {
			this.commit(picked)
		}
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-editable-datetime': EditableDateTime
	}
}
