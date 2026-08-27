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

/** In-place edit of a lore page's mutable metadata (its level and narrative index stay path-derived). */
export interface LorePageUpdate {
	/** Title. Omit to keep; a value renames the page and its self-named folder. */
	title?: string | undefined
	/** Beginning date. Omit to keep, a value to set, `null` to clear. */
	beginning?: string | null | undefined
}

/**
 * A request to create a lore page. The level is derived from the parent — omit `parentPuck` to create a top-level
 * Era, otherwise the new page becomes the parent's next successive level (Era→Cha→Act→p). The narrative index and
 * PUCK identity are composed by the core.
 */
export interface LorePageCreate {
	/** Parent lore PUCK. Omit for a top-level Era. */
	parentPuck?: string | undefined
	title: string
	/** Optional beginning date. */
	beginning?: string | null | undefined
}
