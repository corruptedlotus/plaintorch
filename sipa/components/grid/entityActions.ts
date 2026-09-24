import { Notice } from 'obsidian'
import { Directive, Objective, typeNameOf } from '@pleiades/sdk'
import { addObjectiveToPolaris, core, ExpandingAction, IconName, PromptTextModal, type ScheduleValue } from '..'
import { host } from '../../host'
import type { GridEntity } from './entityTree'

/** What kind of thing a row holds, resolved from the runtime type the core stamped on it. */
export type EntityKind = 'stellar-directive' | 'lunar-directive' | 'objective' | 'fate' | 'decree'

/** The icon each kind is drawn with, matching the banners. */
const kindIcons: Record<EntityKind, IconName> = {
	'stellar-directive': 'directive',
	'lunar-directive': 'directive-lunar',
	'objective': 'objective',
	// Fate and decree stand in with these to match their banners (which set the same icons); dedicated
	// 'fate'/'decree' icons now exist but are not adopted here yet, to keep the grid and banners aligned.
	'fate': 'fate',
	'decree': 'decree'
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
 * Told apart by the runtime type the core stamps as `@type` (read through the SDK's {@link typeNameOf})
 * rather than by the presence of fields, since a stellar and a lunar directive share one model. The earlier
 * check read the polymorphic `$type` discriminator, which the core only emits for the base-typed directive
 * listing — fates and decrees are listed by their concrete type and carry no `$type`, so they fell through to
 * the objective default and rendered with its icon and columns.
 */
export function entityKindOf(entity: GridEntity): EntityKind {
	switch (typeNameOf(entity)) {
		case 'LunarDirective': return 'lunar-directive'
		case 'StellarDirective': return 'stellar-directive'
		case 'Fate': return 'fate'
		case 'Decree': return 'decree'
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

/** A field of an entity the grid lets you edit in place. */
export type EditableField = 'title' | 'status' | 'celestronValue' | 'orbit' | 'date'

/**
 * Saves one edited field through the call its kind and field require.
 *
 * There is no single shape for this: each kind has its own update contract, and a workflow state moves
 * through a dedicated shift endpoint rather than an update. What is common is that the write goes through
 * the repository, which is what publishes it to every other surface showing the entity.
 *
 * The value is read from the entity rather than passed in, because the binding has already written it
 * there — that is what makes the edit visible everywhere before the write is even sent.
 */
export async function saveEntityField(entity: GridEntity, field: EditableField): Promise<boolean> {
	const id = entity.id
	const repositories = core.repos
	const value = entity as unknown as Record<string, unknown>
	// An empty schedule has to be sent as an empty string to clear it; undefined would be a no-op.
	const orbit = (value.orbit as string | undefined) ?? ''

	switch (entityKindOf(entity)) {
		case 'stellar-directive':
			return !!await repositories.directives.mutate(id, async () => field === 'status'
				? await core.directives.shiftStellarWorkflow(id, { status: value.status as never })
				: await core.directives.updateStellar(id, { title: entity.title }))
		case 'lunar-directive':
			return !!await repositories.lunarDirectives.mutate(id, async () => field === 'status'
				? await core.directives.shiftLunarWorkflow(id, { status: value.status as never })
				: await core.directives.updateLunar(id, { title: entity.title }))
		case 'objective':
			return !!await repositories.objectives.mutate(id, async () => field === 'status'
				? await core.objectives.shiftWorkflow(id, { status: value.status as never })
				: await core.objectives.update(id, { title: entity.title, celestronValue: value.celestronValue as number }))
		case 'fate':
			// A fate is orbit-only (PEP111): the schedule column edits the orbit; a fixed date is folded into a
			// `Z{…}` literal by the schedule control, so there is no separate 'date' field here.
			return !!await repositories.fates.mutate(id, async () => await core.declaratives.updateFate(id, field === 'orbit'
				? { orbit }
				: field === 'status'
					? { status: value.status as never }
					: { title: entity.title }))
		case 'decree':
			return !!await repositories.decrees.mutate(id, async () => await core.declaratives.updateDecree(id, field === 'orbit'
				? { orbit }
				: field === 'status'
					? { status: value.status as never }
					: { title: entity.title }))
	}
}

/** The directive an entity currently sits under: a directive's parent, an incentive's owner. */
export function parentIdOf(entity: GridEntity): string | undefined {
	return isDirectiveKind(entityKindOf(entity))
		? (entity as Directive).parentDirectiveId
		: (entity as { directiveId?: string }).directiveId
}

/**
 * Why an entity cannot be moved under a directive — or, with no directive, lifted to the top level — or
 * `undefined` when it can.
 *
 * The same rules the core enforces, answered up front so a row that would refuse a drop never offers itself as
 * a target: a directive cannot go under itself or anything beneath it (the parent chain would close into a loop
 * no root reaches), and a lunar hierarchy stays lunar, a stellar one stellar (PEP100). Incentives go anywhere.
 * An entity already under the directive has nowhere to move, and one with no parent is already at the top.
 */
export function reparentRefusal(entity: GridEntity, parent: Directive | undefined): string | undefined {
	if (!parent) {
		return parentIdOf(entity) ? undefined : 'Already at the top level.'
	}

	if (parentIdOf(entity) === parent.id) {
		return 'Already there.'
	}

	const kind = entityKindOf(entity)
	if (!isDirectiveKind(kind)) {
		return undefined
	}

	if (kind !== entityKindOf(parent)) {
		return 'Lunar and stellar directives do not mix.'
	}

	// Walk up from the would-be parent; meeting the entity means the parent is the entity or beneath it. A
	// hand-edited vault can already loop, so the walk remembers where it has been.
	const byId = new Map((core.repos.directiveList.peek() ?? []).map(directive => [directive.id, directive]))
	const seen = new Set<string>()
	for (let current: Directive | undefined = parent; current && !seen.has(current.id); current = byId.get(current.parentDirectiveId ?? '')) {
		if (current.id === entity.id) {
			return 'A directive cannot go under itself.'
		}

		seen.add(current.id)
	}

	return undefined
}

/**
 * Moves an entity under a directive — a directive under a new parent, an incentive under a new owner — or, with
 * no directive, out from under the one it has: a directive to the top of the tree, an incentive to the world.
 *
 * The write goes through the entity's repository, so the canonical instance takes the new parent and every
 * surface drawing a tree from it reshapes on its own; nothing is re-listed.
 */
export async function reparentEntity(entity: GridEntity, parent: Directive | undefined): Promise<boolean> {
	const refusal = reparentRefusal(entity, parent)
	if (refusal) {
		new Notice(refusal)
		return false
	}

	const id = entity.id
	const repositories = core.repos
	// An explicit null is what clears a parent; leaving the field out would leave the parent alone.
	const parentId = parent?.id ?? null
	let moved = false
	try {
		switch (entityKindOf(entity)) {
			case 'stellar-directive':
				moved = !!await repositories.directives.mutate(id, async () => await core.directives.updateStellar(id, { parentDirectiveId: parentId }))
				break
			case 'lunar-directive':
				moved = !!await repositories.lunarDirectives.mutate(id, async () => await core.directives.updateLunar(id, { parentDirectiveId: parentId }))
				break
			case 'objective':
				moved = !!await repositories.objectives.mutate(id, async () => await core.objectives.update(id, { directiveId: parentId }))
				break
			case 'fate':
				moved = !!await repositories.fates.mutate(id, async () => await core.declaratives.updateFate(id, { directiveId: parentId }))
				break
			case 'decree':
				moved = !!await repositories.decrees.mutate(id, async () => await core.declaratives.updateDecree(id, { directiveId: parentId }))
				break
		}
	}
	catch (error) {
		console.error('PLAINTORCH: reparenting failed.', error)
	}

	new Notice(!moved
		? `PLAINTORCH could not move ${entity.title}.`
		: parent ? `Moved ${entity.title} under ${parent.title}.` : `Moved ${entity.title} to the top level.`)
	return moved
}

/** Whether a fate is a one-off occurrence — an orbit that is a fixed-datetime `Z{…}` literal (PEP111). */
export function isSingleInstanceFate(entity: GridEntity): boolean {
	const fate = entity as { orbit?: string }
	return !!fate.orbit && /^\s*Z\s*\{/.test(fate.orbit)
}

/**
 * Folds a chosen schedule into the orbit a fate stores (PEP111). A fate is orbit-only, so an orbit passes
 * through while a fixed date/time becomes a `Z{y/M/d[Th:m]}` literal (with a `=<dur>` span for an event
 * window). An empty result clears the schedule server-side.
 */
export function fateScheduleToOrbit(schedule: ScheduleValue): string {
	if (schedule.mode === 'orbit') {
		return schedule.orbit ?? ''
	}

	if (!schedule.date) {
		return ''
	}

	const [year = '', month = '', day = ''] = schedule.date.split('-')
	let literal = `Z{${Number(year)}/${Number(month)}/${Number(day)}`
	if (schedule.time) {
		const [hour = '', minute = ''] = schedule.time.split(':')
		literal += `T${Number(hour)}:${minute}`
	}

	literal += '}'
	const span = eventSpanNotation(schedule.time, schedule.endTime)
	return span ? `${literal}=${span}` : literal
}

/** The `=<dur>` span notation from a start/end time pair, or undefined when there is no positive window. */
function eventSpanNotation(startTime: string | undefined, endTime: string | undefined): string | undefined {
	if (!startTime || !endTime) {
		return undefined
	}

	const minutes = timeToMinutes(endTime) - timeToMinutes(startTime)
	if (minutes <= 0) {
		return undefined
	}

	const hours = Math.floor(minutes / 60)
	const mins = minutes % 60
	return `${hours > 0 ? `${hours}h` : ''}${mins > 0 ? `${mins}m` : ''}` || undefined
}

function timeToMinutes(time: string): number {
	const [hour = '', minute = ''] = time.split(':')
	return Number(hour) * 60 + Number(minute)
}

/**
 * Persists a fate's chosen schedule as its orbit (PEP111): a recurring orbit or a one-off `Z{…}` literal
 * folded from a fixed date/time. A fate is orbit-only, so there is only ever one shape to send.
 */
export async function saveFateSchedule(entity: GridEntity, schedule: ScheduleValue): Promise<boolean> {
	return !!await core.repos.fates.mutate(entity.id, async () =>
		await core.declaratives.updateFate(entity.id, { orbit: fateScheduleToOrbit(schedule) }))
}

/** Opens the note an entity is the authority for, in a new tab. Takes any entity with a PUCK identity. */
export async function openEntityNote(entity: { id: string }): Promise<void> {
	const existence = await core.repos.entityResolution.get(entity.id)
	if (!existence?.associatedNote) {
		new Notice('That entity has no note yet.')
		return
	}

	await host.navigation.openNote(existence.associatedNote)
}

/** Opens a known vault-relative markdown path in a new tab; either slash is accepted. */
export function openNotePath(vaultRelativePath: string): void {
	void host.navigation.openNote(vaultRelativePath.replace(/\\/g, '/'))
}

/**
 * Opens a note once its file is on disk, tolerating a write still draining in the core (PEP110). Pass
 * <c>core.lastWriteNotePending</c> from right after the mutation: when the core reported the write pending the host
 * announces the short wait up front. Even a ready write lands a beat after the call returns, so the host waits for
 * the file rather than assuming it is already there — the assumption that made a rename-reveal throw or a
 * create-then-open spawn a phantom note (see `NavigationHost.openNote`).
 */
export async function openNoteWhenReady(vaultRelativePath: string, pending = false): Promise<void> {
	await host.navigation.openNote(vaultRelativePath.replace(/\\/g, '/'), { waitForFile: true, pending })
}

/** The kinds whose note can be brought into being on demand — the implicit entities the SDK can materialize. */
const materializableKinds = new Set<string>(['objective', 'fate', 'decree'])

/**
 * Whether a kind's note can be created here — its entity is implicit and stays database-only (no file) until its
 * synchronization boundary is begun. The show/hide test a surface offering a "Create note" action uses.
 */
export function canMaterializeKind(kind: string | undefined): boolean {
	return !!kind && materializableKinds.has(kind)
}

/**
 * Brings an implicit entity's note into being and opens it — the file is what begins its synchronization boundary.
 *
 * Resolves first so an entity that already has a note simply opens it rather than being begun twice; then materializes
 * through the kind's begin call, re-reads the listings and resolution the change touches, and opens the written note.
 * A kind with no begin call reports that nothing could be created.
 */
export async function createEntityNote(entity: { id: string }, kind: string): Promise<void> {
	const existence = await core.repos.entityResolution.get(entity.id)
	if (existence?.associatedNote) {
		openNotePath(existence.associatedNote)
		return
	}

	const begin = beginNoteByKind(kind, entity.id)
	if (!begin) {
		new Notice('That entity has no note to create here.')
		return
	}

	const begun = await begin
	if (!begun) {
		new Notice('PLAINTORCH could not create that note.')
		return
	}

	// Whether the note is still draining is decided by the begin write above; the resolves below are reads and leave it.
	const notePending = core.lastWriteNotePending
	await Promise.all([refreshListings(), core.repos.entityResolution.refresh(entity.id)])
	const resolved = await core.repos.entityResolution.get(entity.id)
	if (resolved?.associatedNote) {
		await openNoteWhenReady(resolved.associatedNote, notePending)
	}
}

/** The materialization call a kind needs, or `undefined` for a kind that cannot be created here. */
function beginNoteByKind(kind: string, id: string): Promise<unknown> | undefined {
	switch (kind) {
		case 'objective': return core.objectives.beginObjective(id)
		case 'fate': return core.declaratives.beginFate(id)
		case 'decree': return core.declaratives.beginDecree(id)
		default: return undefined
	}
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
export function objectiveActions(objective: Objective): ExpandingAction[] {
	const objectiveId = objective.id
	return [
		{
			key: 'polaris',
			icon: 'polaris',
			label: 'Add to Polaris',
			run: async () => {
				await addObjectiveToPolaris(objective)
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
