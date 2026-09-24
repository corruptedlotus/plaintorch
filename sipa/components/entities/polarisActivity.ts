import type { Executive } from '@pleiades/sdk'
import { core, type ContextMenuEntry, type ContextMenuSpec } from '..'
import { ExecutiveModal } from './ExecutiveModal'
import { openEntityEditor } from './entityMenu'
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
 */
export function polarisActivityMenu(activity: PolarisActivity, changed?: (updated: PolarisActivity) => void): ContextMenuSpec {
	const done = isPolarisActivityDone(activity)
	const entity = entityOf(activity)
	const entries: ContextMenuEntry[] = [
		{
			label: done ? 'Not done' : 'Done',
			icon: done ? 'lucide:undo-2' : 'lucide:check',
			run: async () => {
				const updated = await setPolarisActivityDone(activity, !done)
				if (updated) {
					changed?.(updated)
				}
			}
		},
		{ label: 'Time Management', icon: 'lucide:clock', run: () => openPolarisActivityAllocation(activity, changed) },
		{ label: 'Edit', icon: 'lucide:pencil', disabled: !entity, run: () => { entity && openEntityEditor(entity) } },
		{ label: 'Move to next Polaris', icon: 'lucide:calendar-plus', run: () => moveToNextPolaris(activity) },
		{ separator: true },
		{ label: 'Remove', icon: 'lucide:trash-2', danger: true, run: () => removePolarisActivity(activity) }
	]

	return { title: polarisActivityTitle(activity), entries }
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

	await revalidateCycle()
	return updated
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

	await revalidateCycle()
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

	await revalidateCycle()
	toast(`Removed ${title} from the Polaris cycle.`, 'success')
	return true
}

/** The cycle and the briefing both draw the activity list; whichever is on screen re-reads itself. */
async function revalidateCycle() {
	await Promise.all([
		core.repos.polaris.revalidateObserved(),
		core.repos.briefing.revalidateIfObserved()
	])
}
