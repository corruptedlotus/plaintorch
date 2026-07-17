import type { Attentive, Eventive } from "../declaratives/models"
import type { Objective, ObjectiveCollege } from "../objectives/models"
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

export interface PolarisCycle {
	id: string
	title: string
	forecast: PolarisForecast | undefined
	startTime: string | undefined
	endTime: string | undefined
	isForecast?: boolean
	executives: Executive[]
	reflectives: Reflective[]
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
	/** Preferred timeframe for execution (affinity, PEP100). Purely semantic. */
	affinityTimeframeId: number | undefined
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
	objectiveId?: string | undefined
	title?: string | undefined
	clearObjective?: boolean
	/** Primary time allocation, as a whole-minute working time unit. Leave undefined to keep the current value. */
	estimation?: number | undefined
	/** Minimum time allocation, as a whole-minute working time unit. Leave undefined to keep the current value. */
	minimum?: number | undefined
	/** Maximum time allocation, as a whole-minute working time unit. Leave undefined to keep the current value. */
	maximum?: number | undefined
	/** Clears the estimation allocation regardless of any provided value. */
	clearEstimation?: boolean
	/** Clears the minimum allocation regardless of any provided value. */
	clearMinimum?: boolean
	/** Clears the maximum allocation regardless of any provided value. */
	clearMaximum?: boolean
	/** Preferred timeframe for execution (affinity, PEP100). */
	affinityTimeframeId?: number | undefined
	/** Clears the affinity timeframe regardless of any provided value. */
	clearAffinityTimeframe?: boolean
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
	time?: string | undefined
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
