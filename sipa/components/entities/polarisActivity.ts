import { isObjectiveIncentive, ObjectiveStatus, type Executive, type Objective, type PolarisCycle } from '@pleiades/sdk'
import { confirmAction, core, isIncentiveInCycle, removeConfirmation, type ContextMenuEntry, type ContextMenuSpec, type InteractionBubbleAnchor } from '..'
import { ExecutiveModal } from './ExecutiveModal'
import { entityMenuEntries } from './entityMenu'
import { toast } from '../../host'

/**
 * One thing a Polaris cycle holds to work at: an {@link Executive}. Since PEP111 a decree in a cycle is an
 * executive too (not a bound attentive), so a cycle holds exactly one kind of row — and what a row can *do* is
 * written once, here. The alias is kept so the surfaces that speak of "activities" read unchanged.
 */
export type PolarisActivity = Executive

/** The kind Polaris activities travel under in a drag-and-drop transfer — the record's runtime type name. */
export const polarisActivityKinds = ['Executive'] as const

/** The transfer kind of one activity. */
export function polarisActivityKind(_activity: PolarisActivity): string {
	return 'Executive'
}

/** What an activity is called: the incentive (objective or decree) behind it. */
export function polarisActivityTitle(activity: PolarisActivity): string {
	return activity.incentive?.title ?? 'Executive'
}

/** Whether the activity is done: its executive executed. */
export function isPolarisActivityDone(activity: PolarisActivity): boolean {
	return activity.executed
}

/** The incentive (objective or decree) behind an activity — what "Edit" edits. */
function entityOf(activity: PolarisActivity) {
	return activity.incentive
}

/**
 * The context menu of an activity inside its cycle. It replaces the generic entity menu on purpose: in a cycle,
 * the row is the *activity* — done or not, how much time it gets, in the cycle or out of it — and "Delete" there
 * would delete the objective or decree, which is never what removing a row from today means.
 *
 * `changed` is called with the updated executive after Done or the allocation modal commits, so the row showing
 * it can redraw at once rather than wait for the cycle to be re-read.
 *
 * Moving and removing ask first; marking an objective's executive done then offers to mark the objective done too,
 * where the menu was opened.
 */
export function polarisActivityMenu(activity: PolarisActivity, changed?: (updated: PolarisActivity) => void): ContextMenuSpec {
	const done = isPolarisActivityDone(activity)
	const entity = entityOf(activity)
	const title = polarisActivityTitle(activity)
	const entries: ContextMenuEntry[] = [
		{
			label: done ? 'Not done' : 'Done',
			icon: done ? 'lucide:undo-2' : 'lucide:check',
			run: async origin => {
				const updated = await setPolarisActivityDone(activity, !done)
				if (!updated) {
					return
				}

				changed?.(updated)
				const objective = !done ? await offerObjectiveDone(updated, origin) : undefined
				if (objective) {
					changed?.({ ...updated, incentive: objective })
				}
			}
		},
		{ label: 'Time Management', icon: 'lucide:clock', run: () => openPolarisActivityAllocation(activity, changed) },
		entity ? entityMenuEntries.edit(entity) : { label: 'Edit', icon: 'lucide:pencil', disabled: true, run: () => { } },
		{
			label: 'Move to next Polaris',
			icon: 'lucide:calendar-plus',
			confirm: { message: `Move "${title}" to the next Polaris cycle?`, action: 'Move', actionIcon: 'lucide:calendar-plus' },
			run: () => moveToNextPolaris(activity)
		},
		{ separator: true },
		{
			label: 'Remove',
			icon: 'lucide:trash-2',
			danger: true,
			// The executive goes with its tracked time, which is worth saying when there is some.
			confirm: removeConfirmation((activity.elapsed ?? 0) > 0
				? `Remove "${title}" from this Polaris cycle? Its tracked time goes with it.`
				: `Remove "${title}" from this Polaris cycle?`),
			run: () => removePolarisActivity(activity)
		}
	]

	return { title, entries }
}

/**
 * Having marked an activity done, offers beside `anchor` to mark the objective behind it done too — when it is an
 * objective still open (a decree has no such status). Resolves to the objective as updated, or `undefined` when not
 * offered, declined or refused.
 */
export async function offerObjectiveDone(activity: PolarisActivity, anchor: InteractionBubbleAnchor): Promise<Objective | undefined> {
	const objective = isObjectiveIncentive(activity.incentive) ? activity.incentive : undefined
	if (!objective || objective.status >= ObjectiveStatus.Done) {
		return undefined
	}

	const confirmed = await confirmAction(anchor, {
		message: `Mark "${objective.title}" as done too?`,
		action: 'Mark done',
		actionIcon: 'lucide:check',
		cancel: 'Not yet',
		severity: 'positive',
	})
	if (!confirmed) {
		return undefined
	}

	const updated = await core.repos.objectives.mutate(objective.id, () => core.objectives.shiftWorkflow(objective.id, { status: ObjectiveStatus.Done }))
	if (!updated) {
		toast(`PLAINTORCH could not mark ${objective.title} as done.`, 'error')
		return undefined
	}

	await revalidatePolarisCycles()
	return updated
}

/** Opens the activity's allocation modal — its time, its affinity, its done flag. */
export function openPolarisActivityAllocation(activity: PolarisActivity, changed?: (updated: PolarisActivity) => void) {
	new ExecutiveModal(activity, executive => changed?.(executive)).open()
}

/** Marks an activity done or not. Resolves to the updated activity, or `undefined` when the core refused. */
export async function setPolarisActivityDone(activity: PolarisActivity, done: boolean): Promise<PolarisActivity | undefined> {
	const executive = await core.polaris.updateExecutive(activity.id, { executed: done })
	// The update answers with the bare record; the row keeps drawing the incentive it already had.
	const updated = executive && { ...activity, ...executive, incentive: executive.incentive ?? activity.incentive }

	if (!updated) {
		toast(`PLAINTORCH could not update ${polarisActivityTitle(activity)}.`, 'error')
		return undefined
	}

	await revalidatePolarisCycles()
	return updated
}

/**
 * Sets an activity's estimate — its primary time allocation — on its own, the one allocation a planning surface edits
 * in place (the rest stay with the allocation modal). `undefined` clears it. Resolves to the updated activity, or
 * `undefined` when the core refused.
 */
export async function setPolarisActivityEstimation(activity: PolarisActivity, estimation: number | undefined): Promise<PolarisActivity | undefined> {
	const executive = await core.polaris.updateExecutive(activity.id, { estimation: estimation ?? null })
	if (!executive) {
		toast(`PLAINTORCH could not update the estimate of ${polarisActivityTitle(activity)}.`, 'error')
		return undefined
	}

	await revalidatePolarisCycles()
	// The update answers with the bare record; the row keeps drawing what it already resolved.
	return {
		...activity,
		...executive,
		incentive: executive.incentive ?? activity.incentive,
		affinityTimeframe: executive.affinityTimeframe ?? activity.affinityTimeframe,
	}
}

/**
 * Whether an activity can move into a cycle: not the one it is in, and not one already holding an executive for the
 * same objective or decree — a cycle holds one of each, and the core refuses a second.
 */
export function canMovePolarisActivity(activity: PolarisActivity, cycle: PolarisCycle): boolean {
	const incentiveId = activity.incentiveId ?? activity.incentive?.id
	return activity.polarisCycleId !== cycle.id && !(incentiveId && isIncentiveInCycle(incentiveId, cycle))
}

/**
 * Moves an activity into another Polaris cycle — another day's (PEP111's relocation). Its allocation and tracked time
 * travel with it unchanged; only the cycle it belongs to changes. Both cycles re-read themselves. Resolves to whether
 * it moved.
 */
export async function movePolarisActivity(activity: PolarisActivity, cycle: PolarisCycle): Promise<boolean> {
	const title = polarisActivityTitle(activity)
	if (!canMovePolarisActivity(activity, cycle)) {
		toast(`${cycle.title} already holds ${title}.`, 'info')
		return false
	}

	const moved = await core.polaris.updateExecutive(activity.id, { moveToPolarisCycleId: cycle.id })
	if (!moved) {
		toast(`PLAINTORCH could not move ${title} to ${cycle.title}.`, 'error')
		return false
	}

	await revalidatePolarisCycles(activity.polarisCycleId)
	return true
}

/**
 * Defers an activity to the next day's Polaris cycle (PEP111): the core creates that day's forecast cycle when
 * none exists, carries the tracked minutes forward as the new allocation envelope, and resets the tally. The
 * current cycle re-reads itself so the row leaves it and lands on the next.
 */
export async function moveToNextPolaris(activity: PolarisActivity): Promise<boolean> {
	const title = polarisActivityTitle(activity)
	let moved: PolarisActivity | undefined
	try {
		moved = await core.polaris.moveExecutiveToNextPolaris(activity.id)
	}
	catch (error) {
		console.error('PLAINTORCH: moving a Polaris activity to the next cycle failed.', error)
	}

	if (!moved) {
		toast(`PLAINTORCH could not move ${title} to the next Polaris cycle.`, 'error')
		return false
	}

	await revalidatePolarisCycles(activity.polarisCycleId)
	toast(`Moved ${title} to the next Polaris cycle.`, 'success')
	return true
}

/**
 * Takes an activity out of its cycle, deleting the executive and nothing else: the objective or decree behind
 * it stays as it is, state included.
 */
export async function removePolarisActivity(activity: PolarisActivity): Promise<boolean> {
	const title = polarisActivityTitle(activity)
	let removed = false
	try {
		removed = await core.polaris.removeExecutive(activity.id)
	}
	catch (error) {
		console.error('PLAINTORCH: removing a Polaris activity failed.', error)
	}

	if (!removed) {
		toast(`PLAINTORCH could not remove ${title} from the cycle.`, 'error')
		return false
	}

	await revalidatePolarisCycles(activity.polarisCycleId)
	toast(`Removed ${title} from the Polaris cycle.`, 'success')
	return true
}

/**
 * Every surface drawing a cycle's activities — a cycle itself, the briefing's current one, the forecast's days —
 * re-reads whatever of it is on screen.
 *
 * `emptied` names the cycles a write took an executive out of, which may have left one with none: those are re-read
 * on their own. The briefing and the forecast serve their cycles nested in a record, and the store withholds a nested
 * entity's EMPTY collection as "not loaded" — so a cycle whose last executive left would keep drawing it until the
 * cycle is fetched as the root of a response.
 */
export async function revalidatePolarisCycles(...emptied: Array<string | undefined>) {
	const cycleIds = [...new Set(emptied)].filter((id): id is string => !!id)
	await Promise.all([
		core.repos.polaris.revalidateObserved(),
		core.repos.briefing.revalidateIfObserved(),
		core.repos.polarisDays.revalidateObserved(),
		...cycleIds.map(id => core.repos.polaris.refresh(id))
	])
}
