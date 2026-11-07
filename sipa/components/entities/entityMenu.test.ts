import { describe, it, expect } from 'vitest'
import type { Directive } from '@pleiades/sdk'
import type { ContextMenuEntry, ContextMenuSubmenu } from '../design/ContextMenu'
import { directiveActions } from '../grid/entityActions'
import { entityContextMenu } from './entityMenu'

// The Add submenu of a directive's context menu: offered on both kinds of directive, nowhere else, and offering exactly
// what the directive's grid row offers.

const directive = (type: 'StellarDirective' | 'LunarDirective') => ({ '@type': type, id: `${type}-1`, title: 'A directive' })

const addOf = (entries: ContextMenuEntry[]) =>
	entries.find((entry): entry is ContextMenuSubmenu => 'entries' in entry && (entry as ContextMenuSubmenu).label === 'Add')

describe('entityContextMenu Add submenu', () => {
	it.each(['StellarDirective', 'LunarDirective'] as const)('offers what a %s row offers, with its icons and labels', type => {
		const entity = directive(type)
		const add = addOf(entityContextMenu(entity).entries)
		expect(add).toBeDefined()

		const row = directiveActions(entity as unknown as Directive)
		expect(add!.entries.map(entry => 'label' in entry ? [entry.label, entry.icon] : undefined))
			.toEqual(row.map(action => [action.label, action.icon]))
	})

	it('keeps a lunar hierarchy lunar and a stellar one stellar', () => {
		const labels = (type: 'StellarDirective' | 'LunarDirective') =>
			addOf(entityContextMenu(directive(type)).entries)!.entries.map(entry => 'label' in entry ? entry.label : undefined)

		expect(labels('StellarDirective')).toEqual(['Objective', 'Fate', 'Decree', 'Stellar Directive'])
		expect(labels('LunarDirective')).toEqual(['Objective', 'Fate', 'Decree', 'Lunar Directive'])
	})

	it('is not offered on an incentive', () => {
		const objective = { '@type': 'Objective', id: 'o-1', title: 'An objective' }
		expect(addOf(entityContextMenu(objective).entries)).toBeUndefined()
	})
})
