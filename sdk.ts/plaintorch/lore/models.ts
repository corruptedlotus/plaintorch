import { model } from "@a11d/api-dotnet"

@model("LorePage")
export class LorePage {
	get id() {
		return this.puck
	}
	puck!: string
	title!: string
	overrideIdentifier?: string
	parentPuck?: string
	beginning?: string
	level?: string
	relativePath?: string
	era?: number
	chapter?: number
	act?: number
	phase?: number
	indexedUtc!: string
	parent?: LorePage
}

/** In-place edit of a lore page's frontmatter-backed metadata (its title/hierarchy are path-derived, not editable). */
export interface LorePageUpdate {
	beginning?: string | undefined
	/** Clears the beginning date regardless of any provided value. */
	clearBeginning?: boolean
}
