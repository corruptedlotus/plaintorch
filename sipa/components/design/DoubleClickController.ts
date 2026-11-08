import { Controller, type ReactiveElement } from '@a11d/lit'
import { EditablePart } from '../editing/EditableDataLink'
import { InteractionBubbleBase } from './InteractionBubbleBase'

/**
 * How close in time two taps must land to count as a double tap: the window the dependency canvas times its own
 * double-click with. A mouse's double-click is the platform's, timed by the system's setting.
 */
const doubleTapWindow = 450

/** How far apart two taps may land and still be one double tap; a finger lands less precisely than a pointer. */
const doubleTapSlop = 24

/**
 * What answers a press by its nature: a link, a form control, an editable region, a drag handle, anything in the tab
 * order, and anything that claims an interactive role.
 */
const interactiveSelector = [
	'a[href]', 'button', 'input', 'select', 'textarea', 'label', 'summary',
	'[contenteditable]:not([contenteditable="false"])',
	'[draggable="true"]',
	'[tabindex]:not([tabindex="-1"])',
	...['button', 'link', 'checkbox', 'radio', 'switch', 'menuitem', 'option', 'tab', 'textbox', 'combobox', 'slider', 'spinbutton']
		.map(role => `[role="${role}"]`)
].join(', ')

/** The fields that take typed text when focused. */
const textEntrySelector = 'textarea, select, input:not([type="button"], [type="submit"], [type="reset"], [type="checkbox"], [type="radio"], [type="image"], [type="file"], [type="range"], [type="color"])'

/** The cursors plain content shows, which promise nothing. */
const plainCursors = new Set(['', 'auto', 'default'])

/** What a double-click on the host's open space does. Handed the event that completed it: a `dblclick`, or a tap's `click`. */
export type DoubleClickAction = (event: MouseEvent) => void

/**
 * Binds a double-click on a host's open space to an action (component-controller model, akin to
 * {@link ContextMenuController}): a grid row opens its entity's details this way.
 *
 * **Open space** is wherever nothing between the press and the host answers a press of its own: not a link, a button
 * or another form control, not a drag handle, nothing focusable or with an interactive role, no editable field that a
 * press would start editing (one idle behind its edit button leaves its content free, so it counts as open), and nothing
 * whose cursor promises an interaction the host's own does not — the catch-all for a widget that listens for clicks but
 * says so only by its pointer. Both presses of the pair must land there.
 *
 * **Never mid-edit.** A press made while anything in the host is being edited — a field holding the caret or showing its
 * input, a bubble raised from inside the host — is that edit's: it commits or dismisses, and starts no double-click.
 * That is read the moment the press happens, capturing on the window ahead of every other listener, since by the time
 * the press reaches the host the field has already lost the focus and the bubble's light dismiss has closed it.
 *
 * **No selection.** The second press of a pair on open space has its default prevented, which keeps the platform from
 * selecting the word (or, on a third press, the line) under it. A first press is left alone: it is what moves the focus
 * and ends an edit elsewhere.
 *
 * **Mouse and pen** go by the platform's own `dblclick`, timed by the system's double-click setting. The dependency
 * canvas times clicks itself because its first click brings a node forward and changes what the second lands on; a
 * press on open space changes nothing, so the platform's event arrives intact. **Touch** has no reliable `dblclick` —
 * a web view fires one for a double tap only where double-tap zoom is off, if at all — so two taps on open space are
 * timed here ({@link doubleTapWindow}, {@link doubleTapSlop}) and a `dblclick` that follows a tap is ignored. A host
 * that takes double taps best sets `touch-action: manipulation`, so the platform does not zoom on them instead. Neither
 * collides with a long press (the context menu) or a press-and-hold (a lift for dragging), which never make a click.
 *
 * An inner element that handles a double-click itself prevents its default, and the controller leaves that one alone.
 */
export class DoubleClickController extends Controller {
	/** The controllers of connected hosts, by host, so the window's press listener finds those a press lands inside. */
	private static readonly byHost = new WeakMap<EventTarget, DoubleClickController>()
	/** The controllers the previous press landed inside, to tell the second press of a pair from a fresh first one. */
	private static previous = new Set<DoubleClickController>()
	private static listening = false

	/** Whether the latest press on the host landed on open space while nothing in the host was being edited. */
	private pressClear = false
	/** Whether the press before the latest one landed inside the host too. */
	private pressContinues = false
	/** The kind of pointer that made the latest press. */
	private pressPointer = ''
	/** Whether every press of the mouse's current click sequence was clear. */
	private sequenceClear = false
	/** The latest clear tap, waiting for a second. */
	private lastTap?: { readonly at: number, readonly x: number, readonly y: number }

	public constructor(host: ReactiveElement, private readonly action: DoubleClickAction) {
		super(host)
	}

	private get element(): HTMLElement {
		return this.host as unknown as HTMLElement
	}

	// Listens as itself (`handleEvent`) rather than through bound fields: Lit may connect a controller while it is still
	// constructing, before any field initialiser has run.
	public override hostConnected(): void {
		DoubleClickController.listen()
		DoubleClickController.byHost.set(this.element, this)
		this.element.addEventListener('mousedown', this, true)
		this.element.addEventListener('dblclick', this)
		this.element.addEventListener('click', this)
	}

	public override hostDisconnected(): void {
		DoubleClickController.byHost.delete(this.element)
		this.element.removeEventListener('mousedown', this, true)
		this.element.removeEventListener('dblclick', this)
		this.element.removeEventListener('click', this)
		this.lastTap = undefined
		this.sequenceClear = false
	}

	public handleEvent(event: Event): void {
		switch (event.type) {
			case 'mousedown': return this.onMouseDown(event as MouseEvent)
			case 'dblclick': return this.onDoubleClick(event as MouseEvent)
			case 'click': return this.onClick(event as MouseEvent)
		}
	}

	/**
	 * The press listener, once for every host: capturing on the window, it hears a press before anything else does, so
	 * each host the press lands inside is read as the press found it.
	 */
	private static listen(): void {
		if (DoubleClickController.listening) {
			return
		}

		DoubleClickController.listening = true
		window.addEventListener('pointerdown', event => {
			const pressed = new Set<DoubleClickController>()
			for (const target of event.composedPath()) {
				const controller = DoubleClickController.byHost.get(target)
				if (controller) {
					controller.press(event, DoubleClickController.previous.has(controller))
					pressed.add(controller)
				}
			}

			DoubleClickController.previous = pressed
		}, true)
	}

	private press(event: PointerEvent, continues: boolean): void {
		this.pressPointer = event.pointerType
		this.pressContinues = continues
		this.pressClear = event.button === 0 && this.isOpenSpace(event.composedPath()) && !this.isBusy()
		if (!this.pressClear) {
			this.lastTap = undefined
		}
	}

	private onMouseDown(event: MouseEvent): void {
		// A tap's own compatibility event: taps are timed in onClick.
		if (this.pressPointer === 'touch') {
			return
		}

		const clear = this.pressClear && event.button === 0
		this.sequenceClear = event.detail <= 1 ? clear : this.sequenceClear && this.pressContinues && clear
		if (event.detail >= 2 && this.sequenceClear) {
			event.preventDefault()
		}
	}

	private onDoubleClick(event: MouseEvent): void {
		if (this.pressPointer === 'touch' || event.defaultPrevented || event.button !== 0) {
			return
		}

		const clear = this.sequenceClear && this.isOpenSpace(event.composedPath())
		this.sequenceClear = false
		if (clear) {
			this.activate(event)
		}
	}

	private onClick(event: MouseEvent): void {
		if (this.pressPointer !== 'touch') {
			return
		}

		if (event.defaultPrevented || !this.pressClear) {
			this.lastTap = undefined
			return
		}

		const tap = { at: performance.now(), x: event.clientX, y: event.clientY }
		const last = this.lastTap
		if (last && this.pressContinues && tap.at - last.at <= doubleTapWindow && Math.hypot(tap.x - last.x, tap.y - last.y) <= doubleTapSlop) {
			this.lastTap = undefined
			this.activate(event)
			return
		}

		this.lastTap = tap
	}

	/** The gesture is the host's now: nothing around it should read it as its own double-click too. */
	private activate(event: MouseEvent): void {
		event.preventDefault()
		event.stopPropagation()
		this.action(event)
	}

	/** Whether a press along `path` landed on the host's open space (see the class description). */
	private isOpenSpace(path: readonly EventTarget[]): boolean {
		const index = path.indexOf(this.element)
		if (index < 0) {
			return false
		}

		for (const target of path.slice(0, index)) {
			if (!(target instanceof Element)) {
				continue
			}

			if (target instanceof EditablePart) {
				if (target.answersPress) {
					return false
				}

				continue
			}

			if (target.matches(interactiveSelector) || (target instanceof HTMLElement && target.isContentEditable)) {
				return false
			}
		}

		return !promisesInteraction(path[0], this.element)
	}

	/**
	 * Whether anything in the host is being edited: a bubble raised from inside it, a focused field taking text, or an
	 * editable holding its edit.
	 */
	private isBusy(): boolean {
		if (InteractionBubbleBase.isRaisedFrom(this.element)) {
			return true
		}

		const focused = deepActiveElement()
		if (focused && composedContains(this.element, focused) && (focused.isContentEditable || focused.matches(textEntrySelector))) {
			return true
		}

		const editing = (element: Element) => element instanceof EditablePart && element.editing
		return someDeep(this.element, editing) || (!!this.element.shadowRoot && someDeep(this.element.shadowRoot, editing))
	}
}

/** Whether the cursor over `target` promises something the host's own does not: a pointer, a caret, a grab. */
function promisesInteraction(target: EventTarget | undefined, host: Element): boolean {
	if (!(target instanceof Element)) {
		return false
	}

	const cursor = getComputedStyle(target).cursor
	return !plainCursors.has(cursor) && cursor !== getComputedStyle(host).cursor
}

/** Whether an element under `root`, or under any open shadow root within it, passes `test`. */
function someDeep(root: ParentNode, test: (element: Element) => boolean): boolean {
	for (const element of Array.from(root.querySelectorAll('*'))) {
		if (test(element) || (!!element.shadowRoot && someDeep(element.shadowRoot, test))) {
			return true
		}
	}

	return false
}

/** Whether `node` is `container` or lies within it, through shadow roots. */
function composedContains(container: Node, node: Node): boolean {
	for (let current: Node | null = node; current; current = current instanceof ShadowRoot ? current.host : current.parentNode) {
		if (current === container) {
			return true
		}
	}

	return false
}

/** The focused element, looked for through open shadow roots. */
function deepActiveElement(): HTMLElement | undefined {
	let active = document.activeElement
	while (active?.shadowRoot?.activeElement) {
		active = active.shadowRoot.activeElement
	}

	return active instanceof HTMLElement ? active : undefined
}
