import { typeNameOf } from '@pleiades/sdk'
import {
	canMoveEntity, core, deleteConfirmation, moveEntity, navigateToEntity, removeConfirmation, type ContextMenuAction,
	type ContextMenuEntry, type ContextMenuSpec, type GridEntity
} from '..'
import { EntityEditModal } from './EntityEditModal'
import { toast } from '../../host'

/** The minimal shape every interactable entity shares — a PUCK identity and a title. */
export interface InteractableEntity {
	id: string
	title: string
}

/** The runtime type names this menu can delete, each through its own domain's delete call. */
const deletableTypes = new Set(['Objective', 'StellarDirective', 'LunarDirective', 'Fate', 'Decree', 'LorePage'])

/**
 * The rows of an entity's menu, each built here and nowhere else, so a surface composing a menu of its own — the
 * canvas's node menu, the activity menu of a cycle's row — borrows the row rather than redrawing it.
 */
export const entityMenuEntries = {
	/** Opens the entity's editing modal (its banner). */
	edit: (entity: InteractableEntity): ContextMenuAction =>
		({ label: 'Edit', icon: 'lucide:pencil', run: () => openEntityEditor(entity) }),

	/** Opens the note the entity is the authority for. */
	openNote: (entity: InteractableEntity): ContextMenuAction =>
		({ label: 'Open note', icon: 'lucide:file-text', run: () => navigateToEntity(entity.id) }),

	/** Moves the entity through the hierarchy, through the directive picker (see {@link moveEntity}). */
	moveTo: (entity: InteractableEntity): ContextMenuAction =>
		({ label: 'Move to…', icon: 'lucide:folder-symlink', run: () => void moveEntity(entity as unknown as GridEntity) }),

	/** Takes an objective out of whichever Onrush holds it, asking first; the objective itself survives. */
	removeFromOnrush: (entity: InteractableEntity): ContextMenuAction => ({
		label: 'Remove from Onrush',
		icon: 'onrush',
		confirm: removeConfirmation(`Remove "${entity.title}" from its Onrush?`, 'default'),
		run: () => removeFromOnrush(entity.id)
	}),

	/** Deletes the entity, asking first — the one destructive row, drawn in the destructive colour. */
	delete: (entity: InteractableEntity): ContextMenuAction => ({
		label: 'Delete',
		icon: 'lucide:trash-2',
		danger: true,
		confirm: deleteConfirmation(`Delete "${entity.title}"?`),
		run: () => deleteEntity(entity)
	})
}

/**
 * What each kind adds to the rows every entity gets, by runtime type name. A kind not listed here adds nothing; a kind
 * with more to offer adds its rows here rather than in the item that draws it, so a grid row, a list item and a
 * heading all raise the same menu for the same entity.
 */
const kindEntries: Record<string, (entity: InteractableEntity) => ContextMenuEntry[]> = {
	Objective: entity => (entity as { onrushSprintId?: string }).onrushSprintId ? [entityMenuEntries.removeFromOnrush(entity)] : []
}

/**
 * Builds the context menu for an entity, whatever kind it is (an entity item, a grid row, a heading).
 *
 * Every entity offers to be edited and to open its note; a kind that sits in the hierarchy — a directive, an
 * incentive — offers to be moved through it; a kind with rows of its own adds them ({@link kindEntries}: an objective
 * in an Onrush offers to leave it); and any kind the SDK can delete offers deletion, set apart below a separator. A
 * kind with no delete call simply omits that row rather than offering one that would silently do nothing. Leaving the
 * Onrush and deleting both ask first.
 *
 * A surface whose row is not the entity itself — a cycle's activity, an occurrence, a canvas node — builds its own
 * menu from {@link entityMenuEntries} instead, adding and replacing rows as its row needs.
 */
export function entityContextMenu(entity: InteractableEntity): ContextMenuSpec {
	const typeName = typeNameOf(entity)
	const entries: ContextMenuEntry[] = [
		entityMenuEntries.edit(entity),
		entityMenuEntries.openNote(entity)
	]

	if (canMoveEntity(typeName)) {
		entries.push(entityMenuEntries.moveTo(entity))
	}

	entries.push(...kindEntries[typeName ?? '']?.(entity) ?? [])

	if (canDeleteEntity(typeName)) {
		entries.push({ separator: true }, entityMenuEntries.delete(entity))
	}

	return { title: entity.title, entries }
}

/** Whether an entity of this runtime type can be deleted here — the show/hide test a surface offering deletion uses. */
export function canDeleteEntity(typeName: string | undefined): boolean {
	return !!typeName && deletableTypes.has(typeName)
}

/** Opens an entity's editing modal (its banner), reporting when a kind has no editor here yet. */
export function openEntityEditor(entity: InteractableEntity): boolean {
	const modal = EntityEditModal.forEntity(typeNameOf(entity), entity)
	if (!modal) {
		toast('That entity has no editor here yet.', 'warning')
		return false
	}

	modal.open()
	return true
}

/**
 * Deletes an entity of whatever kind, reporting the outcome and re-reading the listings it appeared in. Kept module
 * -private so it does not collide with the canvas's node-based {@link deleteEntity} through the components barrel; the
 * exported {@link deleteEntityByType} is the form a surface holding only an identity (a banner keyed by PUCK) uses.
 */
async function deleteEntity(entity: InteractableEntity): Promise<boolean> {
	return await deleteEntityByType(typeNameOf(entity), entity.id, entity.title)
}

/**
 * Deletes an entity named by its runtime type, PUCK and title, reporting the outcome and re-reading the listings it
 * appeared in. The type-and-id form so a surface that never holds the full instance — a banner resolving an entity by
 * PUCK — can still delete it through the one delete path the context menu uses.
 */
export async function deleteEntityByType(typeName: string | undefined, id: string, title: string): Promise<boolean> {
	const request = deleteByType(typeName, id)
	if (!request) {
		toast(`${title} can't be deleted from here.`, 'warning')
		return false
	}

	const deleted = await request
	if (!deleted) {
		toast(`PLAINTORCH could not delete ${title}.`, 'error')
		return false
	}

	toast(`Deleted ${title}.`, 'success')
	await refreshAfterEntityChange()
	return true
}

/** The delete call each kind needs, or `undefined` for a kind with none. */
function deleteByType(typeName: string | undefined, id: string): Promise<boolean> | undefined {
	switch (typeName) {
		case 'Objective': return core.objectives.delete(id)
		case 'StellarDirective':
		case 'LunarDirective': return core.directives.delete(id)
		case 'Fate': return core.declaratives.deleteFate(id)
		case 'Decree': return core.declaratives.deleteDecree(id)
		case 'LorePage': return core.lore.delete(id)
		default: return undefined
	}
}

/** Takes an objective out of whichever sprint holds it; the objective itself survives. */
async function removeFromOnrush(objectiveId: string): Promise<void> {
	const removed = await core.repos.objectives.mutate(objectiveId, async () => await core.objectives.removeFromOnrush(objectiveId))
	toast(removed ? 'Removed from the Onrush.' : 'Could not remove from the Onrush.', removed ? 'success' : 'error')
	if (removed) {
		await refreshAfterEntityChange()
	}
}

/** Re-reads the listings an entity change touches, so every surface showing it updates. */
async function refreshAfterEntityChange(): Promise<void> {
	await Promise.all([
		core.repos.directiveList.refresh(),
		core.repos.objectiveList.refresh(),
		core.repos.fateList.refresh(),
		core.repos.decreeList.refresh(),
		core.repos.loreList.refresh(),
		core.repos.onrushCurrent.refresh(),
		core.repos.onrushPlanning.refresh(),
		core.repos.briefing.revalidateIfObserved()
	])
}
