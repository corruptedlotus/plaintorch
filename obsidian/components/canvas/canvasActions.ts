import {
	DependencyConstraint,
	DependencyEndpointKind,
	DependencyTrigger,
	type DependencyEndpointRequest,
	type EndpointRef,
	type OnrushSprint
} from '@pleiades/sdk'
import { Notice } from 'obsidian'
import { core, PromptTextModal } from '..'
import { effectiveConstraint, effectiveTrigger, sourceRef, targetRef, type CanvasEdge } from './graphModel'
import type { CanvasContextMode } from './graphContext'

/**
 * The canvas's writes.
 *
 * Every call here reports its own outcome. The node transport resolves a failed request to `undefined`
 * rather than throwing, so a rejected write — a cycle the core refused, a validation the client did not
 * anticipate — is indistinguishable from silence unless it is checked for at the call site. That check is
 * the reason these are functions rather than inline calls.
 */

/** Turns an endpoint into the shape a create request wants. */
function toRequest(ref: EndpointRef): DependencyEndpointRequest {
	return {
		kind: ref.kind,
		id: ref.id,
		recurrenceDate: ref.recurrenceDate,
		recurrenceTime: ref.recurrenceTime
	}
}

/**
 * Sends a create with the trigger and constraint each end actually admits.
 *
 * A checkpoint has no begin or finish, so its side is left empty however the caller asked; the core rejects a
 * checkpoint endpoint that carries one. The single place both drawing and reshaping route through, so neither
 * can send an edge the validator will refuse for that reason.
 */
function sendCreate(source: EndpointRef, target: EndpointRef, trigger: DependencyTrigger, constraint: DependencyConstraint) {
	return core.dependencies.create({
		source: toRequest(source),
		target: toRequest(target),
		trigger: source.kind === DependencyEndpointKind.Checkpoint ? undefined : trigger,
		constraint: target.kind === DependencyEndpointKind.Checkpoint ? undefined : constraint
	})
}

/**
 * Draws a new edge: the source blocks the target.
 *
 * Defaults to the pairing the core itself defaults to — satisfied when the prerequisite finishes, gating the
 * dependant's begin — which is the ordinary reading of "this comes before that". A checkpoint endpoint is the
 * exception on its own side: it has no begin or finish, so a checkpoint source carries no trigger and a
 * checkpoint target no constraint, which the core requires to be empty rather than defaulted.
 */
export async function createDependency(
	source: EndpointRef,
	target: EndpointRef,
	trigger: DependencyTrigger = DependencyTrigger.OnFinish,
	constraint: DependencyConstraint = DependencyConstraint.ToBegin
): Promise<boolean> {
	const created = await sendCreate(source, target, trigger, constraint)
	if (!created) {
		new Notice('PLAINTORCH refused that dependency.')
		return false
	}

	await refreshGraph()
	return true
}

/** Removes an edge. The entities it joined are untouched. */
export async function deleteDependency(edge: CanvasEdge): Promise<boolean> {
	const deleted = await core.dependencies.delete(edge.dependency.id)
	if (!deleted) {
		new Notice('PLAINTORCH could not remove that dependency.')
		return false
	}

	await refreshGraph()
	return true
}

/**
 * Changes what an edge triggers on or gates.
 *
 * There is no update endpoint for a dependency, so this is a delete followed by a create. The two are not one
 * transaction: if the create is refused the edge is already gone, which is why the original values are put
 * back rather than left to a refetch.
 */
export async function reshapeDependency(
	edge: CanvasEdge,
	trigger: DependencyTrigger,
	constraint: DependencyConstraint
): Promise<boolean> {
	if (trigger === effectiveTrigger(edge.dependency) && constraint === effectiveConstraint(edge.dependency)) {
		return true
	}

	const source = sourceRef(edge.dependency)
	const target = targetRef(edge.dependency)
	if (!await core.dependencies.delete(edge.dependency.id)) {
		new Notice('PLAINTORCH could not change that dependency.')
		return false
	}

	const recreated = await sendCreate(source, target, trigger, constraint)
	if (!recreated) {
		new Notice('PLAINTORCH refused the change; restoring the dependency.')
		await sendCreate(source, target, effectiveTrigger(edge.dependency), effectiveConstraint(edge.dependency))
		await refreshGraph()
		return false
	}

	await refreshGraph()
	return true
}

/**
 * Puts an objective into a sprint.
 *
 * In an onrush context this *is* what adding a node means, so it goes through the objective's repository:
 * that is what tells every other surface showing the objective that it moved.
 */
export async function addObjectiveToOnrush(objectiveId: string, sprint: OnrushSprint): Promise<boolean> {
	const added = await core.repos.objectives.mutate(objectiveId, async () =>
		await core.objectives.addToOnrush(objectiveId, sprint.id))

	new Notice(added ? `Added to ${sprint.title}.` : 'Could not add to that Onrush.')
	if (added) {
		await refreshOnrush()
	}

	return added
}

/** Takes an objective out of whichever sprint holds it. The objective itself survives. */
export async function removeObjectiveFromOnrush(objectiveId: string): Promise<boolean> {
	const removed = await core.repos.objectives.mutate(objectiveId, async () =>
		await core.objectives.removeFromOnrush(objectiveId))

	if (!removed) {
		new Notice('Could not remove that objective from the Onrush.')
		return false
	}

	await refreshOnrush()
	return true
}

/**
 * Adds a new checkpoint the sprint tracks, asking for its title first.
 *
 * Created already bound to the sprint, so it appears on the canvas as one of the sprint's checkpoints beside
 * its milestone. A dismissed prompt is not a failure and does nothing.
 */
export async function addCheckpointToOnrush(sprint: OnrushSprint): Promise<boolean> {
	let title: string | undefined
	try {
		title = await PromptTextModal.prompt('New Checkpoint', 'Title')
	}
	catch {
		return false
	}

	if (!title) {
		return false
	}

	const created = await core.dependencies.createCheckpoint({ title, onrushSprintId: sprint.id })
	if (!created) {
		new Notice('PLAINTORCH could not create that checkpoint.')
		return false
	}

	new Notice(`Checkpoint added: ${title}`)
	await refreshOnrush()
	return true
}

/** Deletes a checkpoint (and the edges touching it). A milestone is refused by the core and never offered. */
export async function deleteCheckpoint(checkpointId: string): Promise<boolean> {
	const deleted = await core.dependencies.deleteCheckpoint(checkpointId)
	if (!deleted) {
		new Notice('PLAINTORCH could not delete that checkpoint.')
		return false
	}

	await Promise.all([refreshGraph(), refreshOnrush()])
	return true
}

/** Re-reads the edges after a write to them. */
export async function refreshGraph(): Promise<void> {
	await core.repos.dependencyList.refresh()
}

/** Re-reads both sprints (and the checkpoint listing), since what a sprint holds is what just changed. */
export async function refreshOnrush(): Promise<void> {
	await Promise.all([
		core.repos.onrushCurrent.refresh(),
		core.repos.onrushPlanning.refresh(),
		core.repos.objectiveList.refresh(),
		core.repos.checkpointList.refresh()
	])
}

/** Persists the sprint's canvas layout — best effort, since a lost layout only costs a re-placement. */
export async function saveGraphLayout(sprintId: string, layout: string | undefined): Promise<void> {
	await core.onrush.setGraphLayout(sprintId, layout)
}

/** Which repository record a mode reads its sprint from. */
export function sprintRecordOf(mode: CanvasContextMode) {
	return mode === 'onrush-planning' ? core.repos.onrushPlanning : core.repos.onrushCurrent
}
