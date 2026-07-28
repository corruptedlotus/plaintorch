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

/** The kind discriminator used to filter directives (PEP100). */
export type DirectiveKind = 'stellar' | 'lunar'

@model('Directive')
export class Directive {
	/** Polymorphic discriminator emitted by the core: "stellar" or "lunar". */
	$type?: DirectiveKind
	id!: string
	title!: string
	codename: string | undefined
	parentDirectiveId: string | undefined
	parentDirective?: Directive | undefined
	subdirectives: Directive[] = []
	/**
	 * Workflow state. Stellar directives carry a {@link DirectiveStatus} lifecycle value; lunar directives carry a
	 * {@link LunarDirectiveStatus} moonlight value (both are emitted on the same `status` field, PEP100).
	 */
	status: DirectiveStatus | LunarDirectiveStatus = DirectiveStatus.Planned
	tags: string[] = []
	objectives: Objective[] = []
	/** Scheduling dates; only present on stellar directives. */
	due?: string | undefined
	startDate?: string | undefined
	endDate?: string | undefined
	/** Timeframes; only present on lunar directives (PEP100). */
	timeframes?: Timeframe[]

	get isLunar() {
		return this.$type === 'lunar'
	}

	get isStellar() {
		return this.$type === 'stellar'
	}
}

/** Directive-level definition of a portion of the day (PEP100). Belongs to a lunar directive; purely semantic. */
export interface Timeframe {
	id: number
	directiveId: string
	title: string
	startTime: string
	endTime: string
	orbit: string | undefined
}

/** A timeframe paired with a summary of the lunar directive that owns it (global timeframe listing, PEP100). */
export interface DirectiveTimeframeRecord {
	id: number
	directiveId: string
	directiveTitle: string
	directiveCodename?: string | undefined
	directiveStatus: LunarDirectiveStatus
	title: string
	startTime: string
	endTime: string
	orbit?: string | undefined
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

/** Stellar directive update, including scheduling dates. */
export interface StellarDirectiveUpdate {
	title?: string | undefined
	codename?: string | undefined
	parentDirectiveId?: string | undefined
	tags?: string[] | undefined
	due?: string | undefined
	startDate?: string | undefined
	endDate?: string | undefined
}

/** Lunar directive update. Lunar directives are everglow and carry no scheduling dates (PEP100). */
export interface LunarDirectiveUpdate {
	title?: string | undefined
	codename?: string | undefined
	parentDirectiveId?: string | undefined
	tags?: string[] | undefined
}

export interface StellarDirectiveWorkflowShift {
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
