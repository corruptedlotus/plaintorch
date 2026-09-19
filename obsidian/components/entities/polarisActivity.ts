import { Notice } from 'obsidian'
import { AttentiveResolution, type Attentive, type Executive } from '@pleiades/sdk'
import { core, getApp, type ContextMenuEntry, type ContextMenuSpec } from '..'
import { attentiveOccurrence, AttentiveModal } from './AttentiveModal'
import { ExecutiveModal } from './ExecutiveModal'
import { openEntityEditor } from './entityMenu'

/**
 * One thing a Polaris cycle holds to work at: an executive (an objective planned into the cycle) or an attentive
 * (a decree's occurrence bound to it). The two are separate records with separate calls, but to the cycle card
 * they are the same kind of row — so what a row can *do* is written once, here, against this union.
 */
export type PolarisActivity =
	| { readonly kind: 'executive', readonly executive: Executive }
	| { readonly kind: 'attentive', readonly attentive: Attentive }

/** The kinds Polaris activities travel under in a drag-and-drop transfer — the records' runtime type names. */
export const polarisActivityKinds = ['Executive', 'Attentive'] as const

/** The transfer kind of one activity. */
export function polarisActivityKind(activity: PolarisActivity): string {
	return activity.kind === 'executive' ? 'Executive' : 'Attentive'
}

/** What an activity is called: the entity behind it, or an objective-less executive's own title. */
export function polarisActivityTitle(activity: PolarisActivity): string {
	return activity.kind === 'executive'
		? activity.executive.objective?.title ?? activity.executive.title ?? 'Executive'
		: activity.attentive.decree?.title ?? 'Attentive'
}

/** Whether the activity is done: an executive executed, an attentive resolved as done. */
export function isPolarisActivityDone(activity: PolarisActivity): boolean {
	return activity.kind === 'executive'
		? activity.executive.executed
		: activity.attentive.resolution === AttentiveResolution.Done
}

/** The objective or decree behind an activity — what "Edit" edits. An objective-less executive has none. */
function entityOf(activity: PolarisActivity) {
	return activity.kind === 'executive' ? activity.executive.objective : activity.attentive.decree
}

/**
 * The context menu of an activity inside its cycle. It replaces the generic entity menu on purpose: in a cycle,
 * the row is the *activity* — done or not, how much time it gets, in the cycle or out of it — and "Delete" there
 * would delete the objective or decree, which is never what removing a row from today means.
 *
 * `changed` is called with the updated record after Done or the allocation modal commits, so the row showing it
 * can redraw at once rather than wait for the cycle to be re-read.
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
		{ separator: true },
		{ label: 'Remove', icon: 'lucide:trash-2', danger: true, run: () => removePolarisActivity(activity) }
	]

	return { title: polarisActivityTitle(activity), entries }
}

/** Opens the activity's allocation modal — its time, its affinity, its done flag. */
export function openPolarisActivityAllocation(activity: PolarisActivity, changed?: (updated: PolarisActivity) => void) {
	if (activity.kind === 'executive') {
		new ExecutiveModal(getApp(), activity.executive, executive => changed?.({ kind: 'executive', executive })).open()
		return
	}

	new AttentiveModal(getApp(), activity.attentive, attentive => changed?.({ kind: 'attentive', attentive })).open()
}

/** Marks an activity done or not. Resolves to the updated activity, or `undefined` when the core refused. */
export async function setPolarisActivityDone(activity: PolarisActivity, done: boolean): Promise<PolarisActivity | undefined> {
	let updated: PolarisActivity | undefined
	if (activity.kind === 'executive') {
		const executive = await core.polaris.updateExecutive(activity.executive.id, { executed: done })
		// The update answers with the bare record; the row keeps drawing the objective it already had.
		updated = executive && { kind: 'executive', executive: { ...activity.executive, ...executive, objective: executive.objective ?? activity.executive.objective } }
	}
	else {
		const attentive = await core.declaratives.updateAttentive(attentiveOccurrence(activity.attentive), {
			resolution: done ? AttentiveResolution.Done : AttentiveResolution.Pending
		})
		updated = attentive && { kind: 'attentive', attentive: { ...activity.attentive, ...attentive, decree: attentive.decree ?? activity.attentive.decree } }
	}

	if (!updated) {
		new Notice(`PLAINTORCH could not update ${polarisActivityTitle(activity)}.`)
		return undefined
	}

	await revalidateCycle()
	return updated
}

/**
 * Takes an activity out of its cycle, deleting the executive or attentive and nothing else: the objective or
 * decree behind it stays as it is, state included.
 */
export async function removePolarisActivity(activity: PolarisActivity): Promise<boolean> {
	const title = polarisActivityTitle(activity)
	let removed = false
	try {
		removed = activity.kind === 'executive'
			? await core.polaris.removeExecutive(activity.executive.id)
			: await core.polaris.removeAttentive(activity.attentive.id)
	}
	catch (error) {
		console.error('PLAINTORCH: removing a Polaris activity failed.', error)
	}

	if (!removed) {
		new Notice(`PLAINTORCH could not remove ${title} from the cycle.`)
		return false
	}

	await revalidateCycle()
	new Notice(`Removed ${title} from the Polaris cycle.`)
	return true
}

/** The cycle and the briefing both draw the activity list; whichever is on screen re-reads itself. */
async function revalidateCycle() {
	await Promise.all([
		core.repos.polaris.revalidateObserved(),
		core.repos.briefing.revalidateIfObserved()
	])
}
