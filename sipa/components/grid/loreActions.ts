import { core, ExpandingAction, IconName, PromptTextModal } from '..'
import type { LorePage } from '@pleiades/sdk'
import { toast } from '../../host'

/**
 * The icon a lore page draws, from its hierarchy level — matching the lore banner.
 *
 * Every level is the same entity, told apart only by its level discriminator, so the icon is the one thing that says
 * whether a row is an Era, a Chapter, an Act, or a Phase.
 */
export function loreLevelIcon(level: string | undefined): IconName {
	switch (level) {
		case 'Era': return 'lore-era'
		case 'Cha': return 'lore-chapter'
		case 'Act': return 'lore-act'
		case 'p': return 'lore-phase'
		default: return 'lorepage'
	}
}

/** The human-readable name of a lore level, for the row's level+index label. */
export function loreLevelLabel(level: string | undefined): string {
	switch (level) {
		case 'Era': return 'Era'
		case 'Cha': return 'Chapter'
		case 'Act': return 'Act'
		case 'p': return 'Phase'
		default: return 'Lore'
	}
}

/** The own-level index a lore page carries (its Era/Chapter/Act/Phase number), from its level. */
export function loreOwnIndex(page: LorePage): number | undefined {
	switch (page.level) {
		case 'Era': return page.era
		case 'Cha': return page.chapter
		case 'Act': return page.act
		case 'p': return page.phase
		default: return undefined
	}
}

/** A successive lore level a page can gain a child at. */
export interface LoreChildLevel {
	readonly level: string
	readonly label: string
	readonly icon: IconName
}

/**
 * The successive level a parent of the given level accepts: an undefined (or empty) parent level creates a top-level
 * Era, and each level below yields the next one, until a Phase — the lowest — which accepts no children.
 */
export function loreChildLevel(parentLevel: string | undefined): LoreChildLevel | undefined {
	switch (parentLevel) {
		case undefined:
		case '':
			return { level: 'Era', label: 'Era', icon: 'lore-era' }
		case 'Era':
			return { level: 'Cha', label: 'Chapter', icon: 'lore-chapter' }
		case 'Cha':
			return { level: 'Act', label: 'Act', icon: 'lore-act' }
		case 'Act':
			return { level: 'p', label: 'Phase', icon: 'lore-phase' }
		default:
			return undefined
	}
}

/**
 * Creates a lore page beneath a parent (or a top-level Era when none), asking for its title first. The core composes
 * its level, narrative index, and PUCK identity from the parent. Resolves to nothing when the prompt is dismissed.
 */
export async function createLorePage(parentPuck: string | undefined, levelLabel: string): Promise<boolean> {
	let title: string | undefined
	try {
		title = await PromptTextModal.prompt(`New ${levelLabel}`, 'Title')
	}
	catch {
		return false
	}

	if (!title) {
		return false
	}

	const created = await core.lore.create({ parentPuck, title })
	if (!created) {
		toast(`PLAINTORCH could not create that ${levelLabel.toLowerCase()}.`, 'error')
		return false
	}

	toast(`${levelLabel} created: ${title}`, 'success')
	await core.repos.loreList.refresh()
	return true
}

/** The lore grid's FAB action: create a top-level Era. */
export function loreCreationActions(): ExpandingAction[] {
	const era = loreChildLevel(undefined)!
	return [{
		key: era.level,
		icon: era.icon,
		label: `New ${era.label}`,
		run: async () => { await createLorePage(undefined, era.label) }
	}]
}

/** The add-action offered on a lore row: create its successive level, or nothing at all on a Phase. */
export function loreRowActions(page: LorePage): ExpandingAction[] {
	const child = loreChildLevel(page.level)
	if (!child) {
		return []
	}

	return [{
		key: child.level,
		icon: child.icon,
		label: `New ${child.label}`,
		run: async () => { await createLorePage(page.id, child.label) }
	}]
}
