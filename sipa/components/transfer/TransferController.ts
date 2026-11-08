import { AsyncDirective, Controller, directive, noChange, PartType, ReactiveElement, type ElementPart, type PartInfo } from '@a11d/lit'

/**
 * What becomes of an item on its source once another host has fully accepted it: `clone` keeps it where it was
 * (the receiver gets the same instance, the source is untouched), `move` removes it from the source's side.
 */
export type TransferOutbound = 'clone' | 'move'

/**
 * The life of a transaction. `dragging` while the pointer carries the item; `accepting` from the drop until the
 * receiver's acceptance settles; then one of the three terminal states. A drag that never lands on a compatible
 * host ends `cancelled`.
 */
export type TransferState = 'dragging' | 'accepting' | 'accepted' | 'rejected' | 'cancelled'

/**
 * One item travelling from one host to another. Created on drag start and handed to every hook, so a receiver
 * can inspect where an item comes from and a source can learn who took it.
 */
export interface TransferTransaction<T = unknown> {
	/** The kind the item travels under — what a receiver must list in its `accepts` to be a candidate. */
	readonly kind: string
	readonly item: T
	/** The source's outbound policy, fixed at drag start. */
	readonly outbound: TransferOutbound
	readonly source: TransferController<T>
	/** The host that took the item; set on drop. */
	target?: TransferController<T>
	state: TransferState
}

/**
 * How a host takes part in transfers. Everything is optional: a source-only host names a `kind` and a `bag`, a
 * receive-only host lists what it `accepts` and how to `accept` it.
 */
export interface TransferOptions<T> {
	/**
	 * The kind of item this host offers outbound, as a name or resolved per item for a mixed bag. By convention a
	 * kind is the core's runtime type name (`'Objective'`), so any two hosts showing the same entity type
	 * interoperate without agreeing on anything else. A host that never offers anything may omit it.
	 */
	kind?: string | ((item: T) => string | undefined)

	/**
	 * The kinds this host receives. Defaults to its own `kind` when that is a name, else to nothing — a host
	 * offering a mixed bag says explicitly what it takes back.
	 */
	accepts?: readonly string[]

	/**
	 * The host's collection: where the default receive appends to, what a `move` removes from, and what the
	 * default candidacy check consults to refuse an item already present. Read on demand, so it may follow a
	 * property that arrives later. A host that translates every receive and never moves anything needs none.
	 */
	bag?: () => T[] | undefined

	/** What happens on this host once a receiver has accepted one of its items. Default `clone`. */
	outbound?: TransferOutbound

	/**
	 * How two items are told apart, for the already-present check and for a `move` removal. Defaults to the
	 * item's `id` when it has one, else the instance itself — the repository system hands out one canonical
	 * instance per identity, so either reading agrees with it.
	 */
	identity?: (item: T) => unknown

	/**
	 * Whether this host can take the in-flight item right now, evaluated once when a drag starts. Replaces the
	 * default (the kind is accepted and the item is not already in the bag); the kind check always applies.
	 */
	canAccept?: (item: T, transaction: TransferTransaction<T>) => boolean

	/**
	 * Translates a receive. Replaces the default behaviour — appending to the bag — with whatever taking the
	 * item means for this host: an API call, a repository mutate, a modal. Resolves to whether the item was
	 * taken; only then does the source's outbound policy apply. A rejection (or a throw) leaves both sides as
	 * they were.
	 */
	accept?: (item: T, transaction: TransferTransaction<T>) => boolean | Promise<boolean>

	/**
	 * Removes an item this host `move`d out, once accepted elsewhere. Replaces the default, which splices the
	 * bag by identity — a host whose bag is not its own to mutate (a repository-owned array) translates here.
	 */
	remove?: (item: T, transaction: TransferTransaction<T>) => void

	/**
	 * The element that receives drops, when it is not the host itself. Re-resolved after every host update, so
	 * it may point into the host's rendered content.
	 */
	dropTarget?: () => Element | null | undefined
}

/** Reflected on a drop target for the life of a drag it could accept, for `:host([transfer-target])` styling. */
export const transferTargetAttribute = 'transfer-target'
/** Reflected on a drop target while a compatible item hovers over it, for `:host([transfer-over])` styling. */
export const transferOverAttribute = 'transfer-over'

/** Dispatched on a receiver's host once it has accepted an item. Bubbles, composed; `detail` is the transaction. */
export const transferAcceptedEvent = 'transferaccepted'
/**
 * Dispatched on a source's host once a receiver has answered — accepted (after the outbound policy has applied)
 * or rejected. Bubbles, composed; `detail` is the transaction. A cancelled drag dispatches nothing.
 */
export const transferSettledEvent = 'transfersettled'

/** The `DataTransfer` type a drag carries, so foreign drags (a file, a selection) never read as a transfer. */
const dataTransferType = 'application/x-p7t-transfer'

/** How long a finger rests on an item before it lifts — what tells carrying it from scrolling past it or tapping it. */
const touchHoldMs = 350
/** How far a resting finger may wander: further before the lift is a scroll, further after it starts the carry. */
const touchSlopPx = 8
/** How near a scroller's edge a carried item scrolls it, and by how much per frame at the very edge. */
const autoscrollEdgePx = 48
const autoscrollStepPx = 14
/**
 * How long after a touch carry the platform's own follow-ups to the long press — its context menu, a trailing click —
 * are swallowed (a new press ends it at once). Platforms differ in when they send them, up to the release itself.
 */
const touchAftermathMs = 500

type AnyTransferController = TransferController<any>

/** Every connected controller — what a drag start consults to flag its candidates. */
const connected = new Set<AnyTransferController>()

/** The drag in flight, if any. One at a time by nature of pointer drag. */
let active: TransferTransaction<any> | undefined

/**
 * Lets a component hand items to, and take items from, other components through drag and drop — a mouse's (and a
 * pen's) through the platform's drag and drop, a finger's through the controller's own {@link TouchCarry}: press and
 * hold to lift an item, carry it, set it down.
 *
 * Component-controller model, akin to {@link ContextMenuController} / {@link EntityWatch}: a host declares one
 * as a field with its options, marks each row it offers with `${transfer.draggable(item)}`, and is done. Every
 * host holding one joins a document-wide exchange keyed by **kind**; when a drag starts, each other connected
 * host that accepts the kind and whose candidacy check passes is flagged as a target (`transfer-target`), and
 * the one under the pointer as hovered (`transfer-over`) — both reflected as attributes on its drop target for
 * styling. Dropping runs the receiver's acceptance and, only once that settles as accepted, the source's
 * outbound policy: keep the item (`clone`) or remove it (`move`).
 *
 * The receive has a default and a translation. By default an accepted item is appended to the receiver's
 * `bag`; a host whose "taking" means something else — adding an objective to a Polaris cycle through an API
 * call — supplies `accept` and the default is bypassed. The same split exists on the source: the default
 * `move` removal splices the bag, `remove` replaces it.
 *
 * Independent of the repository system, and designed to sit beside it: the item travels by reference, so a
 * canonical instance stays canonical on both sides; the identity default reads the `id` the repositories key
 * on; and a repository-backed host translates its receive through `mutate` rather than letting the default
 * touch an array it does not own.
 */
export class TransferController<T> extends Controller {
	private wiredTarget?: Element
	private candidate = false
	private hoverDepth = 0

	public constructor(host: ReactiveElement, private readonly options: TransferOptions<T> = {}) {
		super(host)
	}

	/** The drag currently in flight anywhere in the document, if any. */
	public static get active(): TransferTransaction | undefined {
		return active
	}

	private get element(): HTMLElement {
		return this.host as unknown as HTMLElement
	}

	/** The kinds this host receives. */
	public get accepts(): readonly string[] {
		return this.options.accepts ?? (typeof this.options.kind === 'string' ? [this.options.kind] : [])
	}

	/** What happens here once a receiver has accepted one of this host's items. */
	public get outbound(): TransferOutbound {
		return this.options.outbound ?? 'clone'
	}

	/** The drag in flight, when there is one. */
	public get transaction(): TransferTransaction<T> | undefined {
		return active as TransferTransaction<T> | undefined
	}

	/** Whether this host could take the item currently being dragged. */
	public get isCandidate(): boolean {
		return this.candidate
	}

	/** Whether a compatible item is currently hovering over this host's drop target. */
	public get isHovered(): boolean {
		return this.candidate && this.hoverDepth > 0
	}

	/** Whether this host is the source of the drag in flight. */
	public get isSource(): boolean {
		return active?.source === this
	}

	public override hostConnected(): void {
		connected.add(this)
		this.wire()
		if (active) {
			this.enter(active)
		}
	}

	public override hostUpdated(): void {
		// The drop target may live in rendered content that was not there on connect, or be swapped by a render.
		this.wire()
	}

	public override hostDisconnected(): void {
		connected.delete(this)
		if (active?.source === this && active.state === 'dragging') {
			// Our item is mid-air and we are gone: there is no one left to settle for, so end it.
			this.endDrag(active, 'cancelled')
		}
		this.leave()
		this.unwire()
	}

	/**
	 * Marks an element as one of this host's offered items:
	 *
	 * ```ts
	 * html`<p7t-objective-item .entity=${objective} ${this.transfer.draggable(objective)}></p7t-objective-item>`
	 * ```
	 *
	 * The element becomes draggable and, when dragged, starts a transaction carrying `item` under this host's
	 * kind. Re-rendering with another item updates what the element carries without re-wiring it.
	 *
	 * When the marked element is only a handle — a row's icon standing in for the row — `image` names what the
	 * pointer should be seen carrying instead; it is resolved at drag start.
	 */
	public draggable(item: T, image?: () => Element | null | undefined) {
		return draggableDirective(this as AnyTransferController, item, image)
	}

	/** The kind an item travels under, from this host's declaration. */
	private kindOf(item: T): string | undefined {
		const kind = this.options.kind
		return typeof kind === 'function' ? kind(item) : kind
	}

	private identityOf(item: T): unknown {
		if (this.options.identity) {
			return this.options.identity(item)
		}

		const id = (item as { id?: unknown } | null | undefined)?.id
		return id === undefined || id === null ? item : id
	}

	private contains(item: T): boolean {
		const bag = this.options.bag?.()
		if (!bag) {
			return false
		}

		const identity = this.identityOf(item)
		return bag.some(member => this.identityOf(member) === identity)
	}

	/* ---------------- Source side ---------------- */

	/**
	 * Starts a transaction for one of this host's items. The `dragstart` entry point of the {@link draggable}
	 * directive — a host marks its rows with the directive rather than calling this.
	 */
	public beginDrag(item: T, event: DragEvent): void {
		const transaction = event.dataTransfer ? this.open(item) : undefined
		if (!transaction || !event.dataTransfer) {
			event.preventDefault()
			return
		}

		event.dataTransfer.effectAllowed = transaction.outbound === 'move' ? 'move' : 'copy'
		// Only the kind rides on the native payload — enough for the drag to start everywhere, and under a type
		// nothing else reads, so a stray drop into an editor inserts nothing. The item itself never does: it is an
		// object handed over by reference within the document.
		event.dataTransfer.setData(dataTransferType, transaction.kind)
	}

	/**
	 * Opens a transaction for one of this host's items and flags every candidate — the start a mouse's drag and a
	 * finger's carry share. Nothing when the item has no kind to travel under.
	 */
	private open(item: T): TransferTransaction<T> | undefined {
		const kind = this.kindOf(item)
		if (!kind) {
			console.warn(`PLAINTORCH: <${this.element.tagName.toLowerCase()}> offers an item for transfer but declares no kind for it.`)
			return undefined
		}

		if (active) {
			// A pointer carries one item at a time, so a drag still recorded as active is a stale one: its source row
			// was removed mid-drag, and Chromium then never fires the `dragend` that would have ended it.
			this.endDrag(active, 'cancelled')
		}

		const transaction: TransferTransaction<T> = { kind, item, outbound: this.outbound, source: this, state: 'dragging' }
		active = transaction
		for (const controller of connected) {
			controller.enter(transaction)
		}
		this.host.requestUpdate()
		return transaction
	}

	/* ---------------- Touch ---------------- */

	/**
	 * The `pointerdown` entry point of the {@link draggable} directive for a FINGER: starts a {@link TouchCarry}, the
	 * touch counterpart of a native drag, run on pointer events. A mouse and a pen keep the platform's drag and drop.
	 */
	public pressTouch(item: T, element: HTMLElement, event: PointerEvent, image?: () => Element | null | undefined): void {
		TouchCarry.press(this as AnyTransferController, item, element, event, image)
	}

	/** The carry's start: opens the transaction for the lifted item. Resolves to whether there is one. */
	public carryStart(item: T): boolean {
		return !!this.open(item)
	}

	/** The element a carried item is dropped on — what the carry hit-tests against; set while the host is connected. */
	public get wiredDropTarget(): Element | undefined {
		return this.wiredTarget
	}

	/** The carry's hover: a carried item is over this host's drop target, or has left it — `dragenter`/`dragleave`'s part. */
	public hover(over: boolean): void {
		if (!this.candidate || (this.hoverDepth > 0) === over) {
			return
		}

		this.hoverDepth = over ? 1 : 0
		this.reflect()
		this.host.requestUpdate()
	}

	/** The carry's drop: the carried item lands on this host — the `drop` event's part. */
	public dropCarried(): void {
		const transaction = active as TransferTransaction<T> | undefined
		if (transaction && this.candidate) {
			this.land(transaction)
		}
	}

	/**
	 * The `dragend` entry point of the {@link draggable} directive. A drag that never dropped on a candidate ends
	 * here, cancelled; one that did has already been ended by the drop.
	 */
	public finishDrag(): void {
		if (active?.source === this && active.state === 'dragging') {
			this.endDrag(active, 'cancelled')
		}
	}

	/** Applies the outbound policy once a receiver has accepted, then announces the outcome. */
	private release(transaction: TransferTransaction<T>): void {
		if (transaction.outbound === 'move') {
			if (this.options.remove) {
				this.options.remove(transaction.item, transaction)
			}
			else {
				this.removeFromBag(transaction.item)
			}
		}

		this.host.requestUpdate()
		this.dispatch(transferSettledEvent, transaction)
	}

	private removeFromBag(item: T): void {
		const bag = this.options.bag?.()
		if (!bag) {
			return
		}

		const identity = this.identityOf(item)
		const index = bag.findIndex(member => this.identityOf(member) === identity)
		if (index >= 0) {
			bag.splice(index, 1)
		}
	}

	/* ---------------- Receiving side ---------------- */

	/** A drag has started somewhere: decide whether this host is a candidate for it, and show it if so. */
	private enter(transaction: TransferTransaction<any>): void {
		const candidate = transaction.source !== this
			&& this.accepts.includes(transaction.kind)
			&& (this.options.canAccept
				? this.options.canAccept(transaction.item as T, transaction as TransferTransaction<T>)
				: !this.contains(transaction.item as T))

		if (candidate === this.candidate) {
			return
		}

		this.candidate = candidate
		this.hoverDepth = 0
		this.reflect()
		this.host.requestUpdate()
	}

	/** The drag is over: drop the candidacy and any hover, whatever they were. */
	private leave(): void {
		if (!this.candidate && this.hoverDepth === 0) {
			return
		}

		this.candidate = false
		this.hoverDepth = 0
		this.reflect()
		this.host.requestUpdate()
	}

	/** Ends the drag for every host and lets go of it as the active one. Settlement of a drop continues on its own. */
	private endDrag(transaction: TransferTransaction<any>, state: 'cancelled' | 'accepting'): void {
		transaction.state = state
		if (active === transaction) {
			active = undefined
		}

		for (const controller of connected) {
			controller.leave()
		}
		transaction.source.host.requestUpdate()
	}

	private async complete(transaction: TransferTransaction<T>): Promise<void> {
		const item = transaction.item
		let accepted = false
		try {
			accepted = this.options.accept
				? await this.options.accept(item, transaction)
				: this.acceptIntoBag(item)
		}
		catch (error) {
			console.error(`PLAINTORCH: <${this.element.tagName.toLowerCase()}> failed to accept a transferred ${transaction.kind}.`, error)
			accepted = false
		}

		transaction.state = accepted ? 'accepted' : 'rejected'
		if (accepted) {
			this.host.requestUpdate()
			this.dispatch(transferAcceptedEvent, transaction)
			transaction.source.release(transaction)
		}
		else {
			transaction.source.dispatch(transferSettledEvent, transaction)
		}
	}

	private acceptIntoBag(item: T): boolean {
		const bag = this.options.bag?.()
		if (!bag || this.contains(item)) {
			return false
		}

		bag.push(item)
		return true
	}

	private dispatch(type: string, transaction: TransferTransaction<T>): void {
		this.element.dispatchEvent(new CustomEvent<TransferTransaction<T>>(type, { detail: transaction, bubbles: true, composed: true }))
	}

	private reflect(): void {
		const target = this.wiredTarget
		if (!target) {
			return
		}

		target.toggleAttribute(transferTargetAttribute, this.candidate)
		target.toggleAttribute(transferOverAttribute, this.isHovered)
	}

	private wire(): void {
		const target = this.options.dropTarget?.() ?? this.element
		if (target === this.wiredTarget) {
			return
		}

		this.unwire()
		this.wiredTarget = target
		target.addEventListener('dragenter', this.onDragEnter)
		target.addEventListener('dragover', this.onDragOver)
		target.addEventListener('dragleave', this.onDragLeave)
		target.addEventListener('drop', this.onDrop)
		this.reflect()
	}

	private unwire(): void {
		const target = this.wiredTarget
		if (!target) {
			return
		}

		target.removeEventListener('dragenter', this.onDragEnter)
		target.removeEventListener('dragover', this.onDragOver)
		target.removeEventListener('dragleave', this.onDragLeave)
		target.removeEventListener('drop', this.onDrop)
		target.removeAttribute(transferTargetAttribute)
		target.removeAttribute(transferOverAttribute)
		this.wiredTarget = undefined
	}

	// Enter/leave are counted rather than toggled: crossing into a child fires an enter for the child before the
	// leave for its parent, so the depth dips to zero only when the pointer really exits the target.
	private readonly onDragEnter = (event: Event): void => {
		if (!active || !this.candidate) {
			return
		}

		event.preventDefault()
		this.hoverDepth++
		if (this.hoverDepth === 1) {
			this.reflect()
			this.host.requestUpdate()
		}
	}

	private readonly onDragOver = (event: Event): void => {
		if (!active || !this.candidate) {
			return
		}

		// Without this the browser refuses the drop; the effect is what draws the copy/move cursor.
		event.preventDefault()
		const dataTransfer = (event as DragEvent).dataTransfer
		if (dataTransfer) {
			dataTransfer.dropEffect = active.outbound === 'move' ? 'move' : 'copy'
		}
	}

	private readonly onDragLeave = (): void => {
		if (!this.candidate || this.hoverDepth === 0) {
			return
		}

		this.hoverDepth--
		if (this.hoverDepth === 0) {
			this.reflect()
			this.host.requestUpdate()
		}
	}

	private readonly onDrop = (event: Event): void => {
		const transaction = active as TransferTransaction<T> | undefined
		if (!transaction || !this.candidate) {
			return
		}

		event.preventDefault()
		event.stopPropagation()
		this.land(transaction)
	}

	/** An item lands here — dropped by a mouse or set down by a finger. */
	private land(transaction: TransferTransaction<T>): void {
		transaction.target = this
		// The drag is over for everyone the moment it lands; only this host and the source stay involved, on
		// the transaction, until the acceptance settles.
		this.endDrag(transaction, 'accepting')
		void this.complete(transaction)
	}
}

interface Point {
	readonly x: number
	readonly y: number
}

/**
 * One finger carrying one item: the touch counterpart of a native drag, run on pointer events, and the same exchange
 * underneath — the source opens the transaction, every candidate is flagged, the drop target under the finger is
 * hovered, and setting the item down there lands it, exactly as a mouse's drop does. A carry is the controller's own
 * rather than the platform's because touch drag and drop is anything but uniform: absent in some web views, a
 * different gesture on every platform that has it, and everywhere a race with the long press's context menu.
 *
 * The gesture:
 * - **press** — a finger lands on an item. Nothing happens yet: moving away is a scroll (the platform's, untouched),
 *   lifting soon is a tap (its click goes through).
 * - **lift** — the finger rested for {@link touchHoldMs}: the item lifts (a copy of it rises over the page, the original
 *   fades, a light haptic tick), and the page stops panning under the finger.
 * - **carry** — the lifted item moves past {@link touchSlopPx}: the transaction opens and the copy follows the finger,
 *   lighting the drop target beneath it; resting near a scroller's edge scrolls it, snapping suspended meanwhile.
 * - **set down** — over a target, the item lands there; anywhere else the carry is cancelled.
 *
 * A lift released without moving is a long press: the item's context menu opens, as a long press always did. The
 * platform's own long-press menu — and a click trailing the gesture — are swallowed, since platforms send them at
 * different moments of the press; the carry raises the menu itself instead.
 */
class TouchCarry {
	private static current?: TouchCarry
	/** Until when the platform's follow-ups to a carry (its long-press menu, a click) are swallowed. */
	private static aftermathUntil = 0
	private static listening = false

	private phase: 'pressing' | 'lifted' | 'carrying' = 'pressing'
	private readonly pointerId: number
	private readonly origin: Point
	private point: Point
	/** Where in the image the finger holds it, so the copy does not jump under the finger as it lifts. */
	private readonly grab: Point
	private readonly hold: ReturnType<typeof setTimeout>
	private ghost?: HTMLElement
	private hovered?: AnyTransferController
	private frame?: number
	/** The inline styles changed on the press and the lift, to put back as they were. */
	private readonly restore = new Map<HTMLElement, Map<string, string>>()

	private constructor(
		private readonly source: AnyTransferController,
		private readonly item: unknown,
		private readonly element: HTMLElement,
		private readonly image: Element,
		event: PointerEvent,
	) {
		this.pointerId = event.pointerId
		this.origin = this.point = { x: event.clientX, y: event.clientY }
		const bounds = image.getBoundingClientRect()
		this.grab = { x: event.clientX - bounds.left, y: event.clientY - bounds.top }
		// Text must not start selecting under a resting finger — the platforms begin a selection about when the lift does.
		this.style(element, 'user-select', 'none')
		this.style(element, '-webkit-user-select', 'none')
		this.hold = setTimeout(() => this.lift(), touchHoldMs)
		window.addEventListener('pointermove', this.onPointerMove, true)
		window.addEventListener('pointerup', this.onPointerUp, true)
		window.addEventListener('pointercancel', this.onPointerCancel, true)
	}

	/** A finger landed on an item. One carry at a time; a second finger is the page's. */
	public static press(source: AnyTransferController, item: unknown, element: HTMLElement, event: PointerEvent, image?: () => Element | null | undefined) {
		if (TouchCarry.current || !event.isPrimary) {
			return
		}

		TouchCarry.listen()
		TouchCarry.current = new TouchCarry(source, item, element, image?.() ?? element, event)
	}

	/** Whether a finger is down on `element`'s item — where a drag the platform starts on its own must yield. */
	public static isPressing(element: Element): boolean {
		return TouchCarry.current?.element === element
	}

	/** Whether `element`'s item is lifted or carried — from then on the finger is the carry's, not the page's to pan. */
	public static isHolding(element: Element): boolean {
		const carry = TouchCarry.current
		return carry?.element === element && carry.phase !== 'pressing'
	}

	/**
	 * The page-wide listeners, once: swallowing the platform's long-press menu and trailing click while a carry is
	 * lifted and just after (see {@link touchAftermathMs}); any new press ends that aftermath at once.
	 */
	private static listen() {
		if (TouchCarry.listening) {
			return
		}

		TouchCarry.listening = true
		const swallow = (event: Event) => {
			const lifted = !!TouchCarry.current && TouchCarry.current.phase !== 'pressing'
			// Only the platform's own: the menu the carry raises itself is synthetic, and must go through.
			if (event.isTrusted && (lifted || performance.now() < TouchCarry.aftermathUntil)) {
				event.preventDefault()
				event.stopPropagation()
			}
		}
		window.addEventListener('contextmenu', swallow, true)
		window.addEventListener('click', swallow, true)
		window.addEventListener('pointerdown', () => TouchCarry.aftermathUntil = 0, true)
	}

	private lift() {
		if (TouchCarry.current !== this || !this.element.isConnected) {
			this.end()
			return
		}

		this.phase = 'lifted'
		this.ghost = ghostOf(this.image)
		this.place()
		if (this.image instanceof HTMLElement) {
			this.style(this.image, 'opacity', '.35')
		}
		haptic(10)
	}

	private readonly onPointerMove = (event: PointerEvent) => {
		if (event.pointerId !== this.pointerId) {
			return
		}

		this.point = { x: event.clientX, y: event.clientY }
		const moved = Math.hypot(this.point.x - this.origin.x, this.point.y - this.origin.y) > touchSlopPx
		if (this.phase === 'pressing') {
			if (moved) {
				// A scroll, not a carry: the platform pans as it always did.
				this.end()
			}
			return
		}

		if (this.phase === 'lifted' && moved) {
			if (!this.source.carryStart(this.item)) {
				this.end()
				return
			}
			this.phase = 'carrying'
		}

		this.schedule()
	}

	private readonly onPointerUp = (event: PointerEvent) => {
		if (event.pointerId !== this.pointerId) {
			return
		}

		if (this.phase === 'carrying') {
			// Hit-test where the finger left, not where the last frame was drawn.
			this.track(false)
			const target = this.hovered
			this.end(true)
			if (target) {
				target.dropCarried()
			}
			else {
				this.source.finishDrag()
			}
			return
		}

		if (this.phase === 'lifted') {
			// A long press that never moved: the item's context menu, which the carry swallowed from the platform.
			this.end(true)
			this.element.dispatchEvent(new MouseEvent('contextmenu', {
				bubbles: true, composed: true, cancelable: true, button: 2, clientX: this.point.x, clientY: this.point.y,
			}))
			return
		}

		// A tap: its click is the item's own business.
		this.end()
	}

	private readonly onPointerCancel = (event: PointerEvent) => {
		if (event.pointerId !== this.pointerId) {
			return
		}

		const carrying = this.phase === 'carrying'
		this.end(this.phase !== 'pressing')
		if (carrying) {
			this.source.finishDrag()
		}
	}

	private schedule() {
		this.frame ??= requestAnimationFrame(() => {
			this.frame = undefined
			this.track(true)
		})
	}

	/** One frame of the carry: the copy follows the finger, the target beneath it lights, and an edge scrolls. */
	private track(scroll: boolean) {
		this.place()
		if (this.phase !== 'carrying') {
			return
		}

		// The transaction can end under the carry — its source left the page mid-carry, say.
		if (active?.source !== this.source || active.state !== 'dragging') {
			this.end()
			return
		}

		const hit = deepElementFromPoint(this.point)
		const target = dropTargetAt(hit)
		if (target !== this.hovered) {
			this.hovered?.hover(false)
			this.hovered = target
			target?.hover(true)
		}

		if (scroll && this.autoscroll(hit)) {
			// The finger rests at the edge: keep scrolling without waiting for it to move.
			this.schedule()
		}
	}

	private place() {
		this.ghost?.style.setProperty('translate', `${this.point.x - this.grab.x}px ${this.point.y - this.grab.y}px`)
	}

	/**
	 * Scrolls the innermost scroller under the finger that can still go towards the edge the finger is near — a day
	 * strip sideways, a list down. Its snapping is suspended for the rest of the carry, or a mandatory snap would undo
	 * every small step; it settles back onto a snap position once the carry ends.
	 */
	private autoscroll(hit: Element | null): boolean {
		const scrollers: HTMLElement[] = []
		for (let node: Node | null = hit; node; node = composedParent(node)) {
			if (node instanceof HTMLElement) {
				scrollers.push(node)
			}
		}

		const page = document.scrollingElement
		if (page instanceof HTMLElement && !scrollers.includes(page)) {
			scrollers.push(page)
		}

		for (const scroller of scrollers) {
			const step = edgeStep(scroller, this.point)
			if (!step.x && !step.y) {
				continue
			}

			const before = { left: scroller.scrollLeft, top: scroller.scrollTop }
			this.style(scroller, 'scroll-snap-type', 'none')
			scroller.scrollBy(step.x, step.y)
			if (scroller.scrollLeft !== before.left || scroller.scrollTop !== before.top) {
				return true
			}
		}

		return false
	}

	/** Sets an inline style for the life of the carry, remembering what to put back. */
	private style(element: HTMLElement, property: string, value: string) {
		let saved = this.restore.get(element)
		if (!saved) {
			saved = new Map()
			this.restore.set(element, saved)
		}

		if (!saved.has(property)) {
			saved.set(property, element.style.getPropertyValue(property))
		}
		element.style.setProperty(property, value)
	}

	private end(aftermath = false) {
		clearTimeout(this.hold)
		if (this.frame !== undefined) {
			cancelAnimationFrame(this.frame)
			this.frame = undefined
		}

		window.removeEventListener('pointermove', this.onPointerMove, true)
		window.removeEventListener('pointerup', this.onPointerUp, true)
		window.removeEventListener('pointercancel', this.onPointerCancel, true)
		this.hovered?.hover(false)
		this.hovered = undefined
		this.ghost?.remove()
		this.ghost = undefined
		for (const [element, saved] of this.restore) {
			for (const [property, value] of saved) {
				element.style.setProperty(property, value)
			}
		}
		this.restore.clear()

		if (aftermath) {
			TouchCarry.aftermathUntil = performance.now() + touchAftermathMs
		}
		if (TouchCarry.current === this) {
			TouchCarry.current = undefined
		}
	}
}

/**
 * The copy a finger carries: the image re-made in the top layer — above every dialog, out of every scroller that would
 * clip it — and set to ignore the pointer, so hit-testing sees what is under it. A lit element draws from its
 * properties, which a node clone does not carry, so it is re-made as a fresh element of its kind holding the same
 * properties. The custom properties and text styles it inherits where it stands (theme tokens, accents) go along,
 * since none of its ancestors do.
 */
function ghostOf(image: Element): HTMLElement {
	const ghost = document.createElement('div')
	ghost.popover = 'manual'
	ghost.inert = true
	ghost.setAttribute('aria-hidden', 'true')

	const inherited = getComputedStyle(image)
	for (let index = 0; index < inherited.length; index++) {
		const property = inherited.item(index)
		if (property.startsWith('--')) {
			ghost.style.setProperty(property, inherited.getPropertyValue(property))
		}
	}
	for (const property of ['color', 'font-family', 'font-size', 'font-weight', 'line-height', 'direction']) {
		ghost.style.setProperty(property, inherited.getPropertyValue(property))
	}

	Object.assign(ghost.style, {
		position: 'fixed',
		inset: '0 auto auto 0',
		margin: '0',
		padding: '0',
		border: 'none',
		background: 'none',
		overflow: 'visible',
		width: `${image.getBoundingClientRect().width}px`,
		pointerEvents: 'none',
		zIndex: '2147483647',
		opacity: '.92',
		scale: '1.03',
		filter: 'drop-shadow(0 .6em 1.2em rgb(0 0 0 / .45))',
	})

	ghost.append(copyOf(image))
	document.body.append(ghost)
	try {
		ghost.showPopover()
	}
	catch {
		// No popover support: the fixed position and the z-index carry it.
	}
	return ghost
}

/** A copy of an element that draws like it — re-made from its properties when it is a lit element. */
function copyOf(element: Element): Element {
	if (!(element instanceof ReactiveElement)) {
		return element.cloneNode(true) as Element
	}

	const copy = document.createElement(element.localName) as unknown as Record<PropertyKey, unknown>
	for (const attribute of Array.from(element.attributes)) {
		(copy as unknown as Element).setAttribute(attribute.name, attribute.value)
	}
	for (const property of (element.constructor as typeof ReactiveElement).elementProperties.keys()) {
		copy[property] = (element as unknown as Record<PropertyKey, unknown>)[property]
	}
	return copy as unknown as Element
}

/** The element under a point, through every shadow root on the way down — `elementFromPoint` stops at the first host. */
function deepElementFromPoint(point: Point): Element | null {
	let element = document.elementFromPoint(point.x, point.y)
	while (element?.shadowRoot) {
		const inner = element.shadowRoot.elementFromPoint(point.x, point.y)
		if (!inner || inner === element) {
			break
		}
		element = inner
	}
	return element
}

/** The parent across shadow boundaries: a shadow root hands over to its host. */
function composedParent(node: Node): Node | null {
	return node instanceof ShadowRoot ? node.host : node.parentNode
}

/**
 * The candidate whose drop target holds an element — the innermost, as a native drop is taken by the deepest target
 * that stops it (a column's footer bin before the column around it).
 */
function dropTargetAt(element: Element | null): AnyTransferController | undefined {
	const targets = new Map<Node, AnyTransferController>()
	for (const controller of connected) {
		const target = controller.isCandidate ? controller.wiredDropTarget : undefined
		if (target) {
			targets.set(target, controller)
		}
	}

	for (let node: Node | null = element; node; node = composedParent(node)) {
		const controller = targets.get(node)
		if (controller) {
			return controller
		}
	}

	return undefined
}

/** How far a finger at `point` scrolls `scroller` this frame: towards whichever of its edges the finger is near. */
function edgeStep(scroller: HTMLElement, point: Point): Point {
	const page = scroller === document.scrollingElement
	const bounds = page ? new DOMRect(0, 0, innerWidth, innerHeight) : scroller.getBoundingClientRect()
	const style = getComputedStyle(scroller)
	const scrolls = (overflow: string) => page || overflow === 'auto' || overflow === 'scroll'
	return {
		x: scroller.scrollWidth > scroller.clientWidth && scrolls(style.overflowX) ? edgeSpeed(point.x, bounds.left, bounds.right) : 0,
		y: scroller.scrollHeight > scroller.clientHeight && scrolls(style.overflowY) ? edgeSpeed(point.y, bounds.top, bounds.bottom) : 0,
	}
}

/** The scroll speed along one axis: nothing in the middle, rising to {@link autoscrollStepPx} at either edge. */
function edgeSpeed(at: number, start: number, end: number): number {
	const zone = Math.min(autoscrollEdgePx, (end - start) / 4)
	if (zone <= 0) {
		return 0
	}

	if (at < start + zone) {
		return -autoscrollStepPx * Math.min(1, (start + zone - at) / zone)
	}

	if (at > end - zone) {
		return autoscrollStepPx * Math.min(1, (at - end + zone) / zone)
	}

	return 0
}

/** A light tick, where the device can and the page may (a vibration needs the user to have interacted). */
function haptic(milliseconds: number) {
	try {
		if (navigator.userActivation?.hasBeenActive ?? true) {
			navigator.vibrate?.(milliseconds)
		}
	}
	catch {
		// No vibration here.
	}
}

/**
 * The element directive behind {@link TransferController.draggable}. Wires `dragstart`/`dragend` once per
 * element and re-reads which item (and controller) it carries on every render.
 */
class DraggableDirective extends AsyncDirective {
	private element?: HTMLElement
	private controller?: AnyTransferController
	private item?: unknown
	private image?: () => Element | null | undefined

	public constructor(partInfo: PartInfo) {
		super(partInfo)
		if (partInfo.type !== PartType.ELEMENT) {
			throw new Error('transfer.draggable() can only be applied to an element.')
		}
	}

	public override render(_controller: AnyTransferController, _item: unknown, _image?: () => Element | null | undefined) {
		return noChange
	}

	public override update(part: ElementPart, [controller, item, image]: [AnyTransferController, unknown, (() => Element | null | undefined)?]) {
		this.controller = controller
		this.item = item
		this.image = image
		if (this.element !== part.element) {
			this.detach()
			this.element = part.element as HTMLElement
			this.attach()
		}
		return noChange
	}

	protected override disconnected(): void {
		this.detach()
	}

	protected override reconnected(): void {
		this.attach()
	}

	private attach(): void {
		const element = this.element
		if (!element) {
			return
		}

		element.draggable = true
		// No callout over a long-pressed item (iOS): the long press is the carry's, and its menu the item's own.
		element.style.setProperty('-webkit-touch-callout', 'none')
		element.addEventListener('dragstart', this.onDragStart)
		element.addEventListener('dragend', this.onDragEnd)
		element.addEventListener('pointerdown', this.onPointerDown)
		// Non-passive, and on the element: a touch's moves all go to where it started, and only a listener the
		// platform knows of from the touch's start can keep the page from panning out from under a lifted item.
		element.addEventListener('touchmove', this.onTouchMove, { passive: false })
	}

	private detach(): void {
		const element = this.element
		if (!element) {
			return
		}

		element.removeEventListener('dragstart', this.onDragStart)
		element.removeEventListener('dragend', this.onDragEnd)
		element.removeEventListener('pointerdown', this.onPointerDown)
		element.removeEventListener('touchmove', this.onTouchMove)
		element.style.removeProperty('-webkit-touch-callout')
		element.removeAttribute('draggable')
	}

	/** A finger carries through the controller's touch carry; a mouse and a pen through the platform's drag. */
	private readonly onPointerDown = (event: PointerEvent): void => {
		if (event.pointerType === 'touch' && this.controller && this.element) {
			this.controller.pressTouch(this.item, this.element, event, this.image)
		}
	}

	private readonly onTouchMove = (event: TouchEvent): void => {
		if (this.element && TouchCarry.isHolding(this.element) && event.cancelable) {
			event.preventDefault()
		}
	}

	private readonly onDragStart = (event: DragEvent): void => {
		if (this.element && TouchCarry.isPressing(this.element)) {
			// The platform's own touch drag, where it has one: the carry already has this finger — and the long press.
			event.preventDefault()
			event.stopPropagation()
			return
		}

		if (!this.controller) {
			return
		}

		// A nested draggable (a handle within the row) starts its own drag; the outer row must not also start one.
		event.stopPropagation()
		this.controller.beginDrag(this.item, event)

		const image = this.image?.()
		if (image && event.dataTransfer && !event.defaultPrevented) {
			// Held where it was grabbed, so the image does not jump under the pointer as the drag starts.
			const bounds = image.getBoundingClientRect()
			event.dataTransfer.setDragImage(image, event.clientX - bounds.left, event.clientY - bounds.top)
		}
	}

	private readonly onDragEnd = (event: DragEvent): void => {
		event.stopPropagation()
		this.controller?.finishDrag()
	}
}

const draggableDirective = directive(DraggableDirective)
