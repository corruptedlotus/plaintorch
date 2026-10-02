import { component, css, html, property, state, type PropertyValues } from "@a11d/lit"
import { OrbitUnit, type DeclarativeCalendar, type Eventive } from "@pleiades/sdk"
import { core } from ".."
import { CalendarRef } from "../data/CalendarRef"
import { CreationRowBase } from "../editing/CreationRowBase"
import type { EditablePart } from "../editing/EditableDataLink"
import { momentAtGranularity } from "../editing/EditableDateTime"
import type { GranularitySelect } from "../editing/GranularitySelect"
import type { TimeZoneSelect } from "../editing/TimeZoneSelect"
import "../editing/EditableDateTime"
import "../editing/EditableDuration"
import "../editing/GranularitySelect"
import "../editing/TimeZoneSelect"
import { openOccurrenceModal } from "./OccurrenceModal"
import { toast } from "../../host"

/** How far ahead the agenda lists eventives, in days — mirrored from the core's agenda horizon. */
export const eventiveHorizonDays = 7

/** A local calendar day as the `YYYY-MM-DD` key the core's dates use. */
function dateKey(date: Date): string {
	const pad = (value: number) => value.toString().padStart(2, '0')
	return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
}

/** Today, as the civil moment of an all-day occurrence. */
const today = () => `${dateKey(new Date())}T00:00:00`

/**
 * The inline row that adds a **manual** eventive to a fate: one added by hand rather than generated from its orbit,
 * so it has no RECURRENCE-ID and is addressed by its row id from then on.
 *
 * Its cells are the eventive's {@link https Epoch}, one member each:
 * - **Scope** — the granularity: how much of the moment is real, from a year down to the second. Changing it cuts
 *   the moment to the start of its new unit, and the segments follow;
 * - **When** — the civil moment, typed in segments ({@link EditableDateTime}) that show exactly as much of it as the
 *   scope makes real, on the fate's calendar ({@link calendar}, else the vault default);
 * - **Duration** — the nominal block length in Orbit duration notation, or none to fill one whole unit;
 * - **Time zone** — the zone the moment is anchored in, starting at the fate's own ({@link timeZone}, else floating).
 *
 * It starts as today, all day. Committing adds it through `POST /api/eventives`; the editor a committed row opens is
 * the occurrence editor.
 */
@component('p7t-eventive-creation-row')
export class EventiveCreationRow extends CreationRowBase<Eventive> {
	/** The fate the eventive is added to. */
	@property() fateId = ''

	/** The fate's own calendar, when it names one; unset, the vault default reads the moment. */
	@property({ type: Number }) calendar?: DeclarativeCalendar

	/** The fate's own time zone, the one a new eventive starts in; unset, it starts floating. */
	@property() timeZone?: string

	@state() private moment = today()
	@state() private granularity = OrbitUnit.Day
	@state() private duration?: string
	/** The zone chosen in the row; until one is (a choice of floating included), the fate's own applies. */
	@state() private zoneChoice?: { readonly zone: string | undefined }

	/** The calendar the moment is read on: the fate's, else the vault's preferred one. */
	private readonly calendars = new CalendarRef(this, () => this.calendar)

	private get zone() {
		// The core sends an unset (floating) zone as null.
		return this.zoneChoice ? this.zoneChoice.zone : this.timeZone ?? undefined
	}

	static override get styles() {
		return css`
			${super.styles}

			.cells {
				grid-template-columns: repeat(4, auto);
				justify-content: start;
				column-gap: 1.2em;
			}

			p7t-editable-datetime {
				font-size: 1.1em;
				font-weight: 300;
			}
		`
	}

	protected override get cells() {
		return html`
			<div class='cell'>
				<span class='caption'>Scope</span>
				<p7t-granularity-select
					.value=${this.granularity}
					@change=${(e: Event) => this.changeGranularity((e.target as GranularitySelect).value)}>
				</p7t-granularity-select>
			</div>
			<div class='cell'>
				<span class='caption'>When</span>
				<p7t-editable-datetime
					label='When'
					.calendar=${this.calendars.calendar}
					.granularity=${this.granularity}
					.value=${this.moment}
					@change=${(e: Event) => this.moment = (e.target as EditablePart<string>).value ?? this.moment}>
				</p7t-editable-datetime>
			</div>
			<div class='cell'>
				<span class='caption'>Duration</span>
				<p7t-editable-duration
					nullable
					.granularity=${this.granularity}
					.value=${this.duration}
					@change=${(e: Event) => this.duration = (e.target as EditablePart<string>).value}>
				</p7t-editable-duration>
			</div>
			<div class='cell'>
				<span class='caption'>Time zone</span>
				<p7t-time-zone-select
					.value=${this.zone}
					@change=${(e: Event) => this.zoneChoice = { zone: (e.target as TimeZoneSelect).value }}>
				</p7t-time-zone-select>
			</div>
		`
	}

	/** The calendar the moment was last cut on, so a change of calendar under an open row re-cuts it. */
	private cutOn?: DeclarativeCalendar

	/**
	 * Where a week, month or year begins depends on the calendar, so when the fate's calendar changes (its banner
	 * overriding it, or the preference arriving) the moment moves to the start of its unit on the new one.
	 */
	protected override willUpdate(changed: PropertyValues<this>) {
		super.willUpdate(changed)
		const calendar = this.calendars.calendar
		if (this.cutOn !== undefined && this.cutOn !== calendar) {
			this.moment = momentAtGranularity(this.moment, this.granularity, calendar)
		}

		this.cutOn = calendar
	}

	/** A new scope makes a different part of the moment real, so the moment moves to the start of its new unit. */
	private changeGranularity(granularity: OrbitUnit | undefined) {
		if (granularity === undefined || granularity === this.granularity) {
			return
		}

		this.granularity = granularity
		this.moment = momentAtGranularity(this.moment, granularity, this.calendars.calendar)
	}

	protected override async create(): Promise<Eventive | undefined> {
		// Cut once more on the calendar in force, so the moment sent is the start of its unit on the fate's calendar.
		const moment = momentAtGranularity(this.moment, this.granularity, this.calendars.calendar)
		const created = await core.declaratives.createEventive({
			fateId: this.fateId,
			epoch: { moment, granularity: this.granularity, duration: this.duration, timeZone: this.zone }
		})
		if (!created) {
			toast('PLAINTORCH could not add that eventive.', 'error')
			return undefined
		}

		// The agenda lists only the next few days, so an eventive added further out (or in the past) is not seen
		// joining it; say so rather than leave it looking lost.
		const now = new Date()
		const horizon = dateKey(new Date(now.getFullYear(), now.getMonth(), now.getDate() + eventiveHorizonDays))
		const day = moment.slice(0, 10)
		const listed = day >= dateKey(now) && day <= horizon
		toast(listed ? 'Eventive added.' : `Eventive added. It is listed here once it falls within the next ${eventiveHorizonDays} days.`, 'success')

		await Promise.all([
			core.repos.agenda.revalidateIfObserved(),
			core.repos.fates.revalidateIfObserved(this.fateId)
		])
		return created
	}

	/** Opens the occurrence editor on the new eventive. The create response carries no fate, so the known one is folded in. */
	protected override openEditor(created: Eventive) {
		const fate = created.fate ?? core.repos.fates.peek(this.fateId)
		openOccurrenceModal({ kind: 'eventive', eventive: { ...created, fate } as Eventive })
	}

	protected override reset() {
		this.moment = today()
		this.granularity = OrbitUnit.Day
		this.duration = undefined
		this.zoneChoice = undefined
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-eventive-creation-row': EventiveCreationRow
	}
}
