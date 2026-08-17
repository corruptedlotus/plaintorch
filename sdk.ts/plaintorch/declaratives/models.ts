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
	/**
	 * Exclusive period end date for super-day orbit granularities (week/month/year). A week-born attentive
	 * occupies its whole week, so multiple Polaris cycles can collide with it. Null means single-day.
	 */
	periodEndDate: string | undefined
	resolution: AttentiveResolution
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
	parentIncentiveId?: string | undefined
	clearParentIncentive?: boolean
	date?: string | undefined
	/** Drops the fixed date (used when switching a one-off fate onto a recurring orbit). */
	clearDate?: boolean
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

/** Occurrence details for interaction-triggered materialization. */
export interface EventiveMaterialization {
	date?: string | undefined
	startTime?: string | undefined
	endTime?: string | undefined
}

export interface EventiveUpdate {
	date?: string | undefined
	startTime?: string | undefined
	endTime?: string | undefined
	resolution?: EventiveResolution | undefined
	estimation?: number | undefined
	minimum?: number | undefined
	maximum?: number | undefined
}

/** Occurrence details for interaction-triggered materialization. */
export interface AttentiveMaterialization {
	date?: string | undefined
	time?: string | undefined
	estimation?: number | undefined
	minimum?: number | undefined
	maximum?: number | undefined
}

export interface AttentiveUpdate {
	/** Reschedules the occurrence; only valid while unbound. */
	date?: string | undefined
	time?: string | undefined
	resolution?: AttentiveResolution | undefined
	/** Moves the attentive to another cycle; only valid while Polaris-bound. */
	moveToPolarisCycleId?: string | undefined
	estimation?: number | undefined
	minimum?: number | undefined
	maximum?: number | undefined
}
