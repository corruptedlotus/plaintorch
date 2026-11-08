import { describe, it, expect, vi, beforeAll, afterEach } from 'vitest'
import { ContextMenu, type ContextMenuSpec } from './ContextMenu'

// The shared context menu's submenus, driven the way a pointer, a finger and the keyboard drive them. happy-dom has no
// popover API, so a stand-in keeps each panel's state and fires the `beforetoggle` events the menu tracks it by.

const showing = new WeakSet<HTMLElement>()

beforeAll(() => {
	const toggle = (element: HTMLElement, newState: 'open' | 'closed') => {
		const event = new Event('beforetoggle')
		Object.defineProperty(event, 'newState', { value: newState })
		element.dispatchEvent(event)
	}

	HTMLElement.prototype.showPopover = function (this: HTMLElement) {
		if (!showing.has(this)) {
			showing.add(this)
			toggle(this, 'open')
		}
	}

	HTMLElement.prototype.hidePopover = function (this: HTMLElement) {
		if (showing.has(this)) {
			showing.delete(this)
			toggle(this, 'closed')
		}
	}
})

afterEach(() => {
	document.body.querySelectorAll('p7t-context-menu').forEach(menu => menu.remove())
})

/** Lets Lit updates and the menu's awaited steps settle. */
async function settle(): Promise<void> {
	for (let turn = 0; turn < 4; turn++) {
		await new Promise(resolve => setTimeout(resolve, 0))
	}
}

const wait = (ms: number) => new Promise(resolve => setTimeout(resolve, ms))

const panelOf = (menu: ContextMenu) => menu.shadowRoot!.querySelector<HTMLElement>('.menu')!
const rowsOf = (menu: ContextMenu) => Array.from(menu.shadowRoot!.querySelectorAll<HTMLButtonElement>('.menu > .menu-item'))
const submenuOf = (menu: ContextMenu) => menu.shadowRoot!.querySelector<ContextMenu>('p7t-context-menu')!
const isOpen = (menu: ContextMenu) => showing.has(panelOf(menu))

function deepActiveElement(): Element | null {
	let element = document.activeElement
	while (element?.shadowRoot?.activeElement) {
		element = element.shadowRoot.activeElement
	}

	return element
}

function press(row: HTMLElement, pointerType: string) {
	row.dispatchEvent(new PointerEvent('pointerdown', { pointerType, bubbles: true, composed: true }))
	row.dispatchEvent(new MouseEvent('click', { detail: 1, bubbles: true, composed: true }))
}

function key(key: string): KeyboardEvent {
	const event = new KeyboardEvent('keydown', { key, bubbles: true, composed: true, cancelable: true })
	deepActiveElement()!.dispatchEvent(event)
	return event
}

function spec(run = vi.fn()): ContextMenuSpec {
	return {
		title: 'A directive',
		entries: [
			{ label: 'Edit', run: () => { } },
			{
				label: 'Add',
				icon: 'lucide:plus',
				entries: [
					{ label: 'Objective', run },
					{ label: 'Fate', run: () => { } }
				]
			},
			{ separator: true },
			{ label: 'Delete', danger: true, run: () => { } }
		]
	}
}

describe('ContextMenu submenus', () => {
	it('draws a submenu row that announces its list, closed until opened', async () => {
		const menu = ContextMenu.open(10, 10, spec())
		await settle()

		const add = rowsOf(menu)[1]!
		expect(add.getAttribute('aria-haspopup')).toBe('menu')
		expect(add.getAttribute('aria-expanded')).toBe('false')
		expect(add.querySelector('.chevron')).not.toBeNull()
		expect(isOpen(submenuOf(menu))).toBe(false)
	})

	it('opens on a click and runs a nested row with the outer menu\'s origin, closing every level', async () => {
		const run = vi.fn()
		const menu = ContextMenu.open(40, 50, spec(run))
		await settle()

		const add = rowsOf(menu)[1]!
		press(add, 'mouse')
		await settle()
		const submenu = submenuOf(menu)
		expect(isOpen(submenu)).toBe(true)
		expect(add.getAttribute('aria-expanded')).toBe('true')
		expect(rowsOf(submenu).map(row => row.textContent?.trim())).toEqual(['Objective', 'Fate'])

		rowsOf(submenu)[0]!.click()
		await settle()
		expect(run).toHaveBeenCalledWith([40, 50])
		expect(menu.isConnected).toBe(false)
	})

	it('toggles on a tap, while a mouse click only ever opens it', async () => {
		const menu = ContextMenu.open(0, 0, spec())
		await settle()
		const add = rowsOf(menu)[1]!
		const submenu = submenuOf(menu)

		press(add, 'touch')
		await settle()
		expect(isOpen(submenu)).toBe(true)
		press(add, 'touch')
		await settle()
		expect(isOpen(submenu)).toBe(false)

		press(add, 'mouse')
		await settle()
		press(add, 'mouse')
		await settle()
		expect(isOpen(submenu)).toBe(true)
	})

	it('opens when a mouse rests on the row, and closes when it rests on another', async () => {
		const menu = ContextMenu.open(0, 0, spec())
		await settle()
		const [edit, add] = rowsOf(menu)
		const submenu = submenuOf(menu)

		add!.dispatchEvent(new PointerEvent('pointerenter', { pointerType: 'mouse' }))
		await wait(200)
		await settle()
		expect(isOpen(submenu)).toBe(true)

		// Crossing another row on the way into the submenu does not close it.
		edit!.dispatchEvent(new PointerEvent('pointerenter', { pointerType: 'mouse' }))
		panelOf(submenu).dispatchEvent(new PointerEvent('pointerenter', { pointerType: 'mouse' }))
		await wait(350)
		expect(isOpen(submenu)).toBe(true)

		edit!.dispatchEvent(new PointerEvent('pointerenter', { pointerType: 'mouse' }))
		await wait(350)
		await settle()
		expect(isOpen(submenu)).toBe(false)
		expect(menu.isConnected).toBe(true)
	})

	it('a touch passing over the row does not open it', async () => {
		const menu = ContextMenu.open(0, 0, spec())
		await settle()

		rowsOf(menu)[1]!.dispatchEvent(new PointerEvent('pointerenter', { pointerType: 'touch' }))
		await wait(200)
		expect(isOpen(submenuOf(menu))).toBe(false)
	})

	it('works from the keyboard: in on the first row, out to the row, one Escape per level', async () => {
		const menu = ContextMenu.open(0, 0, spec(), { keyboard: true })
		await settle()
		const [edit, add] = rowsOf(menu)
		const submenu = submenuOf(menu)
		expect(deepActiveElement()).toBe(edit)

		key('ArrowDown')
		expect(deepActiveElement()).toBe(add)

		key('ArrowRight')
		await settle()
		expect(isOpen(submenu)).toBe(true)
		expect(deepActiveElement()).toBe(rowsOf(submenu)[0])

		key('ArrowDown')
		expect(deepActiveElement()).toBe(rowsOf(submenu)[1])

		key('ArrowLeft')
		await settle()
		expect(isOpen(submenu)).toBe(false)
		expect(deepActiveElement()).toBe(add)

		// Enter and Space raise a click with no detail: the submenu opens on its first row.
		add!.dispatchEvent(new MouseEvent('click', { detail: 0, bubbles: true, composed: true }))
		await settle()
		expect(deepActiveElement()).toBe(rowsOf(submenu)[0])

		const escape = key('Escape')
		await settle()
		expect(escape.defaultPrevented).toBe(true)
		expect(isOpen(submenu)).toBe(false)
		expect(isOpen(menu)).toBe(true)
		expect(deepActiveElement()).toBe(add)
	})

	it('gives the focus back to where it was when it closes', async () => {
		const opener = document.createElement('button')
		document.body.appendChild(opener)
		opener.focus()

		const menu = ContextMenu.open(0, 0, spec(), { keyboard: true })
		await settle()
		expect(deepActiveElement()).not.toBe(opener)

		menu.dismiss()
		expect(menu.isConnected).toBe(false)
		expect(document.activeElement).toBe(opener)
		opener.remove()
	})

	it('draws an empty submenu disabled, with nothing to open', async () => {
		const menu = ContextMenu.open(0, 0, { entries: [{ label: 'Add', entries: [] }] })
		await settle()

		const add = rowsOf(menu)[0]!
		expect(add.disabled).toBe(true)
		expect(menu.shadowRoot!.querySelector('p7t-context-menu')).toBeNull()
	})
})
