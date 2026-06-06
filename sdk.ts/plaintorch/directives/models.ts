import { model } from "@a11d/api-dotnet"
import type { Objective } from "../objectives/models"
export enum DirectiveStatus {
	Planned = 0,
	Committed = 1,
	Active = 2,
	Fulfilled = 3,
	Over = 4
}

@model('Directive')
export class Directive {
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
