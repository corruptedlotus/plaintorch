import { typeNameOf } from '@pleiades/sdk'
import { core, navigateToEntity, type ContextMenuEntry, type ContextMenuSpec } from '..'
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
 * Builds the context menu for an entity, whatever kind it is (an entity item, a grid row).
 *
 * Every entity offers to be edited and to open its note; an objective that sits in an Onrush offers to leave it; and
 * any kind the SDK can delete offers deletion, set apart below a separator and in the destructive colour. A kind
 * with no delete call simply omits that row rather than offering one that would silently do nothing.
 */
export function entityContextMenu(entity: InteractableEntity): ContextMenuSpec {
	const typeName = typeNameOf(entity)
	const entries: ContextMenuEntry[] = [
		{ label: 'Edit', icon: 'lucide:pencil', run: () => openEntityEditor(entity) },
		{ label: 'Open note', icon: 'lucide:file-text', run: () => navigateToEntity(entity.id) }
	]

	if (typeName === 'Objective' && (entity as { onrushSprintId?: string }).onrushSprintId) {
		entries.push({ label: 'Remove from Onrush', icon: 'onrush', run: () => removeFromOnrush(entity.id) })
	}

	if (canDeleteEntity(typeName)) {
		entries.push({ separator: true }, { label: 'Delete', icon: 'lucide:trash-2', danger: true, run: () => deleteEntity(entity) })
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
