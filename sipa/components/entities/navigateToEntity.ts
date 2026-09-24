import { core } from ".."
import { host } from "../../host"

/** Opens the note an entity is the authority for, in a new tab. Does nothing when the entity has no note. */
export const navigateToEntity = async (entityId: string) => {
	const existence = await core.system.resolveEntity(entityId)
	if (!existence?.associatedNote) return
	await host.navigation.openNote(existence.associatedNote)
}

/**
 * Follows an entity's note after a rename moved it: the resolution is refreshed (the path changed) and the host is
 * told the note moved, so one that shows notes brings the new file up — unless it is already the one in front —
 * waiting out a write the core reported pending. Silent where notes are not shown.
 */
export const followRenamedNote = async (entityId: string) => {
	const existence = await core.repos.entityResolution.refresh(entityId)
	if (!existence?.associatedNote) return
	await host.navigation.followMovedNote(existence.associatedNote, core.lastWriteNotePending)
}
