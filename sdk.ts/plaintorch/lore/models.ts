import { model } from "@a11d/api-dotnet"

@model("LorePage")
export class LorePage {
	// The lore API returns the entity (not a record projection), so the wire carries a real `id` — this must be a
	// stored field, not a getter over `puck`, or the identity map cannot absorb it and every `id` read is undefined.
	id!: string
	title!: string
	/** Same value as {@link id}; the entity also serializes its frontmatter PUCK. */
	puck?: string
	overrideIdentifier?: string
	/** The parent lore PUCK, driving the hierarchy. */
	parentId?: string
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

/** A lore page within the chronology index: the page plus whether it is currently active and, when the index is structured, its child pages. */
@model("IndexedLorePage")
export class IndexedLorePage extends LorePage {
	isActive: boolean = false
	children?: IndexedLorePage[]
}

/** In-place edit of a lore page's frontmatter-backed metadata (its title/hierarchy are path-derived, not editable). */
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
