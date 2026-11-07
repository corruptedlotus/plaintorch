import { describe, it, expect, vi, beforeAll, afterEach } from 'vitest'
// The components barrel first, in its own order: the grid's modules reach each other through it, and entering at
// `GridBase` would have the barrel define `EntityGrid` before the base class it extends.
import '..'
import { component, html, type HTMLTemplateResult } from '@a11d/lit'
import type { DataRecord } from '@3mo/data-grid/controller'
import type { EntityTypeName } from '@pleiades/sdk'
import { PlaintorchCoreClient, type PlaintorchCoreRequest, type PlaintorchCoreResponse, type PlaintorchCoreTransport } from '@pleiades/sdk/plaintorch'
import { provideHost, type PlatformHost } from '../../host'
import { provideCore } from '../data/coreProvider'
import type { IconName } from '../PleiadesIcon'
import { GridBase } from './GridBase'
import { GridItemBase } from './GridItemBase'
import type { GridNode, GridRow } from './entityTree'

// Reaching a grid's rows by keyboard: one row in the tab order, the arrows moving the focus between rows the way a tree
// does, and Enter opening a row's details — only while a row itself has the focus, never from a field inside it.

@component('test-keyboard-row')
class KeyboardRow extends GridItemBase {
	public opened = 0

	protected override get kindIcon(): IconName {
		return 'lucide:circle' as IconName
	}

	protected override async persistField(): Promise<void> { }

	protected override openDetails() {
		this.opened++
	}
}

const entity = (id: string) => ({ id, title: `Entity ${id}` })
const leaf = (id: string, guides: string[]): GridNode => ({ id, entity: entity(id), kind: 'item', guides })

/** A parent with two children, a leaf, then a heading that only groups (no entity) over one more leaf. */
const tree: GridNode[] = [
	{ id: 'p', entity: entity('p'), kind: 'item', guides: [], children: [leaf('c1', ['item']), leaf('c2', ['item'])] },
	leaf('l', []),
	{ id: 'h', entity: null!, kind: 'root', guides: [], collapsible: false, children: [leaf('w', ['world'])] }
]

@component('test-keyboard-grid')
class KeyboardGrid extends GridBase {
	protected override readonly columns = [GridBase.titleColumnDefinition()]

	protected override get observedKinds(): readonly EntityTypeName[] {
		return []
	}

	protected override buildTree(): GridNode[] {
		return tree
	}

	protected override get loading() {
		return false
	}

	protected override renderRow(row: GridRow, record: DataRecord<GridNode>): HTMLTemplateResult {
		return html`<test-keyboard-row .row=${row} ${this.grid.row(record)}></test-keyboard-row>`
	}
}

beforeAll(() => {
	provideHost({
		toast: () => undefined,
		media: { resourceUrl: () => undefined, isResourceUrl: () => false },
		icons: { lucide: () => undefined, lucideNames: () => [] }
	} as unknown as PlatformHost)
	const transport: PlaintorchCoreTransport = {
		async send(_request: PlaintorchCoreRequest): Promise<PlaintorchCoreResponse | undefined> {
			return { ok: false, status: 404, text: async () => '', header: () => undefined }
		}
	}
	provideCore(new PlaintorchCoreClient({ transports: [transport] }))
})

afterEach(() => {
	vi.restoreAllMocks()
	document.body.replaceChildren()
})

async function settle(grid: KeyboardGrid) {
	for (let turn = 0; turn < 4; turn++) {
		await grid.updateComplete
		await Promise.all(rowsOf(grid).map(row => row.updateComplete))
		await new Promise(resolve => setTimeout(resolve, 0))
	}
}

async function mount() {
	const grid = document.createElement('test-keyboard-grid') as KeyboardGrid
	document.body.appendChild(grid)
	await settle(grid)
	return grid
}

const rowsOf = (grid: KeyboardGrid) => Array.from(grid.shadowRoot!.querySelectorAll<KeyboardRow>('test-keyboard-row'))
const rowOf = (grid: KeyboardGrid, key: string) => rowsOf(grid).find(row => row.row?.key === key)!
const keysOf = (grid: KeyboardGrid) => rowsOf(grid).map(row => row.row?.key)
const focused = (grid: KeyboardGrid) => (grid.shadowRoot!.activeElement as KeyboardRow | null)?.row?.key

function press(target: Element, key: string, init: KeyboardEventInit = {}) {
	const event = new KeyboardEvent('keydown', { key, bubbles: true, composed: true, cancelable: true, ...init })
	target.dispatchEvent(event)
	return event
}

/** Presses `key` on whichever row has the focus now. */
const pressFocused = (grid: KeyboardGrid, key: string) => press(rowOf(grid, focused(grid)!), key)

describe('GridBase keyboard', () => {
	it('puts one row in the tab order, and passes over a heading without an entity', async () => {
		const grid = await mount()
		expect(keysOf(grid)).toEqual(['p', 'l', 'h', 'w'])
		expect(rowsOf(grid).map(row => row.getAttribute('tabindex'))).toEqual(['0', '-1', null, '-1'])
	})

	it('moves the tab stop to the row last focused or pressed', async () => {
		const grid = await mount()
		rowOf(grid, 'l').focus()
		expect(rowOf(grid, 'l').tabIndex).toBe(0)
		expect(rowOf(grid, 'p').tabIndex).toBe(-1)

		// Blurred first: happy-dom's `ShadowRoot.activeElement` throws, where a browser answers null, when asked from inside a
		// focused element's own shadow root, which the press's double-click check does.
		rowOf(grid, 'l').blur()
		rowOf(grid, 'w').dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, composed: true }))
		expect(rowOf(grid, 'w').tabIndex).toBe(0)
		expect(rowOf(grid, 'l').tabIndex).toBe(-1)
	})

	it('steps between rows with the vertical arrows, Home and End, over the heading', async () => {
		const grid = await mount()
		rowOf(grid, 'p').focus()
		expect(pressFocused(grid, 'ArrowDown').defaultPrevented).toBe(true)
		expect(focused(grid)).toBe('l')
		pressFocused(grid, 'ArrowDown')
		expect(focused(grid)).toBe('w')
		expect(pressFocused(grid, 'ArrowDown').defaultPrevented).toBe(true)
		expect(focused(grid)).toBe('w')
		pressFocused(grid, 'ArrowUp')
		expect(focused(grid)).toBe('l')
		pressFocused(grid, 'End')
		expect(focused(grid)).toBe('w')
		pressFocused(grid, 'Home')
		expect(focused(grid)).toBe('p')
	})

	// Under Vitest, lit loads as server code (`isServer`), and `@a11d/lit` then hands every `@event` an inert dispatcher:
	// a row's requests to open or close never reach the grid here. So they are watched on the row's dispatchers, and the
	// grid opens and closes through its own API in their place.
	it('asks to open a closed row with the right arrow, and to close an open one with the left', async () => {
		const grid = await mount()
		const parent = rowOf(grid, 'p')
		const expand = vi.spyOn(parent.requestRowExpand, 'dispatch')
		const toggle = vi.spyOn(parent.requestRowToggle, 'dispatch')
		parent.focus()
		expect(pressFocused(grid, 'ArrowRight').defaultPrevented).toBe(true)
		expect(expand).toHaveBeenCalledWith('p')

		grid.toggleRow('p')
		await settle(grid)
		expect(parent.row?.expanded).toBe(true)
		expect(pressFocused(grid, 'ArrowLeft').defaultPrevented).toBe(true)
		expect(toggle).toHaveBeenCalledWith('p')
		expect(focused(grid)).toBe('p')
	})

	it('steps from an open row to its first child with the right arrow, and back to the parent with the left', async () => {
		const grid = await mount()
		grid.toggleRow('p')
		await settle(grid)
		expect(keysOf(grid)).toEqual(['p', 'c1', 'c2', 'l', 'h', 'w'])

		rowOf(grid, 'p').focus()
		pressFocused(grid, 'ArrowRight')
		expect(focused(grid)).toBe('c1')
		pressFocused(grid, 'ArrowDown')
		expect(focused(grid)).toBe('c2')
		pressFocused(grid, 'ArrowLeft')
		expect(focused(grid)).toBe('p')

		pressFocused(grid, 'ArrowDown')
		pressFocused(grid, 'ArrowRight')
		expect(focused(grid)).toBe('c1')
	})

	it('does nothing with the right arrow on a leaf', async () => {
		const grid = await mount()
		const leafRow = rowOf(grid, 'l')
		const expand = vi.spyOn(leafRow.requestRowExpand, 'dispatch')
		leafRow.focus()
		pressFocused(grid, 'ArrowRight')
		expect(expand).not.toHaveBeenCalled()
		expect(focused(grid)).toBe('l')
	})

	it('does not step from a row under the heading to the heading', async () => {
		const grid = await mount()
		rowOf(grid, 'w').focus()
		pressFocused(grid, 'ArrowLeft')
		expect(focused(grid)).toBe('w')
	})

	it('opens the details of the focused row on Enter, once however long it is held', async () => {
		const grid = await mount()
		rowOf(grid, 'l').focus()
		expect(pressFocused(grid, 'Enter').defaultPrevented).toBe(true)
		pressFocused(grid, 'Enter')
		press(rowOf(grid, 'l'), 'Enter', { repeat: true })
		expect(rowOf(grid, 'l').opened).toBe(2)
	})

	it('leaves the keys pressed inside a row to what is inside it', async () => {
		const grid = await mount()
		const row = rowOf(grid, 'p')
		row.focus()
		const title = row.shadowRoot!.querySelector('p7t-editable-plaintext')!
		press(title, 'Enter')
		expect(press(title, 'ArrowDown').defaultPrevented).toBe(false)
		const expand = vi.spyOn(row.requestRowExpand, 'dispatch')
		expect(press(title, 'ArrowRight').defaultPrevented).toBe(false)
		expect(row.opened).toBe(0)
		expect(expand).not.toHaveBeenCalled()
		expect(focused(grid)).toBe('p')
	})

	it('leaves modified keys alone', async () => {
		const grid = await mount()
		rowOf(grid, 'p').focus()
		expect(press(rowOf(grid, 'p'), 'ArrowDown', { shiftKey: true }).defaultPrevented).toBe(false)
		expect(press(rowOf(grid, 'p'), 'Enter', { ctrlKey: true }).defaultPrevented).toBe(false)
		expect(focused(grid)).toBe('p')
		expect(rowOf(grid, 'p').opened).toBe(0)
	})
})
