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

/**
 * A deadline moment (PEP111): a civil (wall-clock) {@link moment} plus an optional {@link timeZone}. Unlike an
 * occurrence's `Epoch` it carries no granularity or duration — it is a single point by which something is due.
 * Owned by its objective/checkpoint; round-trips to one compact `due:` frontmatter field.
 */
export interface Due {
	/** The civil (wall-clock) moment the item is due, interpreted in {@link timeZone} or floating local time. */
	moment: string
	/** The time zone {@link moment} is anchored in; undefined means wall/floating time. */
	timeZone: string | undefined
	/** Server-computed: the calendar day of {@link moment} (`'YYYY-MM-DD'`). Read-only. */
	date: string
	/** Server-computed: whether the due is a whole-day deadline (no meaningful time of day). Read-only. */
	isAllDay: boolean
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
	/** The optional deadline — a moment plus zone (PEP111), not just a date. */
	due: Due | undefined
	college: ObjectiveCollege = ObjectiveCollege.Unspecified
	status: ObjectiveStatus = ObjectiveStatus.Standby
	celestronValue: number = 0
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
}

export interface InitObjectiveRequest {
	path: string
}

export interface ObjectiveUpdate {
	title?: string | undefined
	/** The owning directive. Omit to keep, a value to move under it, `null` to lift to the top level. */
	directiveId?: string | null | undefined
	onrushSprintId?: string | undefined
	college?: ObjectiveCollege | undefined
	celestronValue?: number | undefined
	/** Due moment (PEP111). Omit to keep, a value to set, `null` to clear. */
	due?: Due | null | undefined
	/** Parent incentive. Omit to keep, an id to set, `null` to clear. */
	parentIncentiveId?: string | null | undefined
}

export interface ObjectiveWorkflowShift {
	status: ObjectiveStatus
}

export interface AddObjectiveToOnrushRequest {
	onrushSprintId: string
}
