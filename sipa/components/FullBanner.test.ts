import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
// The components barrel first, in its own order: the banners reach each other through it.
import '.'
import { PlaintorchCoreClient, type PlaintorchCoreRequest, type PlaintorchCoreResponse, type PlaintorchCoreTransport } from '@pleiades/sdk/plaintorch'
import { provideHost, type PlatformHost } from '../host'
import { provideCore } from './data/coreProvider'
import type { FullBanner } from './FullBanner'

// The details pane's "Open in Planning": the full banner offers it beside the note actions on a stellar directive, where
// the host can pop a planning view out, and nowhere else.

const directives: Record<string, unknown> = {
	s1: { '@type': 'StellarDirective', id: 's1', title: 'A stellar directive' },
	l1: { '@type': 'LunarDirective', id: 'l1', title: 'A lunar directive' }
}

function provideHostWith(popOut: boolean) {
	provideHost({
		toast: () => undefined,
		media: { resourceUrl: () => undefined, isResourceUrl: () => false },
		icons: { lucide: () => undefined, lucideNames: () => [] },
		...popOut ? { canvasPopOut: { label: 'Open in a new window', popOut: async () => undefined } } : {}
	} as unknown as PlatformHost)
}

beforeEach(() => {
	const transport: PlaintorchCoreTransport = {
		async send(request: PlaintorchCoreRequest): Promise<PlaintorchCoreResponse | undefined> {
			const id = /^\/api\/directives\/([^/?]+)$/.exec(request.path)?.[1]
			const body = request.method === 'GET' && id ? directives[id] : undefined
			return { ok: body !== undefined, status: body !== undefined ? 200 : 404, text: async () => JSON.stringify(body ?? null), header: () => undefined }
		}
	}
	provideCore(new PlaintorchCoreClient({ transports: [transport] }))
})

afterEach(() => {
	document.body.replaceChildren()
})

/** The labels of the banner's action bar once its entity has arrived. */
async function actionsOf(xtype: string, puck: string): Promise<string[]> {
	const banner = document.createElement('p7t-full-banner') as FullBanner
	banner.xtype = xtype
	banner.puck = puck
	document.body.append(banner)
	const labels = () => Array.from(banner.shadowRoot?.querySelectorAll('.full-banner-actions p7t-button') ?? [], button => button.textContent?.trim() ?? '')
	await vi.waitFor(() => expect(labels()).toContain('Open note'))
	return labels()
}

describe('FullBanner Open in Planning', () => {
	it('is offered on a stellar directive where the host can pop a planning view out', async () => {
		provideHostWith(true)
		expect(await actionsOf('StellarDirective', 's1')).toEqual(['Open note', 'Open in Planning', 'Delete'])
	})

	it('is not offered where the host cannot pop one out', async () => {
		provideHostWith(false)
		expect(await actionsOf('StellarDirective', 's1')).not.toContain('Open in Planning')
	})

	it('is not offered on a lunar directive', async () => {
		provideHostWith(true)
		expect(await actionsOf('LunarDirective', 'l1')).not.toContain('Open in Planning')
	})
})
