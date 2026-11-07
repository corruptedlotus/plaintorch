import { describe, it, expect, beforeEach } from 'vitest'
import type { Directive } from '@pleiades/sdk'
import { PlaintorchCoreClient, type PlaintorchCoreRequest, type PlaintorchCoreResponse, type PlaintorchCoreTransport } from '@pleiades/sdk/plaintorch'
import { provideHost, type PlatformHost } from '../../host'
import { provideCore } from '../data/coreProvider'
import type { ContextMenuAction, ContextMenuEntry, ContextMenuSubmenu } from '../design/ContextMenu'
import { directiveActions } from '../grid/entityActions'
import { entityContextMenu } from './entityMenu'

// The onrush rows of the generic context menu (PEP102 patch 10): the two kinds a sprint holds offer to join one or to
// leave the one they are in, each through its own endpoint; a lunar directive offers neither.

const requests: string[] = []

function provideScriptedCore() {
	const transport: PlaintorchCoreTransport = {
		async send(request: PlaintorchCoreRequest): Promise<PlaintorchCoreResponse | undefined> {
			requests.push(`${request.method} ${request.path}`)
			const ok = request.method === 'DELETE'
			return { ok, status: ok ? 204 : 404, text: async () => '', header: () => undefined }
		}
	}
	provideCore(new PlaintorchCoreClient({ transports: [transport] }))
}

const entity = (type: string, extra: Record<string, unknown> = {}) => ({ '@type': type, id: 'e1', title: 'Entity', ...extra })

const labels = (entries: ContextMenuEntry[]) => entries.map(entry => 'label' in entry ? entry.label : 'separator' in entry ? '—' : entry.note)

const row = (entries: ContextMenuEntry[], label: string) =>
	entries.find((entry): entry is ContextMenuAction => 'label' in entry && entry.label === label)

describe('entityContextMenu onrush rows', () => {
	beforeEach(() => {
		requests.length = 0
		provideHost({ toast: () => undefined } as unknown as PlatformHost)
		provideScriptedCore()
	})

	it('offers a stellar directive in no sprint to join one', () => {
		const entries = entityContextMenu(entity('StellarDirective')).entries
		expect(labels(entries)).toContain('Add to Onrush')
		expect(labels(entries)).not.toContain('Remove from Onrush')
	})

	it('offers a stellar directive in a sprint to leave it, asking first', () => {
		const entries = entityContextMenu(entity('StellarDirective', { onrushSprintId: 's1' })).entries
		expect(labels(entries)).not.toContain('Add to Onrush')
		expect(row(entries, 'Remove from Onrush')?.confirm).toBeDefined()
	})

	it('offers a lunar directive neither row', () => {
		const entries = entityContextMenu(entity('LunarDirective')).entries
		expect(labels(entries)).not.toContain('Add to Onrush')
		expect(labels(entries)).not.toContain('Remove from Onrush')
	})

	it('offers an objective the same rows as a stellar directive', () => {
		expect(labels(entityContextMenu(entity('Objective')).entries)).toContain('Add to Onrush')
		expect(labels(entityContextMenu(entity('Objective', { onrushSprintId: 's1' })).entries)).toContain('Remove from Onrush')
	})

	it('takes each kind out through its own endpoint', async () => {
		const directive = entityContextMenu(entity('StellarDirective', { onrushSprintId: 's1' })).entries
		await row(directive, 'Remove from Onrush')!.run({ x: 0, y: 0 } as never)
		const objective = entityContextMenu(entity('Objective', { onrushSprintId: 's1' })).entries
		await row(objective, 'Remove from Onrush')!.run({ x: 0, y: 0 } as never)

		expect(requests).toContain('DELETE /api/directives/stellar/e1/onrush')
		expect(requests).toContain('DELETE /api/objectives/e1/onrush')
	})
})

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
