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
}

export interface Executive {
	id: number
	polarisCycleId: string
	polarisCycle?: PolarisCycle | undefined
	objectiveId: string | undefined
	objective?: Objective | undefined
	title: string | undefined
	executed: boolean
}

export interface Reflective {
	id: number
	description: string
	polarisCycleId: string
	polarisCycle?: PolarisCycle | undefined
	executed: boolean
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
