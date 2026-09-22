import { model } from "@a11d/api-dotnet"
import type { Directive, Timeframe } from "../directives/models"
import { ObjectiveCollege, type Objective } from "../objectives/models"
import type { Executive } from "../polaris/models"

export enum FateStatus {
	Active = 0,
	OptOut = 1,
	Cancelled = 2
}

export enum DecreeStatus {
	Active = 0,
	Abandoned = 1
}

/**
 * The calendar a declarative's Orbit resolves against (PEP100/PEP111). Resolution-only: only month/week/year
 * boundaries differ, and resolved occurrence instants are stored as concrete civil datetimes. `undefined` (null
 * on the wire) falls back to the kind default (decrees Pleiadean, fates Gregorian) until a preference lands.
 */
export enum DeclarativeCalendar {
	Gregorian = 0,
	Pleiadean = 1
}

/**
 * The calendar units an orbit can address (PEP111), doubling as an occurrence's granularity. Numeric values
 * mirror the core `OrbitUnit` (larger = finer): the wire sends {@link Epoch.granularity} as one of these.
 */
export enum OrbitUnit {
	Year = 1,
	Month = 2,
	Week = 3,
	Day = 4,
	Hour = 5,
	Minute = 6,
	Second = 7
}

/**
 * An occurrence's position in time (PEP111): a civil (wall-clock) {@link moment} plus how much of it is real
 * ({@link granularity}) and how long the real block lasts ({@link duration}). This is the shape the Orbit engine
 * emits, stored directly so an occurrence maps 1:1 onto an iCalendar instance. Floating is implicit — when the
 * granularity window is larger than the duration, the block floats within the window.
 */
export interface Epoch {
	/** The civil (wall-clock) moment, interpreted in {@link timeZone} or floating local time; never absolute UTC. */
	moment: string
	/** How much of {@link moment} is meaningful — the unit window the occurrence stands for (its accuracy). */
	granularity: OrbitUnit
	/** Nominal block length in Orbit duration notation (`"5h"`, `"1M"`); undefined fills exactly one granularity unit. */
	duration: string | undefined
	/** The time zone {@link moment} is anchored in; undefined means wall/floating time. */
	timeZone: string | undefined
	/** Server-computed: the calendar day of {@link moment} (`'YYYY-MM-DD'`). Read-only. */
	date: string
	/** Server-computed: whether the occurrence is day-or-coarser (no meaningful time of day). Read-only. */
	isAllDay: boolean
	/** Server-computed: the time of day of {@link moment} for a sub-day occurrence; null/undefined when all-day. Read-only. */
	timeOfDay: string | undefined
	/** Server-computed: the exclusive end moment ({@link moment} plus {@link duration}, or one granularity unit). Read-only. */
	endMoment: string
}

export enum EventiveResolution {
	Pending = 0,
	Missed = 1,
	Cancelled = 2,
	/** An opted-out fate's occurrences spawn/project stamped OptOut — generated but hidden from the agenda (PEP111). */
	OptOut = 3
}

export enum AttentiveResolution {
	Pending = 0,
	Done = 1,
	Skipped = 2
}

/**
 * Fate declarative (PEP100): an event-like incentive that happens rather than gets done. Orbit-only (PEP111):
 * a one-off is a fixed-datetime `Z{…}` literal, a recurring fate a recurrence orbit — there are no bare
 * date/time fields, the occurrence's shape comes from the resolved orbit.
 */
@model('Fate')
export class Fate {
	id: string = ''
	title: string = ''
	directiveId: string | undefined
	directive?: Directive | undefined
	parentIncentiveId: string | undefined
	status: FateStatus = FateStatus.Active
	orbit: string | undefined
	/** The calendar the orbit resolves against (PEP111); undefined falls back to the kind default (Gregorian for fates). */
	calendar: DeclarativeCalendar | undefined
	/** Read-only denormalized moment of the next upcoming occurrence, or undefined when none is scheduled (PEP111). */
	nextOccurrence: string | undefined
	eventives?: Eventive[]
}

/** Decree declarative (PEP100): an enduring routine/law controller. Exempt from the parent system. */
@model('Decree')
export class Decree {
	id: string = ''
	title: string = ''
	directiveId: string | undefined
	directive?: Directive | undefined
	status: DecreeStatus = DecreeStatus.Active
	orbit: string | undefined
	/** The calendar the orbit resolves against (PEP111); undefined falls back to the kind default (Pleiadean for decrees). */
	calendar: DeclarativeCalendar | undefined
	/** Read-only denormalized moment of the next upcoming occurrence, or undefined when none is scheduled (PEP111). */
	nextOccurrence: string | undefined
	/** Default length in whole minutes, seeding the allocation of executives this decree materializes. */
	defaultLength: number | undefined
	/** Celestron reward granted on each occurrence execution; not overridable per occurrence. */
	activeCelestron: number = 0
	/** Whether the decree participates in daily reflective generation (lunar hierarchies only). */
	reflect: boolean = false
	college: ObjectiveCollege = ObjectiveCollege.Unspecified
	/** The unbound attentive occurrences this decree has materialized (PEP111). */
	attentives?: Attentive[]
	/** The Polaris executives that work this decree — a decree in a cycle is an executive, not a bound attentive (PEP111). */
	executives?: Executive[]
}

/**
 * Per-occurrence instance of a fate or of an objective's due date. Never Polaris-bound. Its position in time is
 * the owned {@link Epoch} (PEP111); it carries no time allocation — allocation lives on the executive.
 */
export interface Eventive {
	id: number
	fateId: string | undefined
	fate?: Fate | undefined
	objectiveId: string | undefined
	objective?: Objective | undefined
	/** The occurrence's position in time — the (mutable) moment plus granularity, nominal duration, and zone. */
	epoch: Epoch
	/** Original occurrence slot date — the stable RECURRENCE-ID; unlike `epoch` it survives a reschedule. */
	recurrenceDate: string
	/** Original occurrence slot time; undefined for an all-day slot. With `recurrenceDate` forms the RECURRENCE-ID. */
	recurrenceTime: string | undefined
	resolution: EventiveResolution
}

/**
 * Per-occurrence instance of a decree — always an unbound occurrence (PEP111). Adding a decree into a Polaris
 * cycle creates an {@link Executive} instead, so an attentive carries no cycle binding and no time allocation.
 * Its position in time is the owned {@link Epoch}.
 */
export interface Attentive {
	id: number
	decreeId: string
	decree?: Decree | undefined
	/** The occurrence's position in time — the (mutable) moment plus granularity, nominal duration, and zone. */
	epoch: Epoch
	/** Original occurrence slot date — the stable RECURRENCE-ID; unlike `epoch` it survives a reschedule. */
	recurrenceDate: string
	/** Original occurrence slot time; undefined for an all-day slot. With `recurrenceDate` forms the RECURRENCE-ID. */
	recurrenceTime: string | undefined
	/**
	 * Exclusive period end date for super-day orbit granularities (week/month/year). A week-born attentive
	 * occupies its whole week. Null means single-day.
	 */
	periodEndDate: string | undefined
	resolution: AttentiveResolution
	/** UTC timestamp of the most recent transition to Done; cleared when the attentive is unresolved. */
	resolvedOn: string | undefined
	/** Preferred timeframe for this attentive (affinity), seeded from the decree's college; purely semantic. */
	affinityTimeframeId?: number | undefined
	/** The affined timeframe, resolved by the core. */
	affinityTimeframe?: Timeframe | undefined
}

/**
 * Plans a fate (PEP111). A fate is stored orbit-only: the {@link date}/{@link startTime}/{@link endTime}/
 * {@link eventDuration} fields are one-off creation sugar — with no explicit {@link orbit} they fold server-side
 * into a fixed-datetime `Z{y/M/d[Th:m]}` literal (with a `=<dur>` span for a timed window). An explicit
 * {@link orbit} takes precedence and the one-off fields are ignored.
 */
export interface FatePlan {
	title: string
	id?: string | undefined
	directiveId?: string | undefined
	parentIncentiveId?: string | undefined
	date?: string | undefined
	startTime?: string | undefined
	endTime?: string | undefined
	orbit?: string | undefined
	eventDuration?: number | undefined
}

export interface FateUpdate {
	title?: string | undefined
	status?: FateStatus | undefined
	/** The owning directive. Omit to keep, a value to move under it, `null` to lift to the top level. */
	directiveId?: string | null | undefined
	/** Parent incentive. Omit to keep, an id to set, `null` to clear. */
	parentIncentiveId?: string | null | undefined
	/** A fate is orbit-only (PEP111): reschedule — a one-off (`Z{…}` literal) or a recurrence — by setting a new orbit. */
	orbit?: string | undefined
}

export interface DecreePlan {
	title: string
	id?: string | undefined
	directiveId?: string | undefined
	orbit?: string | undefined
	defaultLength?: number | undefined
	activeCelestron?: number
	reflect?: boolean
}

export interface DecreeUpdate {
	title?: string | undefined
	status?: DecreeStatus | undefined
	/** The owning directive. Omit to keep, a value to move under it, `null` to lift to the top level. */
	directiveId?: string | null | undefined
	orbit?: string | undefined
	defaultLength?: number | undefined
	activeCelestron?: number | undefined
	reflect?: boolean | undefined
}

export interface EventiveUpdate {
	/** Reschedules the occurrence (an eventive is never bound, so its moment can always move). */
	date?: string | undefined
	/** Times: omit to keep, a value to set, `null` to clear. */
	startTime?: string | null | undefined
	endTime?: string | null | undefined
	resolution?: EventiveResolution | undefined
}

export interface AttentiveUpdate {
	/** Reschedules the occurrence (an attentive is always unbound, PEP111). */
	date?: string | undefined
	/** Time: omit to keep, a value to set, `null` to clear. */
	time?: string | null | undefined
	resolution?: AttentiveResolution | undefined
	/** Preferred timeframe (affinity). Omit to keep, an id to set, `null` to clear. */
	affinityTimeframeId?: number | null | undefined
}

/**
 * Addresses a single eventive occurrence by its owner UID (a fate or objective id) and RECURRENCE-ID
 * (Strategy 1). Interacting with the occurrence hardens it, so a projected occurrence needs no row id.
 */
export interface EventiveOccurrenceRef {
	ownerId: string
	recurrenceDate: string
	recurrenceTime?: string | null | undefined
}

/**
 * Addresses a single attentive occurrence by its decree + RECURRENCE-ID (`decreeId` + `recurrenceDate` +
 * `recurrenceTime`): the recurrence-id resolves a projected occurrence and its hardened twin identically, so
 * interacting with it hardens the projection without needing a row id. Attentives are always unbound (PEP111);
 * a decree placed into a cycle is an {@link Executive}, addressed by its own row id.
 */
export interface AttentiveOccurrenceRef {
	decreeId?: string | undefined
	recurrenceDate?: string | undefined
	recurrenceTime?: string | null | undefined
}
