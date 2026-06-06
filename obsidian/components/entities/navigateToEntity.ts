import { App } from "obsidian"
import { core } from ".."

export const navigateToEntity = async (entityId: string) => {
	const existence = await core.system.resolveEntity(entityId)
	if (!existence || !existence.associatedNote) return
	((window as any).app! as App).workspace
		.openLinkText(existence.associatedNote!, '', true)
}