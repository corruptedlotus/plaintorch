import {
	DependencyConstraint,
	DependencyEndpointKind,
	DependencyTrigger,
	type Checkpoint,
	type DependencyEndpointRequest,
	type EndpointHit,
	type EndpointRef,
	type OnrushSprint
} from '@pleiades/sdk'
import { Notice, normalizePath } from 'obsidian'
import { core, getApp, PromptTextModal } from '..'
import { effectiveConstraint, effectiveTrigger, sourceRef, targetRef, type CanvasEdge, type CanvasNode } from './graphModel'
import type { CanvasContextMode } from './graphContext'
import { GLOBAL_CONTEXT_EXTENSION, serializeGlobalContext } from './globalContextFile'

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
		recurrenceId: ref.recurrenceId
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
 * Creates a new checkpoint, asking for its title first.
 *
 * With a sprint it is created already bound to it, so it appears on the canvas as one of the sprint's
 * checkpoints beside its milestone; without one it is created free, for a global context to pin. Resolves to
 * the new checkpoint, or nothing when the prompt was dismissed (not a failure) or the core refused.
 */
export async function createCheckpoint(sprint?: OnrushSprint): Promise<Checkpoint | undefined> {
	let title: string | undefined
	try {
		title = await PromptTextModal.prompt('New Checkpoint', 'Title')
	}
	catch {
		return undefined
	}

	if (!title) {
		return undefined
	}

	const created = await core.dependencies.createCheckpoint({ title, onrushSprintId: sprint?.id })
	if (!created) {
		new Notice('PLAINTORCH could not create that checkpoint.')
		return undefined
	}

	new Notice(`Checkpoint added: ${title}`)
	await (sprint ? refreshOnrush() : core.repos.checkpointList.refresh())
	return created
}

/**
 * Deletes the entity a node stands for, whatever kind it is.
 *
 * Unlike removing from the graph, this removes the entity itself — the objective, checkpoint, directive or
 * fate — and, through the graph refresh, every edge that touched it. A milestone is refused by the core, so
 * the menu never offers this for one.
 */
export async function deleteEntity(node: CanvasNode): Promise<boolean> {
	const deleted = await deleteByKind(node.ref.kind, node.ref.id)
	if (!deleted) {
		new Notice(`PLAINTORCH could not delete ${node.entity.title}.`)
		return false
	}

	new Notice(`Deleted ${node.entity.title}.`)
	await Promise.all([refreshGraph(), refreshOnrush()])
	return true
}

function deleteByKind(kind: DependencyEndpointKind, id: string): Promise<boolean> {
	switch (kind) {
		case DependencyEndpointKind.Objective: return core.objectives.delete(id)
		case DependencyEndpointKind.Directive: return core.directives.delete(id)
		case DependencyEndpointKind.Fate: return core.declaratives.deleteFate(id)
		case DependencyEndpointKind.Checkpoint: return core.dependencies.deleteCheckpoint(id)
		default: return Promise.resolve(false)
	}
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

/**
 * Onrush lifecycle, driven from the canvas's own management tray and empty states (PEP102.5).
 *
 * Each reports its own outcome and re-reads both sprints on success, since starting, activating, concluding or
 * deleting one changes which sprint each mode resolves. The navigation that follows some of these — showing the
 * active graph after an activation, say — is the canvas's to do, since only it holds the mode.
 */

/** Starts a brand-new active onrush, auto-titled by the core. */
export async function startActiveOnrush(): Promise<boolean> {
	const started = await core.onrush.startNew()
	if (!started) {
		new Notice('PLAINTORCH could not start a new Onrush.')
		return false
	}

	await refreshOnrush()
	return true
}

/** Creates a new planning onrush under a title the reader gives it. A dismissed prompt does nothing. */
export async function createPlanningOnrush(): Promise<boolean> {
	let title: string | undefined
	try {
		title = await PromptTextModal.prompt('New Onrush', 'Title')
	}
	catch {
		return false
	}

	if (!title) {
		return false
	}

	const planned = await core.onrush.plan({ title })
	if (!planned) {
		new Notice('PLAINTORCH could not create that Onrush.')
		return false
	}

	await refreshOnrush()
	return true
}

/** Activates a planning onrush, refusing when one is already active — only one runs at a time. */
export async function activatePlanningOnrush(sprintId: string): Promise<boolean> {
	if (await core.onrush.getCurrent()) {
		new Notice('There is already an active Onrush; conclude it first.')
		return false
	}

	const begun = await core.onrush.begin(sprintId)
	if (!begun) {
		new Notice('PLAINTORCH could not activate that Onrush.')
		return false
	}

	await refreshOnrush()
	return true
}

/** Deletes a planning onrush — its milestone and orders go with it, its objectives are freed. */
export async function deletePlanningOnrush(sprintId: string): Promise<boolean> {
	const deleted = await core.onrush.delete(sprintId)
	if (!deleted) {
		new Notice('PLAINTORCH could not delete that Onrush.')
		return false
	}

	await refreshOnrush()
	return true
}

/** Concludes the active onrush, setting its end date. */
export async function concludeOnrush(sprintId: string): Promise<boolean> {
	const ended = await core.onrush.end(sprintId)
	if (!ended) {
		new Notice('PLAINTORCH could not conclude that Onrush.')
		return false
	}

	await refreshOnrush()
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

/**
 * Writes a scratch global context to a new `.p7tpx` file and opens it, graduating it into something durable.
 *
 * The file's own view takes over from there — this is a one-time hand-off, not the ongoing save, which the
 * file view does as the context is edited. A dismissed name prompt does nothing; a name already taken is
 * refused rather than overwritten.
 */
export async function saveGlobalContextToFile(pinned: readonly EndpointHit[], layout: string | undefined): Promise<void> {
	let name: string | undefined
	try {
		name = await PromptTextModal.prompt('Save global context', 'File name')
	}
	catch {
		return
	}

	if (!name) {
		return
	}

	const app = getApp()
	const path = normalizePath(`${name.replace(/[\\/:*?"<>|]/g, '').trim()}.${GLOBAL_CONTEXT_EXTENSION}`)
	if (app.vault.getAbstractFileByPath(path)) {
		new Notice(`A file named "${path}" already exists.`)
		return
	}

	const file = await app.vault.create(path, serializeGlobalContext(pinned, layout))
	await app.workspace.getLeaf(true).openFile(file)
	new Notice(`Saved global context to ${path}.`)
}

/** Which repository record a mode reads its sprint from. */
export function sprintRecordOf(mode: CanvasContextMode) {
	return mode === 'onrush-planning' ? core.repos.onrushPlanning : core.repos.onrushCurrent
}
