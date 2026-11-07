import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { Component, component, html, property } from '@a11d/lit'
import { PlaintorchCoreClient, type PlaintorchCoreRequest, type PlaintorchCoreResponse, type PlaintorchCoreTransport } from '@pleiades/sdk/plaintorch'
import type { Objective } from '@pleiades/sdk'
import { provideHost, type PlatformHost } from '../../host'
import { core, provideCore } from './coreProvider'
import { DerivedRef } from './DerivedRef'
import { EntityRef, EntityWatch, QueryRef } from './EntityRef'

// The Lit bindings, mounted as real elements over a real client whose core is a table the test edits. What is
// pinned here is the lifecycle each binding owns — resolve, observe, re-render, release — and the states it
// carries, so no component has to wire any of it by hand.

type Scripted = { body: unknown } | { status: number } | 'silence'

const objective = (id: string, extra: Record<string, unknown> = {}) => ({ '@type': 'Objective', id, title: `Objective ${id}`, status: 0, ...extra })

function scriptedClient(routes: Record<string, Scripted>) {
	const requests: string[] = []
	const transport: PlaintorchCoreTransport = {
		async send(request: PlaintorchCoreRequest): Promise<PlaintorchCoreResponse | undefined> {
			requests.push(`${request.method} ${request.path}`)
			const scripted = routes[request.path] ?? { status: 404 }
			if (scripted === 'silence') {
				return undefined
			}

			const status = 'status' in scripted ? scripted.status : 200
			const body = 'body' in scripted ? scripted.body : undefined
			return { ok: status < 300, status, text: async () => body === undefined ? '' : JSON.stringify(body), header: () => undefined }
		}
	}
	return { client: new PlaintorchCoreClient({ transports: [transport] }), routes, requests }
}

const toasts: string[] = []
const fakeHost = { toast: (message: string) => { toasts.push(message) } } as unknown as PlatformHost

/** Lets every pending fetch, absorption and Lit update settle. */
async function settle(): Promise<void> {
	for (let turn = 0; turn < 6; turn++) {
		await new Promise((resolve) => setTimeout(resolve, 0))
	}
}

async function mount<T extends HTMLElement>(tag: string, setUp?: (element: T) => void): Promise<T> {
	const element = document.createElement(tag) as T
	setUp?.(element)
	document.body.appendChild(element)
	await settle()
	return element
}

@component('test-entity-host')
class EntityHost extends Component {
	@property() puck = ''
	public renders = 0
	public readonly ref = new EntityRef<Objective>(this, () => core.repos.objectives, () => this.puck || undefined)

	protected override get template() {
		this.renders++
		return html`<span>${this.ref.value?.title ?? ''}</span>`
	}
}

@component('test-watch-host')
class WatchHost extends Component {
	@property({ type: Object }) entity?: Objective
	public renders = 0
	public readonly watch = new EntityWatch(this, () => this.entity)

	protected override get template() {
		this.renders++
		return html`<span>${this.entity?.title ?? ''}</span>`
	}
}

@component('test-query-host')
class QueryHost extends Component {
	public renders = 0
	public readonly query = new QueryRef<Objective>(this, core.repos.objectives, (o) => o.status === 0, (a, b) => a.title.localeCompare(b.title))

	protected override get template() {
		this.renders++
		return html`${this.query.items.map((o) => html`<i>${o.id}</i>`)}`
	}
}

@component('test-derived-host')
class DerivedHost extends Component {
	public renders = 0
	public readonly listing = new DerivedRef<Objective[]>(this, core.repos.objectiveList)

	protected override get template() {
		this.renders++
		return html`${(this.listing.value ?? []).map((o) => html`<i>${o.id}</i>`)}`
	}
}

void EntityHost
void WatchHost
void QueryHost
void DerivedHost

let shop: ReturnType<typeof scriptedClient>

beforeEach(() => {
	vi.spyOn(console, 'debug').mockImplementation(() => { })
	vi.spyOn(console, 'warn').mockImplementation(() => { })
	toasts.length = 0
	shop = scriptedClient({
		'/api/objectives/O1': { body: objective('O1') },
		'/api/objectives/O2': { body: objective('O2', { status: 2 }) },
		'/api/objectives': { body: [objective('O1'), objective('O2', { status: 2 })] }
	})
	provideHost(fakeHost)
	provideCore(shop.client)
})

afterEach(() => {
	document.body.innerHTML = ''
	vi.restoreAllMocks()
})

describe('EntityRef', () => {
	it('resolves the entity for its host and renders it', async () => {
		const host = await mount<EntityHost>('test-entity-host', (element) => { element.puck = 'O1' })

		expect(host.shadowRoot!.textContent).toContain('Objective O1')
		expect(host.ref.value).toBe(shop.client.store.peek('Objective:O1'))
		expect(host.ref.loading).toBe(false)
		expect(host.ref.notLoaded).toBe(false)
		expect(host.ref.removed).toBe(false)
	})

	it('re-renders when the entity changes anywhere, and stops once the host leaves the document', async () => {
		const host = await mount<EntityHost>('test-entity-host', (element) => { element.puck = 'O1' })
		const entity = host.ref.value!
		const rendersBefore = host.renders

		entity.title = 'renamed elsewhere'
		core.repos.objectives.touch('O1')
		await settle()
		expect(host.renders).toBe(rendersBefore + 1)
		expect(host.shadowRoot!.textContent).toContain('renamed elsewhere')

		host.remove()
		await settle()
		expect(shop.client.store.hasSubscribers('Objective:O1')).toBe(false)
	})

	it('follows a change of identifier while the host stays mounted', async () => {
		const host = await mount<EntityHost>('test-entity-host', (element) => { element.puck = 'O1' })

		host.puck = 'O2'
		await settle()

		expect(host.shadowRoot!.textContent).toContain('Objective O2')
		expect(shop.client.store.hasSubscribers('Objective:O1')).toBe(false)
		expect(shop.client.store.hasSubscribers('Objective:O2')).toBe(true)
	})

	it('shares one request between two hosts of the same entity', async () => {
		await Promise.all([
			mount<EntityHost>('test-entity-host', (element) => { element.puck = 'O1' }),
			mount<EntityHost>('test-entity-host', (element) => { element.puck = 'O1' })
		])

		expect(shop.requests.filter((request) => request === 'GET /api/objectives/O1')).toHaveLength(1)
	})

	it('reads as not loaded, and not loading, when the core could not be asked', async () => {
		shop.routes['/api/objectives/O1'] = 'silence'
		const host = await mount<EntityHost>('test-entity-host', (element) => { element.puck = 'O1' })

		expect(host.ref.value).toBeUndefined()
		expect(host.ref.loading).toBe(false)
		expect(host.ref.notLoaded).toBe(true)
		expect(host.ref.removed).toBe(false)
	})

	it('reads as removed when the core answers that the entity does not exist, and re-renders to say so', async () => {
		const host = await mount<EntityHost>('test-entity-host', (element) => { element.puck = 'O1' })
		const rendersBefore = host.renders

		delete shop.routes['/api/objectives/O1']
		await host.ref.refresh()
		await settle()

		expect(host.ref.removed).toBe(true)
		expect(host.ref.value).toBeUndefined()
		expect(host.renders).toBeGreaterThan(rendersBefore)
		expect(host.shadowRoot!.textContent).not.toContain('Objective O1')
	})

	it('commit broadcasts the edit, and rolls it back with a toast when the core rejects it', async () => {
		const host = await mount<EntityHost>('test-entity-host', (element) => { element.puck = 'O1' })
		const other = await mount<EntityHost>('test-entity-host', (element) => { element.puck = 'O1' })
		const entity = host.ref.value!

		host.ref.beginEdit()
		entity.title = 'accepted'
		// The core applies the write, so the refetch that follows a success reads the new title back.
		expect(await host.ref.commit(async () => { shop.routes["/api/objectives/O1"] = { body: objective("O1", { title: "accepted" }) }; return { saved: true } })).toBe(true)
		await settle()
		expect(other.shadowRoot!.textContent).toContain('accepted')
		expect(toasts).toEqual([])

		host.ref.beginEdit()
		entity.title = 'refused'
		expect(await host.ref.commit(async () => undefined)).toBe(false)
		await settle()
		expect(entity.title).toBe('accepted')
		expect(other.shadowRoot!.textContent).toContain('accepted')
		expect(toasts).toHaveLength(1)
	})

	it('forks a draft of its entity, and none before it has resolved', async () => {
		const unresolved = await mount<EntityHost>('test-entity-host')
		expect(unresolved.ref.fork()).toBeUndefined()

		const host = await mount<EntityHost>('test-entity-host', (element) => { element.puck = 'O1' })
		const draft = host.ref.fork()!
		draft.value.title = 'drafted'
		expect(host.ref.value!.title).toBe('Objective O1')
		expect(draft.diff()).toEqual({ title: 'drafted' })
	})
})

describe('EntityWatch', () => {
	it('re-renders a host handed an entity when that entity changes elsewhere', async () => {
		const entity = (await core.repos.objectives.get('O1'))!
		const host = await mount<WatchHost>('test-watch-host', (element) => { element.entity = entity })
		const rendersBefore = host.renders

		entity.title = 'edited elsewhere'
		core.repos.objectives.touch('O1')
		await settle()

		expect(host.renders).toBe(rendersBefore + 1)
		expect(host.shadowRoot!.textContent).toContain('edited elsewhere')
	})

	it('publish announces an in-place edit the host made to the others', async () => {
		const entity = (await core.repos.objectives.get('O1'))!
		const host = await mount<WatchHost>('test-watch-host', (element) => { element.entity = entity })
		const other = await mount<WatchHost>('test-watch-host', (element) => { element.entity = entity })

		entity.title = 'published'
		host.watch.publish()
		await settle()

		expect(other.shadowRoot!.textContent).toContain('published')
	})

	it('reads as removed and marks the host once the entity is removed — also for a host mounted afterwards', async () => {
		const entity = (await core.repos.objectives.get('O1'))!
		const live = await mount<WatchHost>('test-watch-host', (element) => { element.entity = entity })
		expect(live.watch.removed).toBe(false)
		expect(live.hasAttribute('entity-removed')).toBe(false)

		delete shop.routes['/api/objectives/O1']
		await core.repos.objectives.refresh('O1')
		await settle()
		const late = await mount<WatchHost>('test-watch-host', (element) => { element.entity = entity })

		expect(live.watch.removed).toBe(true)
		expect(live.hasAttribute('entity-removed')).toBe(true)
		expect(late.watch.removed).toBe(true)
		expect(late.hasAttribute('entity-removed')).toBe(true)
	})
})

describe('QueryRef', () => {
	it('is a live, filtered, ordered slice of what the store holds', async () => {
		await core.repos.objectiveList.get()
		const host = await mount<QueryHost>('test-query-host')
		expect(host.query.items.map((o) => o.id)).toEqual(['O1'])

		const rendersBefore = host.renders
		shop.client.store.absorb(objective('O0', { status: 0 }))
		await settle()
		expect(host.renders).toBeGreaterThan(rendersBefore)
		expect(host.query.items.map((o) => o.id)).toEqual(['O0', 'O1'])

		// A member leaving the predicate drops out; one removed from the store drops out too.
		shop.client.store.peek<Objective>('Objective:O0')!.status = 3
		core.repos.objectives.touch('O0')
		shop.client.store.remove('Objective:O1')
		await settle()
		expect(host.query.items).toEqual([])
	})
})

describe('DerivedRef', () => {
	it('resolves its record, renders it, and re-renders when the record changes', async () => {
		const host = await mount<DerivedHost>('test-derived-host')
		expect(host.shadowRoot!.querySelectorAll('i')).toHaveLength(2)
		expect(host.listing.loading).toBe(false)
		expect(host.listing.notLoaded).toBe(false)

		shop.routes['/api/objectives'] = { body: [objective('O1')] }
		await host.listing.refresh()
		await settle()

		expect(host.shadowRoot!.querySelectorAll('i')).toHaveLength(1)
	})

	it('loses a member the moment its entity is removed, without a refetch', async () => {
		const host = await mount<DerivedHost>('test-derived-host')
		const requestsBefore = shop.requests.length

		shop.client.store.remove('Objective:O2')
		await settle()

		expect(host.shadowRoot!.querySelectorAll('i')).toHaveLength(1)
		expect(shop.requests).toHaveLength(requestsBefore)
	})

	it('reads as not loaded, and not loading, when the core could not be asked', async () => {
		shop.routes['/api/objectives'] = 'silence'
		const host = await mount<DerivedHost>('test-derived-host')

		expect(host.listing.value).toBeUndefined()
		expect(host.listing.loading).toBe(false)
		expect(host.listing.notLoaded).toBe(true)
	})
})
