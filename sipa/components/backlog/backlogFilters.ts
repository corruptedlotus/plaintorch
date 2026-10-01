import { DecreeStatus, DirectiveStatus, FateStatus, ObjectiveStatus, type Directive } from '@pleiades/sdk'
import { isArchivedEntity, entityKindOf, entityStatusName } from '../grid/entityActions'
import type { GridEntity } from '../grid/entityTree'
import { statusDescriptors, type Status } from '../entities/StatusItem'
import type { SearchChip, SearchFilter, SearchFilterOption } from '../search/SearchFilterController'

/** The kinds of item the stellar backlog holds. */
type BacklogKind = 'directive' | 'objective' | 'fate' | 'decree'

/** What `type:` can name: one kind, the three incentive kinds, or every kind. */
type BacklogType = BacklogKind | 'incentive' | 'all'

const kinds: readonly BacklogKind[] = ['directive', 'objective', 'fate', 'decree']

const kindsOf: Record<BacklogType, readonly BacklogKind[]> = {
	directive: ['directive'],
	objective: ['objective'],
	fate: ['fate'],
	decree: ['decree'],
	incentive: ['objective', 'fate', 'decree'],
	all: kinds
}

/** The names of a status enum's members, in their declared order. */
const namesOf = (statuses: object) => Object.keys(statuses).filter(key => Number.isNaN(Number(key))) as Status[]

/** The states each kind moves through. */
const statusesOf: Record<BacklogKind, readonly Status[]> = {
	directive: namesOf(DirectiveStatus),
	objective: namesOf(ObjectiveStatus),
	fate: namesOf(FateStatus),
	decree: namesOf(DecreeStatus)
}

/** The kind of an item of the stellar backlog. */
function kindOfItem(item: GridEntity): BacklogKind {
	const kind = entityKindOf(item)
	return kind === 'stellar-directive' || kind === 'lunar-directive' ? 'directive' : kind
}

/** A value's written form: lowercase, words joined by hyphens — `OptOut` reads `opt-out`, a title `launch-the-atlas`. */
function slug(text: string) {
	return text
		.replace(/([a-z])([A-Z])/g, '$1-$2')
		.toLowerCase()
		.replace(/[^\p{L}\p{N}]+/gu, '-')
		.replace(/^-+|-+$/g, '')
}

const typeOptions: readonly SearchFilterOption<BacklogType>[] = [
	{ text: 'directive', value: 'directive', label: 'Directive', icon: 'directive' },
	{ text: 'objective', value: 'objective', label: 'Objective', icon: 'objective' },
	{ text: 'fate', value: 'fate', label: 'Fate', icon: 'fate' },
	{ text: 'decree', value: 'decree', label: 'Decree', icon: 'decree' },
	{ text: 'incentive', value: 'incentive', label: 'Incentive', icon: 'lucide:shapes', detail: 'Objectives, fates and decrees' },
	{ text: 'all', value: 'all', label: 'All', icon: 'lucide:layers', detail: 'Every type' }
]

/** `type:` — what kind of item: a directive, one incentive kind, any incentive, or anything at all. */
export const backlogTypeFilter: SearchFilter<GridEntity, BacklogType> = {
	key: 'type',
	label: 'Type',
	description: 'Directive, objective, fate, decree or incentive',
	icon: 'lucide:shapes',
	multiple: true,
	options: () => typeOptions,
	matches: (item, type) => kindsOf[type].includes(kindOfItem(item))
}

/** The `status:all` value: every state, archived ones included. */
const anyStatus = '*'

/**
 * `status:` — what state an item is in. A state is named once however many kinds share it (`status:active` finds
 * active directives, fates and decrees alike), and the states on offer narrow to the kinds `type:` has chosen. With no
 * status chosen the archived states (archived, over, opted out, abandoned) are hidden; `status:all` shows every state.
 */
export const backlogStatusFilter: SearchFilter<GridEntity, Status | typeof anyStatus> = {
	key: 'status',
	label: 'Status',
	description: 'Shared across types; narrowed by type:',
	icon: 'state-active',
	multiple: true,
	options: chips => {
		const chosen = chips.filter(chip => chip.key === backlogTypeFilter.key).flatMap(chip => kindsOf[(chip as SearchChip<BacklogType>).option.value])
		const offered = new Set((chosen.length > 0 ? chosen : kinds).flatMap(kind => statusesOf[kind]))
		return [
			// In the lifecycle order the status table keeps.
			...(Object.keys(statusDescriptors) as Status[])
				.filter(status => offered.has(status))
				.map((status): SearchFilterOption<Status> => ({
					text: slug(status),
					value: status,
					label: statusDescriptors[status].label,
					icon: statusDescriptors[status].icon
				})),
			{ text: 'all', value: anyStatus, label: 'All', icon: 'lucide:layers', detail: 'Every state, archived ones included' }
		]
	},
	matches: (item, status) => status === anyStatus || entityStatusName(item) === status,
	unset: item => !isArchivedEntity(item)
}

/**
 * `directive:` — a whole hierarchy: the directive chosen, every directive beneath it, and everything they hold. Its
 * values are the stellar directives, in the order of their hierarchy, each noted with the directives above it.
 */
export function backlogDirectiveFilter(directives: readonly Directive[]): SearchFilter<GridEntity, string> {
	const byId = new Map(directives.map(directive => [directive.id, directive]))

	// The directives from a directive up to its root, the directive first. The vault is hand-editable, so the walk
	// remembers where it has been rather than trusting a chain to end.
	const lineage = (directiveId: string | undefined): Directive[] => {
		const line: Directive[] = []
		for (let current = byId.get(directiveId ?? ''); current && !line.includes(current); current = byId.get(current.parentDirectiveId ?? '')) {
			line.push(current)
		}

		return line
	}

	const paths = new Map(directives.map(directive => [directive.id, lineage(directive.id).reverse().map(step => step.title)]))
	const ordered = [...directives].sort((a, b) => paths.get(a.id)!.join('\u0000').localeCompare(paths.get(b.id)!.join('\u0000')))

	// A value's written form is its title's; titles that read alike are told apart by a count.
	const taken = new Map<string, number>()
	const options = ordered.map((directive): SearchFilterOption<string> => {
		const base = slug(directive.title) || slug(directive.id)
		const count = (taken.get(base) ?? 0) + 1
		taken.set(base, count)
		const above = paths.get(directive.id)!.slice(0, -1)
		return {
			text: count === 1 ? base : `${base}-${count}`,
			value: directive.id,
			label: directive.title,
			icon: 'directive',
			detail: above.length > 0 ? above.join(' › ') : undefined
		}
	})

	return {
		key: 'directive',
		label: 'Directive',
		description: 'A whole hierarchy',
		icon: 'directive',
		multiple: true,
		options: () => options,
		matches: (item, directiveId) => {
			const start = kindOfItem(item) === 'directive' ? item.id : (item as { directiveId?: string }).directiveId
			return lineage(start).some(directive => directive.id === directiveId)
		}
	}
}
