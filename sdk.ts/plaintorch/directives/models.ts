import { model } from "@a11d/api-dotnet"
import type { Objective } from "../objectives/models"
export enum DirectiveStatus {
	Planned = 0,
	Committed = 1,
	Active = 2,
	Fulfilled = 3,
	Over = 4,
	Failed = 5
}

export enum LunarDirectiveStatus {
	OnHold = 0,
	Active = 1,
	Stale = 2
}

@model('Directive')
export class Directive {
	/** Polymorphic discriminator emitted by the core: "stellar" or "lunar". */
	$type?: string
	id!: string
	title!: string
	codename: string | undefined
	parentDirectiveId: string | undefined
	parentDirective?: Directive | undefined
	subdirectives: Directive[] = []
	status: DirectiveStatus = DirectiveStatus.Planned
	tags: string[] = []
	due: string | undefined
	startDate: string | undefined
	endDate: string | undefined
	objectives: Objective[] = []
	timeframes?: Timeframe[]
	/** Moonlight state; only present on lunar directives (PEP100). */
	lunarStatus?: LunarDirectiveStatus

	get isLunar() {
		return this.$type === 'lunar' || this.lunarStatus !== undefined
	}
}

/** Directive-level definition of a portion of the day (PEP100). Purely semantic. */
export interface Timeframe {
	id: number
	directiveId: string
	title: string
	startTime: string
	endTime: string
	orbit: string | undefined
}

export interface CreateDirectiveRequest {
	title: string
	id?: string | undefined
	codename?: string | undefined
	parentDirectiveId?: string | undefined
}

export interface InitDirectiveRequest {
	path: string
}

export interface DirectiveUpdate {
	title?: string | undefined
	codename?: string | undefined
	parentDirectiveId?: string | undefined
	tags?: string[] | undefined
	due?: string | undefined
	startDate?: string | undefined
	endDate?: string | undefined
}

export interface DirectiveWorkflowShift {
	status: DirectiveStatus
}

export interface CreateLunarDirectiveRequest {
	title: string
	codename?: string | undefined
	parentDirectiveId?: string | undefined
}

export interface LunarDirectiveWorkflowShift {
	status: LunarDirectiveStatus
}

export interface TimeframePlan {
	title: string
	startTime: string
	endTime: string
	orbit?: string | undefined
}

export interface TimeframeUpdate {
	title?: string | undefined
	startTime?: string | undefined
	endTime?: string | undefined
	orbit?: string | undefined
	clearOrbit?: boolean
}
