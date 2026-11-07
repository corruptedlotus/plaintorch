import { describe, it, expect, vi, afterEach } from 'vitest'
import { Component, component, html } from '@a11d/lit'
import { DoubleClickController } from './DoubleClickController'
import { InteractionBubbleBase } from './InteractionBubbleBase'
import { EditablePart } from '../editing/EditableDataLink'

// The double-click a grid row opens its details with, on a host that holds one of each kind of thing a row holds. What
// is pinned here is where a double-click counts — open space only, both presses of it — and when it never does: on
// anything that answers a press of its own, and while anything in the host is being edited.

@component('test-double-click-host')
class DoubleClickHost extends Component {
	public opened = 0
	public readonly doubleClick = new DoubleClickController(this, () => this.opened++)

	protected override get template() {
		return html`
			<span class='space'>plain label</span>
			<button class='button'>Act</button>
			<input class='input'>
			<p7t-editable class='editable'><span class='inside'>value</span></p7t-editable>
			<p7t-editable class='titled' editButton><span class='inside'>title</span></p7t-editable>
			<p7t-editable class='inert' disabled><span class='inside'>inert</span></p7t-editable>
			<div class='handle' draggable='true'></div>
			<div class='notch' role='button'></div>
		`
	}
}

const hosts: DoubleClickHost[] = []

async function mount(): Promise<DoubleClickHost> {
	const host = document.createElement('test-double-click-host') as DoubleClickHost
	document.body.appendChild(host)
	hosts.push(host)
	await host.updateComplete
	return host
}

function part(host: DoubleClickHost, selector: string): HTMLElement {
	return host.shadowRoot!.querySelector<HTMLElement>(selector)!
}

const init = { bubbles: true, composed: true, cancelable: true }

/** One press of a mouse, as the platform reports it: the pointer event, then its mouse events, the click counted `detail`. */
function mousePress(target: Element, detail: number, button = 0): MouseEvent {
	target.dispatchEvent(new PointerEvent('pointerdown', { ...init, pointerType: 'mouse', button }))
	const down = new MouseEvent('mousedown', { ...init, detail, button })
	target.dispatchEvent(down)
	target.dispatchEvent(new MouseEvent('mouseup', { ...init, detail, button }))
	target.dispatchEvent(new MouseEvent('click', { ...init, detail, button }))
	return down
}

/** A mouse double-click: two presses on `first` and `second`, then the platform's `dblclick` on the second's target. */
function mouseDoubleClick(first: Element, second: Element = first, button = 0) {
	mousePress(first, 1, button)
	const secondDown = mousePress(second, 2, button)
	const dblclick = new MouseEvent('dblclick', { ...init, detail: 2, button })
	second.dispatchEvent(dblclick)
	return { secondDown, dblclick }
}

/** One tap of a finger: its pointer event, the compatibility mouse events, and the click. */
function tap(target: Element, x = 10, y = 10) {
	target.dispatchEvent(new PointerEvent('pointerdown', { ...init, pointerType: 'touch', button: 0, clientX: x, clientY: y }))
	target.dispatchEvent(new MouseEvent('mousedown', { ...init, detail: 1, clientX: x, clientY: y }))
	target.dispatchEvent(new MouseEvent('click', { ...init, detail: 1, clientX: x, clientY: y }))
}

/** Holds an editable in its edit until the returned function settles it. */
function holdEditing(field: EditablePart<unknown>): () => Promise<void> {
	let settle!: () => void
	field.doEdit = () => new Promise(resolve => settle = () => resolve(undefined))
	field.startEditing()
	return async () => {
		settle()
		await Promise.resolve()
		await Promise.resolve()
	}
}

afterEach(() => {
	vi.restoreAllMocks()
	hosts.splice(0).forEach(host => host.remove())
})

describe('DoubleClickController', () => {
	describe('with a mouse', () => {
		it('acts on a double-click on open space, keeping the second press from selecting', async () => {
			const host = await mount()
			const { secondDown, dblclick } = mouseDoubleClick(part(host, '.space'))
			expect(host.opened).toBe(1)
			expect(secondDown.defaultPrevented).toBe(true)
			expect(dblclick.defaultPrevented).toBe(true)
		})

		it('acts on the host itself, between its parts', async () => {
			const host = await mount()
			mouseDoubleClick(host)
			expect(host.opened).toBe(1)
		})

		it('never acts on a control, a field, a drag handle or an element with an interactive role', async () => {
			const host = await mount()
			for (const selector of ['.button', '.input', '.handle', '.notch']) {
				const { secondDown } = mouseDoubleClick(part(host, selector))
				expect(secondDown.defaultPrevented, selector).toBe(false)
			}

			expect(host.opened).toBe(0)
		})

		it('never acts on an editable a press would start editing, inside it or on it', async () => {
			const host = await mount()
			const editable = part(host, '.editable') as EditablePart<unknown>
			mouseDoubleClick(editable.querySelector('.inside')!)
			mouseDoubleClick(editable)
			expect(host.opened).toBe(0)
		})

		it('acts on an editable idle behind its edit button, and on an inert one', async () => {
			const host = await mount()
			mouseDoubleClick(part(host, '.titled').querySelector('.inside')!)
			mouseDoubleClick(part(host, '.inert'))
			expect(host.opened).toBe(2)
		})

		it('needs both presses on open space', async () => {
			const host = await mount()
			mouseDoubleClick(part(host, '.button'), part(host, '.space'))
			mouseDoubleClick(part(host, '.space'), part(host, '.button'))
			expect(host.opened).toBe(0)
		})

		it('does not pair a second press with a first that landed outside the host', async () => {
			const host = await mount()
			const other = await mount()
			mouseDoubleClick(part(other, '.space'), part(host, '.space'))
			expect(host.opened).toBe(0)
			expect(other.opened).toBe(0)
		})

		it('ignores the other buttons', async () => {
			const host = await mount()
			mouseDoubleClick(part(host, '.space'), part(host, '.space'), 2)
			expect(host.opened).toBe(0)
		})

		it('leaves a double-click an inner element handled to it', async () => {
			const host = await mount()
			const space = part(host, '.space')
			space.addEventListener('dblclick', event => event.preventDefault())
			mouseDoubleClick(space)
			expect(host.opened).toBe(0)
		})
	})

	describe('mid-edit', () => {
		it('never acts while an editable in the host holds its edit, and acts again once it ends', async () => {
			const host = await mount()
			const finish = holdEditing(part(host, '.editable') as EditablePart<unknown>)
			mouseDoubleClick(part(host, '.space'))
			expect(host.opened).toBe(0)

			await finish()
			mouseDoubleClick(part(host, '.space'))
			expect(host.opened).toBe(1)
		})

		it('never acts when the first press is the one that ends the edit', async () => {
			const host = await mount()
			const field = part(host, '.editable') as EditablePart<unknown>
			holdEditing(field)
			// The edit ends at once as the first press is heard, the way a blur commits a text field.
			window.addEventListener('pointerdown', () => (field as unknown as { active: boolean }).active = false, { once: true })
			mouseDoubleClick(part(host, '.space'))
			expect(field.editing).toBe(false)
			expect(host.opened).toBe(0)
		})

		it('is not held back by an edit in another host', async () => {
			const host = await mount()
			const other = await mount()
			holdEditing(part(other, '.editable') as EditablePart<unknown>)
			mouseDoubleClick(part(host, '.space'))
			expect(host.opened).toBe(1)
		})

		it('never acts while a field in the host has the focus', async () => {
			const host = await mount()
			part(host, '.input').focus()
			mouseDoubleClick(part(host, '.space'))
			expect(host.opened).toBe(0)
		})

		it('never acts while a bubble raised from inside the host is open', async () => {
			const host = await mount()
			vi.spyOn(InteractionBubbleBase, 'isRaisedFrom').mockImplementation(container => container === host)
			mouseDoubleClick(part(host, '.space'))
			expect(host.opened).toBe(0)
		})
	})

	describe('by touch', () => {
		it('acts on two quick taps on open space, and not again on the dblclick that may follow', async () => {
			const host = await mount()
			const space = part(host, '.space')
			tap(space)
			tap(space, 14, 12)
			space.dispatchEvent(new MouseEvent('dblclick', { ...init, detail: 2 }))
			expect(host.opened).toBe(1)
		})

		it('lets slow or distant taps pass', async () => {
			const host = await mount()
			const space = part(host, '.space')
			const now = vi.spyOn(performance, 'now')
			now.mockReturnValue(1000)
			tap(space)
			now.mockReturnValue(1600)
			tap(space)
			now.mockReturnValue(1700)
			tap(space, 80, 10)
			expect(host.opened).toBe(0)
		})

		it('never pairs a tap with one on a control', async () => {
			const host = await mount()
			tap(part(host, '.button'))
			tap(part(host, '.space'))
			tap(part(host, '.button'))
			expect(host.opened).toBe(0)
		})
	})
})
