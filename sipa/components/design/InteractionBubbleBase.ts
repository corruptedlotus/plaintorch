import { css, event, html, nothing, property, type HTMLTemplateResult, type PropertyValues } from '@a11d/lit'
import { Popover as MoPopover, PopoverAlignment, PopoverFloatingUiPositionController, PopoverPlacement, type PopoverCoordinates } from '@3mo/popover'
import { PopoverHost } from '@3mo/popover/dist/PopoverHost'
import type { IconName } from '../PleiadesIcon'
// Evaluated first for its side effect: it forces @3mo/popover onto its Floating UI position controller, which the
// bubble's viewport clamp plugs into and whose script-set placement the bubble's styles rely on.
import './Tooltip'

/** What a bubble points at: an element, a viewport point, or the event that raised it (see {@link InteractionBubbleBase.prompt}). */
export type InteractionBubbleAnchor = HTMLElement | PopoverCoordinates | Event

/** What every interaction bubble can be told. A bubble's own options extend these. */
export interface InteractionBubbleOptions<TData> {
	/** The side of the anchor the bubble prefers; it flips when that side lacks room. Falls back to above. */
	readonly placement?: PopoverPlacement
	/**
	 * Runs with the committed data once the bubble is committed, after it has gone. The prompt's promise waits for it,
	 * and rejects when it throws. Never run when the bubble is dismissed.
	 */
	readonly run?: (data: TData) => unknown
}

/** How a bubble closed: whether it was committed, and the data it closed with. */
export interface InteractionBubbleSettlement<TData> {
	readonly committed: boolean
	readonly data: TData
}

/** A button of a bubble's closing row (see {@link InteractionBubbleBase.actionsTemplate}). */
export interface InteractionBubbleAction {
	/** Falls back to "Confirm" for the committing button, "Cancel" for the dismissing one. */
	readonly label?: string
	readonly icon?: IconName
	/** Takes the focus when the bubble opens. */
	readonly autofocus?: boolean
}

/** The data type a bubble class works on. */
export type InteractionBubbleDataOf<TBubble> = TBubble extends InteractionBubbleBase<infer TData, any> ? TData : never

/** The options type a bubble class takes. */
export type InteractionBubbleOptionsOf<TBubble> = TBubble extends InteractionBubbleBase<any, infer TOptions> ? TOptions : never

const placements: readonly PopoverPlacement[] = Object.values(PopoverPlacement)

const fallbackCommit = 'Confirm'
const fallbackDismiss = 'Cancel'

/** Space kept between the bubble and the viewport's edges. */
const viewportMargin = 8

type FloatingMiddleware = Parameters<PopoverFloatingUiPositionController['addMiddleware']>[0]

/**
 * Keeps the bubble inside the viewport along its anchor. Floating UI's `flip` only moves it to the anchor's other side,
 * so a bubble centred on an anchor near the viewport's side would still overflow it; `mo-tooltip` adds Floating UI's
 * `shift` for the same reason, and this is its dependency-free equivalent. Running before `arrow`, it leaves the arrow
 * pointing at the anchor however far the bubble slid.
 */
const viewportClamp: FloatingMiddleware = {
	name: 'p7t-viewport-clamp',
	fn: ({ x, y, placement, rects }) => {
		const clamp = (value: number, room: number) => Math.max(viewportMargin, Math.min(value, room - viewportMargin))
		return placement.startsWith('top') || placement.startsWith('bottom')
			? { x: clamp(x, window.innerWidth - rects.floating.width) }
			: { y: clamp(y, window.innerHeight - rects.floating.height) }
	},
}

/**
 * A small interactive bubble pointing at what raised it, that hands back data: a confirmation, a quick edit, a choice.
 * A subclass draws the body ({@link content}) and hands its blank data to the constructor; everything else is here —
 * raising it, placing it, closing it, focus, and the promise of its outcome.
 *
 * Raise one through {@link prompt}, on the subclass: it mounts a transient bubble with the options and the starting
 * data, and resolves with the outcome. Committed (by {@link commit}), that is the bubble's data at that moment, after
 * the options' `run` has had it; dismissed — by Escape, a press elsewhere, a second press of the anchor, the anchor
 * leaving the screen, or {@link dismiss} — it is the starting data, unchanged. An edit bubble thereby answers with the
 * value as it stands after the interaction, and a confirmation with whether it was given. Set up by hand instead — an
 * `anchor`, `options`, `data`, then `open` — a bubble reports through its `settle` event.
 *
 * It is @3mo's `mo-popover` re-skinned, the machinery the tooltips and select lists already run on: a native
 * Popover-API overlay in the top layer (never clipped by an ancestor's overflow nor trapped under a stacking context),
 * tethered to its anchor by Floating UI, flipping to the anchor's other side when out of room, clamped into the
 * viewport, with an arrow at the anchor.
 *
 * It is a `manual` popover with a light-dismiss of its own — a press outside it or Escape dismisses — so a press on the
 * anchor that raised it can toggle it rather than race the browser's automatic dismissal, and showing it never closes an
 * `auto` popover it was raised from. Focus moves in on opening, to the element of the body marked `autofocus`, and goes
 * back where it was when it closes. Only one bubble is open at a time.
 *
 * `data` is replaced, never mutated: the starting data is what a dismissal resolves with, so an edit bubble writes a
 * new value into {@link data} rather than changing the one it was handed.
 *
 * ```ts
 * @component('p7t-rename-bubble')
 * export class RenameBubble extends InteractionBubbleBase<string> {
 * 	constructor() {
 * 		super('')
 * 	}
 *
 * 	protected override get content() {
 * 		return html`
 * 			<input id='label' autofocus aria-label='Name' .value=${this.data}
 * 				@input=${(e: Event) => this.data = (e.target as HTMLInputElement).value}
 * 				@keydown=${(e: KeyboardEvent) => e.key === 'Enter' && this.commit()}>
 * 			${this.actionsTemplate({ label: 'Rename' })}
 * 		`
 * 	}
 * }
 *
 * const title = await RenameBubble.prompt(e, { run: title => rename(id, title) }, entity.title)
 * ```
 *
 * @fires settle - a `CustomEvent<InteractionBubbleSettlement<TData>>` once committed, dismissed or closed unanswered.
 * @csspart arrow - the pointer toward the anchor.
 * @cssprop --p7t-bubble-accent - the colour of the committing button and the focus ring; a subclass or a host
 * stylesheet retints it.
 */
export abstract class InteractionBubbleBase<TData, TOptions extends InteractionBubbleOptions<TData> = InteractionBubbleOptions<TData>> extends MoPopover {
	/** The bubble {@link prompt} has showing, of whichever kind. */
	private static current?: InteractionBubbleBase<any, any>

	/**
	 * Shows a bubble of this class for `anchor`, with `options` and the starting `data` (else the class's blank data),
	 * and resolves with the outcome: the committed data, or the starting data when dismissed.
	 *
	 * `anchor` is best the event that raised it: the bubble points at the element listening for it, or at the pointer
	 * when that element is not on screen (the viewport's centre as the last resort). An element or a viewport point
	 * works too.
	 *
	 * Prompting while another bubble is showing dismisses that one first; prompting again from the anchor already showing
	 * one dismisses it and shows nothing more — resolving with the starting data — which makes a second press of a
	 * trigger close its bubble.
	 *
	 * Where the bubble is mounted matters: a modal `<dialog>` leaves everything outside itself inert, and a press
	 * outside an open popover closes it. So it lives inside the nearest open modal dialog or popover around its anchor,
	 * and in the document's popover host otherwise.
	 */
	static prompt<TBubble extends InteractionBubbleBase<any, any>>(
		this: new () => TBubble,
		anchor: InteractionBubbleAnchor,
		options?: InteractionBubbleOptionsOf<TBubble>,
		data?: InteractionBubbleDataOf<TBubble>,
	): Promise<InteractionBubbleDataOf<TBubble>> {
		// Typed loosely inside: the signature above is what pins the data and options to the bubble class.
		const bubble: InteractionBubbleBase<any, any> = new this()
		if (data !== undefined) {
			bubble.data = data
		}

		const target = resolveAnchor(anchor)
		const showing = InteractionBubbleBase.current
		if (showing) {
			showing.dismiss()
			if (target.element && showing.anchor === target.element) {
				return Promise.resolve(bubble.data)
			}
		}

		bubble.options = options
		bubble.anchor = target.element
		bubble.coordinates = target.coordinates
		InteractionBubbleBase.current = bubble

		return new Promise((resolve, reject) => {
			bubble.settle.subscribe(async ({ committed, data }) => {
				// Taken down at once rather than hidden: a removed popover closes without the toggle mo-popover would
				// answer by pulling the focus back to its anchor.
				bubble.remove()
				try {
					if (committed) {
						await bubble.options?.run?.(data)
					}

					resolve(data)
				}
				catch (error) {
					reject(error)
				}
			})
			mountFor(target.context).append(bubble)
			bubble.open = true
		})
	}

	/** What the bubble shows and, for an edit bubble, edits. Starts as the prompt's starting data, or the blank data. */
	@property({ attribute: false }) data: TData
	/** What the bubble was prompted with. A subclass reads its own options from here, each with a fallback. */
	@property({ attribute: false }) options?: TOptions

	@event() settle!: EventDispatcher<InteractionBubbleSettlement<TData>>

	/** The data a dismissal closes with: {@link data} as it was when the bubble opened. */
	private startingData: TData
	/** Whether it has been open since it last settled, so a close before any opening is not taken as a dismissal. */
	private shown = false
	private settled = false
	/** The element focused before it opened, given the focus back if it is still inside the bubble on closing. */
	private returnFocusTo?: HTMLElement

	/** Dismisses when the anchor leaves the screen, and follows it when it resizes. */
	private readonly anchorObserver = new ResizeObserver(() => this.followAnchor())

	/**
	 * @param blankData The data the bubble starts with when it is given none: a confirmation's "not given", an edit's
	 * empty value. A subclass passes it from its own argument-less constructor, which is what custom elements and
	 * {@link prompt} construct with.
	 */
	constructor(blankData: TData) {
		super()
		this.data = this.startingData = blankData
		this.mode = 'manual'
		this.alignment = PopoverAlignment.Center
		this.placement = PopoverPlacement.BlockStart
		this.offset = 10
		if (this.positionController instanceof PopoverFloatingUiPositionController) {
			this.positionController.addMiddleware(viewportClamp)
		}

		// Stands in for mo-popover's own close handling, which hands the focus to the anchor however the popover
		// closed, stealing it back from wherever the press that dismissed it put it. The rest is kept (syncing `open`,
		// announcing `openChange`); focus is conclude()'s business, which only returns it from inside the bubble.
		this.addEventListener('toggle', event => {
			if ((event as ToggleEvent).newState === 'closed') {
				event.stopImmediatePropagation()
				this.open = false
				this.openChange.dispatch(false)
			}
		}, { capture: true })
	}

	/**
	 * The bubble's body. Mark the control to focus on opening `autofocus`, and give the element that names the bubble
	 * the id `label`; close it through {@link commit} or {@link dismiss} (or the {@link actionsTemplate} row).
	 */
	protected abstract get content(): HTMLTemplateResult

	/** The body's ARIA role: a `dialog`, or an `alertdialog` for a bubble that asks for a decision. */
	protected get bodyRole(): 'dialog' | 'alertdialog' {
		return 'dialog'
	}

	static override get styles() {
		return css`
			${super.styles}

			:host {
				--p7t-bubble-accent: var(--interactive-accent, #71549c);
				box-sizing: border-box;
				max-width: min(calc(100vw - ${viewportMargin * 2}px), 20rem);
				padding: .55rem .6rem .5rem .65rem;
				border: 1px solid color-mix(in srgb, var(--text-normal, #f2ebe4) 18%, transparent);
				border-radius: 10px;
				background: var(--background-secondary, #2a232e);
				color: var(--text-normal, #f2ebe4);
				box-shadow: 0 10px 30px color-mix(in srgb, black 48%, transparent);
				font-family: var(--font-interface, system-ui, sans-serif);
				font-size: .85rem;
				font-weight: 400;
				font-style: normal;
				line-height: 1.4;
				/* Mounted inside whatever dialog or popover surrounds its anchor, it must not inherit that one's text setting. */
				text-align: start;
				text-transform: none;
				letter-spacing: normal;
				white-space: normal;
			}

			/*
			 * Neutralises any CSS anchor positioning mo-popover's styles might carry (see Tooltip.ts): a live position-area
			 * would override the Floating UI controller's script-set left/top. Matches the specificity of those rules.
			 */
			:host([placement][alignment]) {
				position-area: none;
			}

			[part=arrow] {
				display: block;
				width: .75rem;
			}

			.body {
				display: flex;
				flex-direction: column;
				gap: .5rem;
			}

			.actions {
				display: flex;
				justify-content: flex-end;
				gap: .35rem;
			}

			button {
				display: inline-flex;
				align-items: center;
				gap: .4em;
				padding: .3em .75em;
				border: 1px solid transparent;
				border-radius: 6px;
				background-color: color-mix(in srgb, currentColor 9%, transparent);
				color: inherit;
				font: inherit;
				cursor: pointer;
				transition: background-color .15s ease, border-color .15s ease;
			}

			button:hover {
				background-color: color-mix(in srgb, currentColor 17%, transparent);
			}

			button:focus-visible {
				outline: 2px solid var(--p7t-bubble-accent);
				outline-offset: 1px;
			}

			/* A tint rather than a fill: legible on any accent without a per-theme text colour to match. */
			.commit {
				border-color: color-mix(in srgb, var(--p7t-bubble-accent) 70%, transparent);
				background-color: color-mix(in srgb, var(--p7t-bubble-accent) 26%, transparent);
				color: color-mix(in srgb, var(--p7t-bubble-accent) 30%, var(--text-normal, #f2ebe4));
				font-weight: 500;
			}

			.commit:hover {
				background-color: color-mix(in srgb, var(--p7t-bubble-accent) 40%, transparent);
			}

			button p7t-icon {
				width: 1.1em;
				height: 1.1em;
			}
		`
	}

	protected override get template() {
		return html`
			<div part='arrow'></div>
			<div class='body' role=${this.bodyRole} aria-labelledby='label'>${this.content}</div>
		`
	}

	/** The usual closing row: a dismissing button, then a committing one carrying the accent. */
	protected actionsTemplate(commit: InteractionBubbleAction = {}, dismiss: InteractionBubbleAction = {}) {
		return html`
			<div class='actions'>
				<button class='dismiss' ?autofocus=${dismiss.autofocus} @click=${() => this.dismiss()}>
					${!dismiss.icon ? nothing : html`<p7t-icon icon=${dismiss.icon}></p7t-icon>`}
					<span>${dismiss.label || fallbackDismiss}</span>
				</button>
				<button class='commit' ?autofocus=${commit.autofocus} @click=${() => this.commit()}>
					${!commit.icon ? nothing : html`<p7t-icon icon=${commit.icon}></p7t-icon>`}
					<span>${commit.label || fallbackCommit}</span>
				</button>
			</div>
		`
	}

	/** Closes with `data` — the bubble's own by default — as the outcome. */
	commit(data: TData = this.data) {
		this.conclude(true, data)
	}

	/** Closes with the starting data as the outcome. */
	dismiss() {
		this.conclude(false, this.startingData)
	}

	private conclude(committed: boolean, data: TData) {
		if (this.settled) {
			return
		}

		this.settled = true
		if (InteractionBubbleBase.current === this) {
			InteractionBubbleBase.current = undefined
		}

		if (this.shadowRoot?.activeElement) {
			this.returnFocus()
		}

		this.open = false
		this.settle.dispatch({ committed, data })
	}

	/**
	 * Focus left inside the closing bubble falls to the document, so it goes back where it was before the bubble
	 * opened. Not at once: the keystroke that closed it (an Enter in a field) is still under way and would carry on into
	 * the element given the focus, pressing the trigger again. And only while it is still stranded — on the document,
	 * or in the closed bubble — so a press elsewhere, or a `run` that focuses something, keeps the focus it gave.
	 */
	private returnFocus() {
		const target = this.returnFocusTo
		setTimeout(() => {
			const active = deepActiveElement()
			const stranded = !active || active === document.body || !!this.shadowRoot?.activeElement
			if (stranded && target?.isConnected) {
				target.focus({ preventScroll: true })
			}
		})
	}

	protected override willUpdate(changed: PropertyValues<this>) {
		super.willUpdate(changed)
		const placement = this.options?.placement
		if (changed.has('options') && placement && placements.includes(placement)) {
			this.placement = placement
		}
	}

	protected override openUpdated() {
		const opening = this.open && !this.shown
		if (opening) {
			this.returnFocusTo = deepActiveElement()
		}

		super.openUpdated()
		if (opening) {
			this.shown = true
			this.settled = false
			this.startingData = this.data
			this.listen()
			this.renderRoot.querySelector<HTMLElement>('[autofocus]')?.focus({ preventScroll: true })
		}
		else if (!this.open && this.shown) {
			this.shown = false
			this.unlisten()
			// Closed from outside (hidden, say) without settling: that dismisses.
			this.dismiss()
		}
	}

	override disconnectedCallback() {
		super.disconnectedCallback()
		if (this.shown) {
			this.shown = false
			this.unlisten()
			this.dismiss()
		}
	}

	private listen() {
		document.addEventListener('pointerdown', this.onDocumentPointerDown, true)
		document.addEventListener('keydown', this.onDocumentKeyDown, true)
		if (this.anchor) {
			this.anchorObserver.observe(this.anchor)
		}
	}

	private unlisten() {
		document.removeEventListener('pointerdown', this.onDocumentPointerDown, true)
		document.removeEventListener('keydown', this.onDocumentKeyDown, true)
		this.anchorObserver.disconnect()
	}

	// A press on the anchor is left alone: its own click prompts again, which closes the bubble (see prompt).
	private readonly onDocumentPointerDown = (event: PointerEvent) => {
		const path = event.composedPath()
		if (!path.includes(this) && !(this.anchor && path.includes(this.anchor))) {
			this.dismiss()
		}
	}

	// Taken before anything else hears it, so an Escape meant for the bubble does not also close a dialog around it.
	private readonly onDocumentKeyDown = (event: KeyboardEvent) => {
		if (event.key === 'Escape') {
			event.preventDefault()
			event.stopPropagation()
			this.dismiss()
		}
	}

	private followAnchor() {
		if (!isOnScreen(this.anchor)) {
			this.dismiss()
			return
		}

		this.requestUpdate()
	}
}

/** Where a bubble points, and the element whose surroundings decide where it is mounted. */
interface ResolvedAnchor {
	readonly element?: HTMLElement
	readonly coordinates?: PopoverCoordinates
	readonly context?: Element
}

/**
 * The element to point at when the anchor names one on screen; otherwise a point — the pointer's for a mouse event,
 * the viewport's centre as the last resort (an element removed, hidden, or `display: contents`, so without a box).
 */
function resolveAnchor(anchor: InteractionBubbleAnchor): ResolvedAnchor {
	const centre = (): PopoverCoordinates => [window.innerWidth / 2, window.innerHeight / 2]
	if (Array.isArray(anchor)) {
		return { coordinates: anchor }
	}

	if (anchor instanceof Event) {
		const origin = anchor.composedPath()[0]
		const element = [anchor.currentTarget, origin].find(isOnScreen)
		if (element) {
			return { element, context: element }
		}

		const context = origin instanceof Element ? origin : undefined
		// A click the keyboard raised carries no pointer position (0, 0).
		const pointed = anchor instanceof MouseEvent && (anchor.clientX !== 0 || anchor.clientY !== 0)
		return { context, coordinates: pointed ? [anchor.clientX, anchor.clientY] : centre() }
	}

	return isOnScreen(anchor) ? { element: anchor, context: anchor } : { context: anchor, coordinates: centre() }
}

function isOnScreen(element: unknown): element is HTMLElement {
	return element instanceof HTMLElement && element.isConnected && element.getClientRects().length > 0
}

/**
 * The element a bubble is mounted in: the nearest open modal dialog or popover around its context — outside a modal
 * dialog it would be inert, and a press on it outside an open popover would close that popover — or else the
 * document's popover host, where the app's other popovers live.
 */
function mountFor(context: Element | undefined): Element {
	for (let node = context ? composedParent(context) : null; node; node = composedParent(node)) {
		if (node instanceof HTMLElement && node.matches('dialog:modal, :popover-open')) {
			return node
		}
	}

	return PopoverHost.get(context instanceof HTMLElement ? context : document.body)
}

/** A node's parent in the flattened tree: the slot it is assigned to, its shadow root's host, or its parent. */
function composedParent(node: Node): Node | null {
	if (node instanceof Element && node.assignedSlot) {
		return node.assignedSlot
	}

	const parent = node.parentNode
	return parent instanceof ShadowRoot ? parent.host : parent
}

/** The focused element, looked for through open shadow roots. */
function deepActiveElement(): HTMLElement | undefined {
	let active = document.activeElement
	while (active?.shadowRoot?.activeElement) {
		active = active.shadowRoot.activeElement
	}

	return active instanceof HTMLElement ? active : undefined
}
