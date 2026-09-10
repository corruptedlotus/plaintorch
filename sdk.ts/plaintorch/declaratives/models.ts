import type { Directive } from "../directives/models"
import type { Objective } from "../objectives/models"
import type { PolarisCycle } from "../polaris/models"

export enum FateStatus {
	Active = 0,
	OptOut = 1,
	Cancelled = 2
}

export enum DecreeStatus {
	Active = 0,
	Abandoned = 1
}

export enum EventiveResolution {
	Pending = 0,
	Missed = 1,
	Cancelled = 2
}

export enum AttentiveResolution {
	Pending = 0,
	Done = 1,
	Skipped = 2
}

/** Fate declarative (PEP100): an event-like incentive that happens rather than gets done. */
export interface Fate {
	id: string
	title: string
	directiveId: string | undefined
	directive?: Directive | undefined
	parentIncentiveId: string | undefined
	status: FateStatus
	orbit: string | undefined
	date: string | undefined
	startTime: string | undefined
	endTime: string | undefined
	eventDuration: number | undefined
	isAllDay?: boolean
	eventives?: Eventive[]
}

/** Decree declarative (PEP100): an enduring routine/law controller. Exempt from the parent system. */
export interface Decree {
	id: string
	title: string
	directiveId: string | undefined
	directive?: Directive | undefined
	status: DecreeStatus
	orbit: string | undefined
	/** Default length in whole minutes, seeding materialized attentives. */
	defaultLength: number | undefined
	/** Celestron reward granted on each attentive execution; not overridable per attentive. */
	activeCelestron: number
	/** Whether the decree participates in daily reflective generation (lunar hierarchies only). */
	reflect: boolean
	attentives?: Attentive[]
}

/** Per-occurrence instance of a fate or of an objective's due date. Never Polaris-bound. */
export interface Eventive {
	id: number
	fateId: string | undefined
	fate?: Fate | undefined
	objectiveId: string | undefined
	objective?: Objective | undefined
	date: string
	startTime: string | undefined
	endTime: string | undefined
	/** Original occurrence slot date — the stable RECURRENCE-ID; unlike `date` it survives a reschedule. */
	recurrenceDate: string
	/** Original occurrence slot time; undefined for an all-day slot. With `recurrenceDate` forms the RECURRENCE-ID. */
	recurrenceTime: string | undefined
	resolution: EventiveResolution
	estimation: number | undefined
	minimum: number | undefined
	maximum: number | undefined
}

/** Per-occurrence instance of a decree; bound to a Polaris cycle only when added manually. */
export interface Attentive {
	id: number
	decreeId: string
	decree?: Decree | undefined
	polarisCycleId: string | undefined
	polarisCycle?: PolarisCycle | undefined
	date: string
	/** Time of day for sub-day orbit granularities; unbound attentives may carry any time and date. */
	time: string | undefined
	/** Original occurrence slot date — the stable RECURRENCE-ID; unlike `date` it survives a reschedule. */
	recurrenceDate: string
	/** Original occurrence slot time; undefined for an all-day slot. With `recurrenceDate` forms the RECURRENCE-ID. */
	recurrenceTime: string | undefined
	/**
	 * Exclusive period end date for super-day orbit granularities (week/month/year). A week-born attentive
	 * occupies its whole week, so multiple Polaris cycles can collide with it. Null means single-day.
	 */
	periodEndDate: string | undefined
	resolution: AttentiveResolution
	/** UTC timestamp of the most recent transition to Done; cleared when the attentive is unresolved. */
	resolvedOn: string | undefined
	estimation: number | undefined
	minimum: number | undefined
	maximum: number | undefined
	isBound?: boolean
}

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
	directiveId?: string | undefined
	/** Parent incentive. Omit to keep, an id to set, `null` to clear. */
	parentIncentiveId?: string | null | undefined
	/** Fixed date. Omit to keep, a value to set, `null` to clear (e.g. switching onto a recurring orbit). */
	date?: string | null | undefined
	startTime?: string | undefined
	endTime?: string | undefined
	orbit?: string | undefined
	eventDuration?: number | undefined
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
	directiveId?: string | undefined
	orbit?: string | undefined
	defaultLength?: number | undefined
	activeCelestron?: number | undefined
	reflect?: boolean | undefined
}

export interface EventiveUpdate {
	date?: string | undefined
	/** Times: omit to keep, a value to set, `null` to clear. */
	startTime?: string | null | undefined
	endTime?: string | null | undefined
	resolution?: EventiveResolution | undefined
	/** Whole minutes. Omit to keep, a value to set, `null` to clear. */
	estimation?: number | null | undefined
	minimum?: number | null | undefined
	maximum?: number | null | undefined
}

export interface AttentiveUpdate {
	/** Reschedules the occurrence; only valid while unbound. */
	date?: string | undefined
	/** Time: omit to keep, a value to set, `null` to clear. */
	time?: string | null | undefined
	resolution?: AttentiveResolution | undefined
	/** Moves the attentive to another cycle; only valid while Polaris-bound. */
	moveToPolarisCycleId?: string | undefined
	/** Whole minutes. Omit to keep, a value to set, `null` to clear. */
	estimation?: number | null | undefined
	minimum?: number | null | undefined
	maximum?: number | null | undefined
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
 * Addresses a single attentive occurrence one of two ways (Strategy 1). An unbound occurrence uses its decree +
 * RECURRENCE-ID (`decreeId` + `recurrenceDate` + `recurrenceTime`): interacting with it hardens the projected
 * occurrence, so no row id is needed. A Polaris-bound occurrence has no meaningful recurrence-id — it was placed
 * into a cycle by hand and always exists as a row — so it is addressed by its database `id` instead.
 */
export interface AttentiveOccurrenceRef {
	decreeId?: string | undefined
	recurrenceDate?: string | undefined
	recurrenceTime?: string | null | undefined
	id?: number | undefined
}
