import { model } from "@a11d/api-dotnet"
import type { Directive } from "../directives/models"
import type { OnrushSprint } from "../onrush/models"
import type { Executive } from "../polaris"
export enum ObjectiveStatus {
	'Standby' = 0,
	'Blocked' = 1,
	'Onrush' = 2,
	'Polaris' = 3,
	'Done' = 4,
	'Archived' = 5,
	'Failed' = 6
}

export enum ObjectiveCollege {
	'Unspecified' = 0,
	'Swords' = 1,
	'Creation' = 2,
	'Lore' = 3,
	'Eloquence' = 4,
	'Glamour' = 5
}

@model('Objective')
export class Objective {
	id: string = ''
	title: string = ''
	directiveId: string | undefined
	directive?: Directive | undefined
	/** Parent incentive per the PEP100 parent system: another objective (subtask) or a fate. */
	parentIncentiveId: string | undefined
	onrushSprintId: string | undefined
	onrushSprint?: OnrushSprint | undefined
	due: string | undefined
	college: ObjectiveCollege = ObjectiveCollege.Unspecified
	status: ObjectiveStatus = ObjectiveStatus.Standby
	celestronValue: number = 0
	isEnduring: boolean = false
	executives: Executive[] = []

	get statusName() {
		return ObjectiveStatus[this.status]
	}
}

export interface CreateObjectiveRequest {
	title: string
	id?: string | undefined
	directiveId?: string | undefined
	onrushSprintId?: string | undefined
	isEnduring?: boolean
}

export interface InitObjectiveRequest {
	path: string
}

export interface ObjectiveUpdate {
	title?: string | undefined
	directiveId?: string | undefined
	onrushSprintId?: string | undefined
	college?: ObjectiveCollege | undefined
	celestronValue?: number | undefined
	isEnduring?: boolean | undefined
	/** Due date. Omit to keep, a value to set, `null` to clear. */
	due?: string | null | undefined
	/** Parent incentive. Omit to keep, an id to set, `null` to clear. */
	parentIncentiveId?: string | null | undefined
}

export interface ObjectiveWorkflowShift {
	status: ObjectiveStatus
}

export interface AddObjectiveToOnrushRequest {
	onrushSprintId: string
}
