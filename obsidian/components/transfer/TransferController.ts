import { AsyncDirective, Controller, directive, noChange, PartType, type ElementPart, type PartInfo, type ReactiveElement } from '@a11d/lit'

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

type AnyTransferController = TransferController<any>

/** Every connected controller — what a drag start consults to flag its candidates. */
const connected = new Set<AnyTransferController>()

/** The drag in flight, if any. One at a time by nature of pointer drag. */
let active: TransferTransaction<any> | undefined

/**
 * Lets a component hand items to, and take items from, other components through drag and drop.
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
		const kind = this.kindOf(item)
		if (!kind) {
			console.warn(`PLAINTORCH: <${this.element.tagName.toLowerCase()}> offers an item for transfer but declares no kind for it.`)
			event.preventDefault()
			return
		}

		if (!event.dataTransfer) {
			event.preventDefault()
			return
		}

		if (active) {
			// A pointer carries one item at a time, so a drag still recorded as active is a stale one: its source row
			// was removed mid-drag, and Chromium then never fires the `dragend` that would have ended it.
			this.endDrag(active, 'cancelled')
		}

		const transaction: TransferTransaction<T> = { kind, item, outbound: this.outbound, source: this, state: 'dragging' }
		active = transaction
		event.dataTransfer.effectAllowed = transaction.outbound === 'move' ? 'move' : 'copy'
		// Only the kind rides on the native payload — enough for the drag to start everywhere, and under a type
		// nothing else reads, so a stray drop into an editor inserts nothing. The item itself never does: it is an
		// object handed over by reference within the document.
		event.dataTransfer.setData(dataTransferType, kind)

		for (const controller of connected) {
			controller.enter(transaction)
		}
		this.host.requestUpdate()
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
		transaction.target = this
		// The drag is over for everyone the moment it lands; only this host and the source stay involved, on
		// the transaction, until the acceptance settles.
		this.endDrag(transaction, 'accepting')
		void this.complete(transaction)
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
		element.addEventListener('dragstart', this.onDragStart)
		element.addEventListener('dragend', this.onDragEnd)
	}

	private detach(): void {
		const element = this.element
		if (!element) {
			return
		}

		element.removeEventListener('dragstart', this.onDragStart)
		element.removeEventListener('dragend', this.onDragEnd)
		element.removeAttribute('draggable')
	}

	private readonly onDragStart = (event: DragEvent): void => {
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
