import { describe, it, expect, beforeEach, vi } from 'vitest'
import { PlaintorchCoreClient, type PlaintorchCoreRequest, type PlaintorchCoreResponse, type PlaintorchCoreTransport } from '@pleiades/sdk/plaintorch'
import { provideHost, type PlatformHost } from '../../host'
import { provideCore } from '../data/coreProvider'
import type { ContextMenuAction, ContextMenuEntry } from '..'
import { entityContextMenu } from './entityMenu'

// The onrush rows of the generic context menu (PEP102 patch 10): the two kinds a sprint holds offer to join one or to
// leave the one they are in, each through its own endpoint; a lunar directive offers neither.

// The components barrel reaches 3MO, whose `source` entries the global Vitest condition selects and cannot load, so the
// menu meets only the parts of it the onrush rows use: the real core, and inert confirmations and navigation.
vi.mock('..', async () => ({
	core: (await import('../data/coreProvider')).core,
	canMoveEntity: () => false,
	moveEntity: () => undefined,
	navigateToEntity: () => undefined,
	confirmAction: async () => true,
	removeConfirmation: (message: string) => ({ message }),
	deleteConfirmation: (message: string) => ({ message })
}))
vi.mock('./EntityEditModal', () => ({ EntityEditModal: { forEntity: () => undefined } }))

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
