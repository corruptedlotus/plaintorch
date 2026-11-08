import { describe, it, expect, afterEach } from 'vitest'
import { InteractionBubbleBase } from '../design/InteractionBubbleBase'
// For its side effect, `p7t-editable`'s registration: the class itself is used here only as a type.
import './EditableDataLink'
import type { EditablePart } from './EditableDataLink'

// A `p7t-editable` that edits through a bubble (the state chip): a second press of the field closes the bubble it raised,
// as a second press of any bubble's trigger does, rather than being refused because an edit is under way.

/** Stands in for the bubble a prompt shows: the one `InteractionBubbleBase` holds as showing, anchored where it was raised. */
function showBubble(anchor: HTMLElement, onDismiss: () => void) {
	const bubble = {
		anchor,
		dismissed: 0,
		dismiss() {
			this.dismissed++
			setShowing(undefined)
			onDismiss()
		}
	}
	setShowing(bubble)
	return bubble
}

function setShowing(bubble: unknown) {
	(InteractionBubbleBase as unknown as { current?: unknown }).current = bubble
}

/** A field whose edit is a bubble prompt: each edit raises one at the field, and a dismissal cancels the edit. */
async function mountBubbleField() {
	const field = document.createElement('p7t-editable') as EditablePart<number>
	field.value = 1
	const prompts: ReturnType<typeof showBubble>[] = []
	field.doEdit = (_value, anchor) => new Promise((_resolve, reject) => {
		prompts.push(showBubble(anchor, () => reject(new Error('dismissed'))))
	})
	document.body.appendChild(field)
	await field.updateComplete
	return { field, prompts }
}

async function settle() {
	for (let turn = 0; turn < 3; turn++) {
		await Promise.resolve()
	}
}

const click = (target: Element) => target.dispatchEvent(new MouseEvent('click', { bubbles: true, composed: true }))

afterEach(() => {
	setShowing(undefined)
	document.body.replaceChildren()
})

describe('EditablePart with a bubble edit', () => {
	it('opens its bubble on a press, and closes it on a second press', async () => {
		const { field, prompts } = await mountBubbleField()
		click(field)
		expect(field.editing).toBe(true)
		expect(prompts).toHaveLength(1)

		click(field)
		await settle()
		expect(prompts).toHaveLength(1)
		expect(prompts[0]!.dismissed).toBe(1)
		expect(field.editing).toBe(false)
		expect(field.value).toBe(1)
	})

	it('opens it again on a third press', async () => {
		const { field, prompts } = await mountBubbleField()
		click(field)
		click(field)
		await settle()
		click(field)
		expect(field.editing).toBe(true)
		expect(prompts).toHaveLength(2)
	})

	it('leaves a bubble raised elsewhere alone', async () => {
		const { field, prompts } = await mountBubbleField()
		click(field)
		const elsewhere = showBubble(document.body.appendChild(document.createElement('div')), () => undefined)
		click(field)
		await settle()
		expect(elsewhere.dismissed).toBe(0)
		expect(prompts[0]!.dismissed).toBe(0)
		expect(field.editing).toBe(true)
	})
})

describe('InteractionBubbleBase.dismissRaisedFrom', () => {
	it('dismisses only a bubble whose anchor lies within the container, through shadow roots', () => {
		const host = document.body.appendChild(document.createElement('div'))
		const root = host.attachShadow({ mode: 'open' })
		const anchor = root.appendChild(document.createElement('span'))
		const bubble = showBubble(anchor, () => undefined)

		expect(InteractionBubbleBase.dismissRaisedFrom(document.body.appendChild(document.createElement('div')))).toBe(false)
		expect(bubble.dismissed).toBe(0)
		expect(InteractionBubbleBase.dismissRaisedFrom(host)).toBe(true)
		expect(bubble.dismissed).toBe(1)
		expect(InteractionBubbleBase.dismissRaisedFrom(host)).toBe(false)
	})
})
