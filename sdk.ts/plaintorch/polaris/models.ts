import { model } from "@a11d/api-dotnet"
import type { Attentive, Decree, Eventive } from "../declaratives/models"
import type { Objective, ObjectiveCollege } from "../objectives/models"
import type { Timeframe } from "../directives/models"

/**
 * The incentive an {@link Executive} works at (PEP111): an objective (the day's plan to work it) or a decree
 * (the cycle's execution of the decree's routine, replacing the former Polaris-bound attentive). Never a fate —
 * fates are not worked. The two are told apart by {@link incentiveKind}.
 */
export type ExecutiveIncentive = Objective | Decree

/**
 * Which incentive kind an {@link Executive}'s {@link Executive.incentive} is. Reads the polymorphic `$type`
 * discriminator the core emits, falling back to a structural check (a decree carries `activeCelestron`).
 */
export function incentiveKind(incentive: ExecutiveIncentive | undefined): "objective" | "decree" | undefined {
	if (!incentive) {
		return undefined
	}

	const discriminator = (incentive as { $type?: string }).$type
	if (discriminator === "objective" || discriminator === "decree") {
		return discriminator
	}

	return "activeCelestron" in incentive ? "decree" : "objective"
}

/** Whether an executive's incentive is a decree (narrowing to {@link Decree}). */
export function isDecreeIncentive(incentive: ExecutiveIncentive | undefined): incentive is Decree {
	return incentiveKind(incentive) === "decree"
}

/** Whether an executive's incentive is an objective (narrowing to {@link Objective}). */
export function isObjectiveIncentive(incentive: ExecutiveIncentive | undefined): incentive is Objective {
	return incentiveKind(incentive) === "objective"
}
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
	/** Everything the cycle holds to work at (PEP111): objective- and decree-backed executives alike. */
	executives: Executive[] = []
	reflectives: Reflective[] = []
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

/**
 * Adds a decree to a Polaris cycle, creating a decree-backed {@link Executive} (PEP111). The cycle is the
 * temporal context, so no occurrence date/time is carried; the allocation seeds the executive.
 */
export interface PolarisDecreeAdd {
	decreeId: string
	/** Primary time allocation to seed (whole minutes); omitted, the decree's default length is used. */
	estimation?: number | undefined
	minimum?: number | undefined
	maximum?: number | undefined
	/**
	 * Affinity to seed (PEP100 patch 2). Omit for auto (the incentive's directive availability, then its college), an
	 * id to set, `null` for none.
	 */
	affinityTimeframeId?: number | null | undefined
}

export interface Executive {
	id: number
	polarisCycleId: string
	polarisCycle?: PolarisCycle | undefined
	/** The incentive this executive works at (PEP111): an objective or a decree id. Reassignable, never cleared. */
	incentiveId: string | undefined
	/** The incentive this executive works at — an objective or a decree; told apart by {@link incentiveKind}. */
	incentive?: ExecutiveIncentive | undefined
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
	/**
	 * Preferred timeframe for this reflective (affinity, PEP100 patch). Seeded at cycle begin through auto-inclusion:
	 * the decree's directive availability, then its college (PEP100 patch 2).
	 */
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
	/** Primary time allocation to seed on the planned executive, as a whole-minute working time unit. */
	estimation?: number | undefined
	/** Minimum time allocation to seed on the planned executive, as a whole-minute working time unit. */
	minimum?: number | undefined
	/** Maximum time allocation to seed on the planned executive, as a whole-minute working time unit. */
	maximum?: number | undefined
	/**
	 * Affinity to seed on the planned executive (PEP100 patch 2). Omit for auto (the incentive's directive
	 * availability, then its college), an id to set, `null` for none. A one-shot executive has no incentive, so auto
	 * leaves it without an affinity.
	 */
	affinityTimeframeId?: number | null | undefined
}

export interface PolarisExecutivePlanResult {
	objective: Objective | undefined
	executive: Executive
}

export interface ExecutiveUpdate {
	executed?: boolean | undefined
	/** The incentive (objective or decree) can be reassigned but not cleared. Wire field stays `objectiveId`. */
	objectiveId?: string | undefined
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
	/** Relocates the executive to another Polaris cycle (successor to moving a bound attentive, PEP111). */
	moveToPolarisCycleId?: string | undefined
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
