import { model } from "@a11d/api-dotnet"
import type { Attentive, Decree, Eventive } from "../declaratives/models"
import type { Objective, ObjectiveCollege } from "../objectives/models"
import type { Timeframe } from "../directives/models"
export enum PolarisExecutivePlanningMode {
	OneShot = 0,
	Standalone = 1,
	FromDirective = 2,
	FromObjective = 3
}

export interface PolarisForecast {
	forecastReference: string
	forecastTarget: string
}

@model('PolarisCycle')
export class PolarisCycle {
	id: string = ''
	title: string = ''
	forecast: PolarisForecast | undefined
	startTime: string | undefined
	endTime: string | undefined
	isForecast?: boolean
	executives: Executive[] = []
	reflectives: Reflective[] = []
	/** Polaris-bound attentives (PEP100). Unbound inclusions are served separately. */
	attentives?: Attentive[]
}

/**
 * Unbound eventives and attentives a Polaris cycle includes non-structurally because they fall within
 * 24h of its beginning (PEP100). The cycle never relationally owns these records.
 */
export interface PolarisCycleInclusions {
	eventives: Eventive[]
	attentives: Attentive[]
}

/**
 * The day-level agenda relative to today: unbound attentives requiring attention (same-day/24h and
 * previous unattended) and upcoming eventives within a short horizon (PEP100).
 */
export interface PolarisAgenda {
	attentives: Attentive[]
	eventives: Eventive[]
}

/** Manually adds a decree to a Polaris cycle, creating a Polaris-bound attentive (PEP100). */
export interface PolarisAttentiveAdd {
	decreeId: string
	date?: string | undefined
	time?: string | undefined
	estimation?: number | undefined
	minimum?: number | undefined
	maximum?: number | undefined
}

export interface Executive {
	id: number
	polarisCycleId: string
	polarisCycle?: PolarisCycle | undefined
	objectiveId: string | undefined
	objective?: Objective | undefined
	title: string | undefined
	executed: boolean
	/** Primary time allocation, as a whole-minute working time unit. Doubles as a progress marker. */
	estimation: number | undefined
	/** Minimum time allocation, as a whole-minute working time unit. */
	minimum: number | undefined
	/** Maximum time allocation, as a whole-minute working time unit. */
	maximum: number | undefined
	/** Raw count of tracked (passed) minutes spent on this executive. Defaults to 0. */
	elapsed: number
	/** Preferred timeframe for execution (affinity, PEP100). Purely semantic. */
	affinityTimeframeId: number | undefined
	/**
	 * The affined timeframe, resolved when the executive is served inside a cycle (PEP100 patch). Its icon stands
	 * in for the Celestron value on the executive item.
	 */
	affinityTimeframe?: Timeframe | undefined
}

export interface Reflective {
	id: number
	description: string
	polarisCycleId: string
	polarisCycle?: PolarisCycle | undefined
	executed: boolean
	/** Optional time of day (PEP100); further reflective behavior belongs to PEP104. */
	time: string | undefined
	/** Originating decree when generated through lunar reflection (PEP100). */
	decreeId: string | undefined
	/** Originating decree, carrying the relevant lunar directive when served. Absent for manual/drawn reflectives. */
	decree?: Decree | undefined
	/** Preferred timeframe for this reflective (affinity, PEP100 patch). Seeded from the decree's college via auto-inclusion. */
	affinityTimeframeId?: number | undefined
}

export interface PolarisExecutivePlan {
	mode: PolarisExecutivePlanningMode
	title?: string | undefined
	executiveTitle?: string | undefined
	directiveId?: string | undefined
	objectiveId?: string | undefined
	onrushSprintId?: string | undefined
	college?: ObjectiveCollege | undefined
	celestronValue?: number | undefined
	objectiveIsEnduring?: boolean
	/** Primary time allocation to seed on the planned executive, as a whole-minute working time unit. */
	estimation?: number | undefined
	/** Minimum time allocation to seed on the planned executive, as a whole-minute working time unit. */
	minimum?: number | undefined
	/** Maximum time allocation to seed on the planned executive, as a whole-minute working time unit. */
	maximum?: number | undefined
}

export interface PolarisExecutivePlanResult {
	objective: Objective | undefined
	executive: Executive
}

export interface ExecutiveUpdate {
	executed?: boolean | undefined
	/** The objective can be reassigned but not cleared. */
	objectiveId?: string | undefined
	title?: string | undefined
	/** Primary time allocation (whole minutes). Omit to keep, a value to set, `null` to clear. */
	estimation?: number | null | undefined
	/** Minimum time allocation (whole minutes). Omit to keep, a value to set, `null` to clear. */
	minimum?: number | null | undefined
	/** Maximum time allocation (whole minutes). Omit to keep, a value to set, `null` to clear. */
	maximum?: number | null | undefined
	/** Raw tracked-minute tally. Leave undefined to keep the current value; supply a value (including 0 to reset) to overwrite. */
	elapsed?: number | undefined
	/** Preferred timeframe for execution (affinity, PEP100). Omit to keep, an id to set, `null` to clear. */
	affinityTimeframeId?: number | null | undefined
}

export interface ReflectiveDrawRequest {
	polarisCycleId?: string | undefined
	count?: number
	includeRoutine?: boolean
	includeRandom?: boolean
}

export interface ReflectiveUpdate {
	description?: string | undefined
	executed?: boolean | undefined
	/** Time: omit to keep, a value to set, `null` to clear. */
	time?: string | null | undefined
}

export interface PolarisCycleUpdate {
	title?: string | undefined
}

export interface PolarisCycleTimeRequest {
	time?: string | undefined
}

export interface PolarisCyclePlanRequest {
	forecastReference: string
	daysAhead: number
	body?: string | undefined
}
