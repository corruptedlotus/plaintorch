import { Notice } from 'obsidian'
import { Directive } from '@pleiades/sdk'
import { core, ExpandingAction, IconName, PromptTextModal } from '..'
import type { GridEntity } from './entityTree'

/** What kind of thing a row holds, resolved from the runtime type the core stamped on it. */
export type EntityKind = 'stellar-directive' | 'lunar-directive' | 'objective' | 'fate' | 'decree'

/** The icon each kind is drawn with, matching the banners. */
const kindIcons: Record<EntityKind, IconName> = {
	'stellar-directive': 'directive',
	'lunar-directive': 'directive-lunar',
	'objective': 'objective',
	// No dedicated fate or decree icon exists yet; these stand in, as they do on the banners.
	'fate': 'eventive',
	'decree': 'everglow'
}

const kindLabels: Record<EntityKind, string> = {
	'stellar-directive': 'Stellar Directive',
	'lunar-directive': 'Lunar Directive',
	'objective': 'Objective',
	'fate': 'Fate',
	'decree': 'Decree'
}

/**
 * Resolves what an entity is.
 *
 * Directives are told apart by the discriminator the core emits rather than by the presence of fields,
 * since a stellar and a lunar directive share one model.
 */
export function entityKindOf(entity: GridEntity): EntityKind {
	const type = (entity as { $type?: string }).$type
	switch (type) {
		case 'lunar': return 'lunar-directive'
		case 'stellar': return 'stellar-directive'
		case 'fate': return 'fate'
		case 'decree': return 'decree'
		default: return 'objective'
	}
}

/** Whether a kind nests, which only directives do. */
export function isDirectiveKind(kind: EntityKind): boolean {
	return kind === 'stellar-directive' || kind === 'lunar-directive'
}

export function entityIcon(entity: GridEntity): IconName {
	return kindIcons[entityKindOf(entity)]
}

export function entityKindLabel(kind: EntityKind): string {
	return kindLabels[kind]
}

/**
 * Creates an entity of a kind, asking for its title first.
 *
 * Resolves to nothing when the prompt is dismissed, which is not a failure and is reported as nothing
 * having happened.
 */
export async function createEntity(kind: EntityKind, directiveId?: string): Promise<boolean> {
	let title: string | undefined
	try {
		title = await PromptTextModal.prompt(`New ${kindLabels[kind]}`, 'Title')
	}
	catch {
		return false
	}

	if (!title) {
		return false
	}

	const created = await createOfKind(kind, title, directiveId)
	if (!created) {
		new Notice(`PLAINTORCH could not create that ${kindLabels[kind].toLowerCase()}.`)
		return false
	}

	new Notice(`${kindLabels[kind]} created: ${title}`)
	await refreshListings()
	return true
}

async function createOfKind(kind: EntityKind, title: string, directiveId?: string) {
	switch (kind) {
		case 'stellar-directive':
			return await core.directives.create({ title, parentDirectiveId: directiveId })
		case 'lunar-directive':
			return await core.directives.createLunar({ title, parentDirectiveId: directiveId })
		case 'objective':
			return await core.objectives.create({ title, directiveId })
		case 'fate':
			return await core.declaratives.createFate({ title, directiveId })
		case 'decree':
			return await core.declaratives.createDecree({ title, directiveId })
	}
}

/**
 * Renames an entity through the update call its kind uses.
 *
 * Every kind has its own update contract, so a rename cannot be expressed once; what is shared is that it
 * goes through the repository, which is what publishes it to the other surfaces showing the entity.
 */
export async function renameEntity(entity: GridEntity, title: string): Promise<boolean> {
	const kind = entityKindOf(entity)
	const id = entity.id
	const repositories = core.repos
	switch (kind) {
		case 'stellar-directive':
			return !!await repositories.directives.mutate(id, async () => await core.directives.updateStellar(id, { title }))
		case 'lunar-directive':
			return !!await repositories.lunarDirectives.mutate(id, async () => await core.directives.updateLunar(id, { title }))
		case 'objective':
			return !!await repositories.objectives.mutate(id, async () => await core.objectives.update(id, { title }))
		case 'fate':
			return !!await repositories.fates.mutate(id, async () => await core.declaratives.updateFate(id, { title }))
		case 'decree':
			return !!await repositories.decrees.mutate(id, async () => await core.declaratives.updateDecree(id, { title }))
	}
}

/** Opens the note an entity is the authority for, in a new tab. */
export async function openEntityNote(entity: GridEntity): Promise<void> {
	const existence = await core.repos.entityResolution.get(entity.id)
	if (!existence?.associatedNote) {
		new Notice('That entity has no note yet.')
		return
	}

	getWorkspace().openLinkText(existence.associatedNote, '', true)
}

/** The choices offered for creating something anywhere, in the order the FAB presents them. */
export function creationActions(directiveId?: string, kinds?: readonly EntityKind[]): ExpandingAction[] {
	const offered = kinds ?? ['lunar-directive', 'stellar-directive', 'objective', 'fate', 'decree'] as const
	return offered.map(kind => ({
		key: kind,
		icon: kindIcons[kind],
		label: kindLabels[kind],
		run: async () => { await createEntity(kind, directiveId) }
	}))
}

/**
 * The choices offered on a directive row.
 *
 * A child directive is always of the parent's own kind: a lunar hierarchy stays lunar, and a stellar one
 * stays stellar (PEP100).
 */
export function directiveActions(directive: Directive): ExpandingAction[] {
	const ownKind: EntityKind = entityKindOf(directive) === 'lunar-directive' ? 'lunar-directive' : 'stellar-directive'
	return creationActions(directive.id, ['objective', 'fate', 'decree', ownKind])
}

/** The choices offered on an objective row. */
export function objectiveActions(objectiveId: string): ExpandingAction[] {
	return [
		{
			key: 'polaris',
			icon: 'polaris',
			label: 'Add to Polaris',
			run: async () => {
				const added = await core.repos.objectives.mutate(objectiveId, async () =>
					await core.polaris.addObjectiveToCurrent(objectiveId))
				new Notice(added ? 'Added to active Polaris cycle.' : 'Could not add to Polaris.')
			}
		},
		{
			key: 'onrush',
			icon: 'onrush',
			label: 'Add to Onrush',
			run: async () => {
				// The active sprint when one is running, otherwise the one still being planned.
				const sprint = await core.onrush.getCurrent() ?? await core.onrush.getPlanning()
				if (!sprint) {
					new Notice('There is no Onrush to add to.')
					return
				}

				const added = await core.repos.objectives.mutate(objectiveId, async () =>
					await core.objectives.addToOnrush(objectiveId, sprint.id))
				new Notice(added ? `Added to ${sprint.title}.` : 'Could not add to Onrush.')
			}
		}
	]
}

/** Re-reads the listings the grid is built from, after something was created. */
export async function refreshListings(): Promise<void> {
	await Promise.all([
		core.repos.directiveList.refresh(),
		core.repos.objectiveList.refresh(),
		core.repos.fateList.refresh(),
		core.repos.decreeList.refresh()
	])
}

function getWorkspace() {
	return (window as unknown as { app: { workspace: { openLinkText(path: string, source: string, newLeaf: boolean): void } } }).app.workspace
}
