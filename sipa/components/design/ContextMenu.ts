import { Component, component, css, html, nothing, property, query, state } from '@a11d/lit'
import { NavigabilityController } from '@3mo/navigability'
import { PopoverPlacement, type PopoverCoordinates } from '@3mo/popover'
import { IconName } from '../PleiadesIcon'
import { confirmAction, type ConfirmationOptions } from './ConfirmationBubble'

/** A clickable menu row. */
export interface ContextMenuAction {
	label: string
	icon?: IconName
	/**
	 * The action to perform, handed the viewport point the menu was opened at: by then the menu is gone, and that
	 * point is where a follow-up question can still be asked. Its return is ignored; when it is async the menu still
	 * awaits it before it is gone. A row inside a submenu gets the point the outermost menu was opened at.
	 */
	run: (origin: PopoverCoordinates) => void
	/**
	 * Asks before running — a confirmation bubble beside the row, the menu staying open until it is answered. Opens to
	 * the row's side unless the options place it otherwise.
	 */
	confirm?: ConfirmationOptions
	/** Shown in the error colour, for a destructive action. */
	danger?: boolean
	/** Rendered with a selected accent, for a toggle among mutually exclusive options. */
	pressed?: boolean
	disabled?: boolean
}

/**
 * A row that opens a nested list of entries beside it, for a group of choices that would crowd the menu (what can be
 * created under a directive, say).
 *
 * A mouse opens it by resting on the row, and resting on another row closes it again. A click opens it too, and a tap
 * toggles it, since touch has no hover. From the keyboard, Enter, Space or the right arrow open it on its first row,
 * and the left arrow or Escape close it and return to the row. Choosing a row inside it closes the whole menu.
 */
export interface ContextMenuSubmenu {
	label: string
	icon?: IconName
	/** What the nested list holds: actions, separators, notes, and submenus of their own. */
	entries: ContextMenuEntry[]
	/** Drawn but inert. A submenu with no entries is drawn disabled too, since it has nothing to open. */
	disabled?: boolean
}

/** A thin divider between groups. */
export interface ContextMenuSeparator { separator: true }

/** An inert explanatory line, for when a row is withheld and the reason is worth saying. */
export interface ContextMenuNote { note: string }

export type ContextMenuEntry = ContextMenuAction | ContextMenuSubmenu | ContextMenuSeparator | ContextMenuNote

/** A whole menu: an optional title over a list of entries. */
export interface ContextMenuSpec {
	title?: string
	entries: ContextMenuEntry[]
}

/** How a menu is being opened. */
export interface ContextMenuOpenOptions {
	/**
	 * Opened from the keyboard (the context-menu key, Shift+F10). The focus lands on the first row rather than on the
	 * menu itself, so Enter, the arrows and typing act at once.
	 */
	keyboard?: boolean
}

const isSeparator = (entry: ContextMenuEntry): entry is ContextMenuSeparator => 'separator' in entry
const isNote = (entry: ContextMenuEntry): entry is ContextMenuNote => 'note' in entry
const isSubmenu = (entry: ContextMenuEntry): entry is ContextMenuSubmenu => 'entries' in entry

/** Whether a row is drawn inert: withheld outright, or a submenu with nothing in it. */
const isInert = (entry: ContextMenuAction | ContextMenuSubmenu): boolean =>
	!!entry.disabled || (isSubmenu(entry) && entry.entries.length === 0)

/** Whether an entry is a row that can be chosen or opened now, which is what the arrow keys step through. */
const isChoosable = (entry: ContextMenuEntry): boolean => !isSeparator(entry) && !isNote(entry) && !isInert(entry)

/** How long a mouse rests on a submenu's row before the submenu opens. */
const openDelay = 150
/**
 * How long a mouse rests on another row before an open submenu closes. It is longer than the opening delay, so a
 * pointer crossing a row on its way into the submenu does not close it.
 */
const closeDelay = 300

/**
 * A reusable right-click / context menu — originally the dependency canvas's, now shared so any surface can raise
 * one (an entity item, a grid row, the canvas).
 *
 * It renders as a native `popover`, so it sits in the top layer — never clipped by whatever it was opened inside —
 * and light-dismisses on an outside click or Escape for free. Open it imperatively through {@link ContextMenu.open}:
 * it mounts a transient menu to the document, positions it at the pointer within the viewport, closes it after an
 * action runs, and removes itself on dismissal. A row carrying `confirm` asks in a confirmation bubble beside it
 * before running.
 *
 * Opening takes the focus into the menu, and closing gives it back to wherever it was while it is still inside. The
 * arrow keys, Home and End step through the rows (3MO's `NavigabilityController`, as the pickers use), typing jumps
 * to a row by its label, and Tab closes the menu.
 *
 * A {@link ContextMenuSubmenu} row opens a nested `p7t-context-menu` drawn inside this one, so its list looks and
 * behaves like the menu around it. The nested list is a `manual` popover, so the browser never closes it by itself.
 * Inside the outer menu's light dismissal, a press on it counts as inside, and a press anywhere outside still closes
 * everything. This menu closes the nested list when another row takes the pointer, when another row is pressed, and
 * on the keys that leave it.
 */
@component('p7t-context-menu')
export class ContextMenu extends Component {
	@property({ type: Object }) spec?: ContextMenuSpec

	/**
	 * The menu a submenu opens from; absent on the outermost menu. A submenu is drawn inside that menu, opened and
	 * closed by it, and hands a chosen row to the outermost menu to close and run.
	 */
	parentMenu?: ContextMenu

	/** The position, among the entries, of the submenu row whose list is open. */
	@state() private branch?: number

	@query('.menu') private readonly panel!: HTMLElement

	/** The viewport point the menu was opened at, handed to the row that runs. */
	private origin: PopoverCoordinates = [0, 0]
	/** Whether the panel is showing, as its own toggle events last reported. */
	private shown = false
	/** Where the focus was before the menu took it. */
	private returnFocusTo?: HTMLElement
	/** A submenu due to open (with its position) or the open one due to close (without), once a mouse has rested. */
	private hoverIntent?: { timer: ReturnType<typeof setTimeout>, index?: number }
	/** The kind of pointer that pressed a submenu row last: a click from a mouse opens the row, any other toggles it. */
	private pressedWith = ''

	protected readonly navigability = new NavigabilityController<ContextMenuEntry>(this, () => {
		const menu = this
		return {
			get items() { return menu.spec?.entries ?? [] },
			isNavigable: isChoosable,
			wrap: true,
			typeahead: true,
			handleKeyDown: (event: KeyboardEvent) => menu.handleKey(event)
		}
	})

	/**
	 * Opens a menu at a viewport point. Mounts a transient instance to the document body — so it escapes any
	 * clipping or stacking context of the opener — and disposes of it on dismissal.
	 */
	static open(clientX: number, clientY: number, spec: ContextMenuSpec, options: ContextMenuOpenOptions = {}): ContextMenu {
		const menu = document.createElement('p7t-context-menu') as ContextMenu
		menu.spec = spec
		document.body.appendChild(menu)
		void menu.openAt(clientX, clientY, options)
		return menu
	}

	static override get styles() {
		return css`
			:host {
				display: contents;
			}

			/* A popover, so the menu renders in the top layer and is not clipped by whatever raised it. */
			.menu {
				position: fixed;
				margin: 0;
				padding: .3em;
				border-radius: 10px;
				border: 1px solid var(--background-modifier-border, color-mix(in srgb, var(--text-normal) 20%, transparent));
				background-color: var(--background-secondary, #2b2b2b);
				color: var(--text-normal);
				box-shadow: 0 6px 24px rgb(0 0 0 / .28);
				font-family: var(--font-interface);
				min-width: 13em;
			}

			/* The panel takes the focus only to hand it to a row; the rows show where it is. */
			.menu:focus {
				outline: none;
			}

			.menu-title {
				padding: .35em .6em;
				opacity: .55;
				font-size: .8em;
			}

			.menu-item {
				display: flex;
				align-items: center;
				gap: .5em;
				width: 100%;
				padding: .4em .6em;
				border: none;
				border-radius: 7px;
				background: transparent;
				color: inherit;
				font-family: inherit;
				font-size: .95em;
				text-align: start;
				cursor: pointer;
			}

			.menu-item:hover,
			.menu-item:focus-visible,
			.menu-item[aria-expanded='true'] {
				background-color: color-mix(in srgb, var(--text-normal) 10%, transparent);
			}

			.menu-item:focus-visible {
				outline: none;
				box-shadow: inset 0 0 0 1px color-mix(in srgb, var(--interactive-accent, currentColor) 70%, transparent);
			}

			.menu-item[aria-checked='true'] {
				color: var(--p7t-flare-accent, var(--interactive-accent));
			}

			.menu-item.danger:hover,
			.menu-item.danger:focus-visible {
				background-color: color-mix(in srgb, var(--text-error, crimson) 14%, transparent);
			}

			.menu-item.danger {
				color: var(--text-error, crimson);
			}

			.menu-item:disabled {
				opacity: .4;
				cursor: default;
			}

			.menu-item p7t-icon {
				width: 1.1em;
				height: 1.1em;
				opacity: .85;
			}

			/* The submenu's mark, pushed to the row's far end. */
			.menu-item .chevron {
				width: .95em;
				height: .95em;
				margin-inline-start: auto;
				opacity: .55;
			}

			.menu-item .chevron:dir(rtl) {
				transform: scaleX(-1);
			}

			/* A finger needs more room than a pointer. */
			@media (pointer: coarse) {
				.menu-item {
					padding-block: .6em;
				}
			}

			.menu-separator {
				height: 1px;
				margin: .25em .3em;
				background-color: color-mix(in srgb, var(--text-normal) 12%, transparent);
			}

			.menu-note {
				padding: .4em .6em;
				opacity: .6;
				font-size: .85em;
				line-height: 1.25;
			}
		`
	}

	protected override get template() {
		const spec = this.spec
		if (!spec) {
			return html``
		}

		return html`
			<div class='menu' role='menu' tabindex='-1' aria-label=${spec.title ?? nothing}
				popover=${this.parentMenu ? 'manual' : 'auto'}
				@beforetoggle=${(e: Event) => this.onToggle(e)}
				@pointerenter=${() => this.parentMenu?.holdBranch()}>
				${!spec.title ? nothing : html`<div class='menu-title'>${spec.title}</div>`}
				${spec.entries.map((entry, index) => this.entryTemplate(entry, index))}
			</div>
		`
	}

	private entryTemplate(entry: ContextMenuEntry, index: number) {
		if (isSeparator(entry)) {
			return html`<div class='menu-separator' role='separator'></div>`
		}

		if (isNote(entry)) {
			return html`<div class='menu-note'>${entry.note}</div>`
		}

		if (isSubmenu(entry)) {
			return this.submenuTemplate(entry, index)
		}

		return html`
			<button
				class='menu-item ${entry.danger ? 'danger' : ''}'
				role=${entry.pressed === undefined ? 'menuitem' : 'menuitemradio'}
				aria-checked=${entry.pressed ?? nothing}
				?disabled=${entry.disabled}
				${this.navigability.item({ index, data: entry, disabled: !!entry.disabled })}
				@pointerenter=${(e: PointerEvent) => this.onRowEnter(e, index)}
				@click=${(e: Event) => void this.choose(entry, e)}>
				${!entry.icon ? nothing : html`<p7t-icon icon=${entry.icon}></p7t-icon>`}
				<span>${entry.label}</span>
			</button>
		`
	}

	/** A submenu's row, and the nested menu it opens, drawn closed until the row opens it. */
	private submenuTemplate(entry: ContextMenuSubmenu, index: number) {
		const inert = isInert(entry)
		return html`
			<button
				class='menu-item branch'
				role='menuitem'
				aria-haspopup='menu'
				aria-expanded=${this.branch === index ? 'true' : 'false'}
				data-branch=${index}
				?disabled=${inert}
				${this.navigability.item({ index, data: entry, disabled: inert })}
				@pointerdown=${(e: PointerEvent) => this.pressedWith = e.pointerType}
				@pointerenter=${(e: PointerEvent) => this.onRowEnter(e, index)}
				@pointerleave=${() => this.onRowLeave(index)}
				@click=${(e: MouseEvent) => void this.onSubmenuClick(e, index)}>
				${!entry.icon ? nothing : html`<p7t-icon icon=${entry.icon}></p7t-icon>`}
				<span>${entry.label}</span>
				<p7t-icon class='chevron' icon='lucide:chevron-right'></p7t-icon>
			</button>
			${inert ? nothing : html`<p7t-context-menu data-branch=${index} .parentMenu=${this} .spec=${entry}></p7t-context-menu>`}
		`
	}

	override disconnectedCallback() {
		super.disconnectedCallback()
		// A submenu due to open after a hover must not open once the menu is gone.
		this.clearHoverIntent()
	}

	/** The outermost menu: the one opened at the pointer, which runs whatever row is chosen in it or its submenus. */
	private get rootMenu(): ContextMenu {
		return this.parentMenu?.rootMenu ?? this
	}

	/**
	 * Asks first when the entry wants it — the menu stays open meanwhile, and a declined row leaves it open for
	 * another choice — then closes, then runs, so a slow action never leaves the menu hanging over the surface it
	 * acted on. A submenu open beside the row closes first, so it never covers the question.
	 */
	private async choose(entry: ContextMenuAction, event: Event) {
		if (entry.disabled) {
			return
		}

		this.closeBranch()
		if (entry.confirm && !await confirmAction(event, { placement: PopoverPlacement.InlineEnd, ...entry.confirm })) {
			return
		}

		const root = this.rootMenu
		root.dismiss()
		await entry.run(root.origin)
	}

	/** Closes the whole menu, submenus and all. The menu removes itself once closed. */
	dismiss() {
		const root = this.rootMenu
		if (root.shown) {
			root.panel.hidePopover()
		}
	}

	/** Shows the popover and places it at the pointer, flipping above and clamping to the viewport as needed. */
	async openAt(clientX: number, clientY: number, options: ContextMenuOpenOptions = {}) {
		this.origin = [clientX, clientY]
		const focused = deepActiveElement()
		this.returnFocusTo = focused instanceof HTMLElement && focused !== document.body ? focused : undefined
		// The size to place against is the size of the content just assigned, which is not laid out until the
		// update it triggered has run.
		await this.updateComplete
		const panel = this.panel
		panel.showPopover()
		const margin = 8
		const width = panel.offsetWidth
		const height = panel.offsetHeight
		panel.style.left = `${Math.max(margin, Math.min(clientX, window.innerWidth - width - margin))}px`
		panel.style.top = `${clientY + height + margin > window.innerHeight ? Math.max(margin, clientY - height - margin) : clientY + margin}px`
		this.takeFocus(!!options.keyboard)
	}

	/**
	 * Shows this submenu beside the row that opens it. It goes past the parent menu's trailing edge, or its leading edge
	 * when the trailing side has no room, with its first row level with the opening row. Where neither side has room (a
	 * phone held upright), it drops below the opening row instead, over the rest of the parent menu.
	 */
	private async openBeside(row: HTMLElement, frame: HTMLElement, focus: boolean) {
		await this.updateComplete
		const panel = this.panel
		if (!this.shown) {
			panel.showPopover()
		}

		const margin = 8
		// The submenu overlaps the parent's border a little, so the two read as one piece.
		const overlap = 4
		// Dropped below the row, it is indented so the parent's labels stay readable down its side.
		const indent = 16
		const width = panel.offsetWidth
		const height = panel.offsetHeight
		const rowBox = row.getBoundingClientRect()
		const frameBox = frame.getBoundingClientRect()
		const rtl = getComputedStyle(row).direction === 'rtl'
		const after = frameBox.right - overlap
		const before = frameBox.left - width + overlap
		const trailing = rtl ? before : after
		const leading = rtl ? after : before
		const fits = (left: number) => left >= margin && left + width <= window.innerWidth - margin

		let left: number
		let top: number
		if (fits(trailing) || fits(leading)) {
			left = fits(trailing) ? trailing : leading
			// Up by the panel's border and padding, so its first row and the opening row share a line.
			top = rowBox.top - panel.clientTop - (parseFloat(getComputedStyle(panel).paddingTop) || 0)
		}
		else {
			left = rtl ? rowBox.right - width - indent : rowBox.left + indent
			top = rowBox.bottom
		}

		panel.style.left = `${Math.max(margin, Math.min(left, window.innerWidth - width - margin))}px`
		panel.style.top = `${Math.max(margin, Math.min(top, window.innerHeight - height - margin))}px`
		if (focus) {
			this.takeFocus(true)
		}
	}

	/** Hides this submenu, and any submenu open inside it, handing the focus back to the parent menu if it held it. */
	private close() {
		this.closeBranch()
		const hadFocus = this.holdsFocus()
		if (this.shown) {
			this.panel.hidePopover()
		}

		if (hadFocus) {
			this.parentMenu?.panel.focus({ preventScroll: true })
		}
	}

	/** Takes the focus into the menu: onto the panel, where the keys arrive, and onto the first row when asked. */
	private takeFocus(firstRow: boolean) {
		this.panel.focus({ preventScroll: true })
		if (firstRow) {
			// Cleared first, so landing on the first row moves the focus even when the cursor was already there.
			this.navigability.clear()
			this.navigability.goFirst({ method: 'keyboard' })
		}
	}

	/** Opens the submenu at a position, closing any other open one first. */
	private async openBranch(index: number, focus = false) {
		this.clearHoverIntent()
		if (this.branch !== undefined && this.branch !== index) {
			this.closeBranch()
		}

		this.branch = index
		await this.updateComplete
		const row = this.rowAt(index)
		const submenu = this.submenuAt(index)
		// Closed again meanwhile (another row took the pointer, or the menu went), or not drawn: nothing to open.
		if (this.branch !== index || !this.isConnected || !row || !submenu) {
			return
		}

		await submenu.openBeside(row, this.panel, focus)
	}

	/** Closes the open submenu, if any; with `refocus`, the focus returns to its row, as the keys that leave it do. */
	private closeBranch(refocus = false) {
		this.clearHoverIntent()
		const index = this.branch
		if (index === undefined) {
			return
		}

		this.branch = undefined
		this.submenuAt(index)?.close()
		if (refocus) {
			this.rowAt(index)?.focus({ preventScroll: true })
		}
	}

	/**
	 * Keeps the open submenu open: called when the pointer arrives in it, so a close scheduled while it crossed another
	 * row on the way is called off, here and in every menu above.
	 */
	private holdBranch() {
		this.clearHoverIntent()
		this.parentMenu?.holdBranch()
	}

	private onRowEnter(event: PointerEvent, index: number) {
		// Only a mouse hovers. A touch or a pen reports entering on a tap, and the tap's click decides.
		if (event.pointerType !== 'mouse') {
			return
		}

		const entry = this.spec?.entries[index]
		if (entry && isSubmenu(entry) && !isInert(entry)) {
			if (this.branch === index) {
				this.clearHoverIntent()
			}
			else {
				this.intend(openDelay, index)
			}
		}
		else if (this.branch !== undefined) {
			this.intend(closeDelay)
		}
	}

	/** A pointer that leaves a submenu's row before the submenu opened calls the opening off. */
	private onRowLeave(index: number) {
		if (this.hoverIntent?.index === index) {
			this.clearHoverIntent()
		}
	}

	private async onSubmenuClick(event: MouseEvent, index: number) {
		// A click the keyboard raised (Enter, Space) carries no detail; it opens the submenu on its first row.
		const byKeyboard = event.detail === 0
		const toggles = !byKeyboard && this.pressedWith !== 'mouse'
		this.pressedWith = ''
		if (toggles && this.branch === index) {
			this.closeBranch()
			return
		}

		await this.openBranch(index, byKeyboard)
	}

	/** Schedules opening the submenu at a position, or, with none, closing the open one, replacing what was scheduled. */
	private intend(delay: number, index?: number) {
		this.clearHoverIntent()
		const timer = setTimeout(() => {
			this.hoverIntent = undefined
			if (index === undefined) {
				this.closeBranch()
			}
			else {
				void this.openBranch(index)
			}
		}, delay)
		this.hoverIntent = { timer, index }
	}

	private clearHoverIntent() {
		if (this.hoverIntent) {
			clearTimeout(this.hoverIntent.timer)
			this.hoverIntent = undefined
		}
	}

	/**
	 * The keys the rows' cursor leaves to the menu, read before the cursor's own handling. Returns whether the key was
	 * taken, which prevents its default.
	 *
	 * A key pressed inside a submenu reaches every menu around it, since the submenu is drawn inside them. Only the
	 * innermost menu acts on it.
	 */
	private handleKey(event: KeyboardEvent): boolean {
		if (event.composedPath().find(target => target instanceof ContextMenu) !== this) {
			return false
		}

		const rtl = getComputedStyle(this.panel).direction === 'rtl'
		const index = this.navigability.index
		const entry = index === undefined ? undefined : this.spec?.entries[index]
		switch (event.key) {
			case rtl ? 'ArrowLeft' : 'ArrowRight':
				if (index !== undefined && entry && isSubmenu(entry) && isChoosable(entry)) {
					void this.openBranch(index, true)
					return true
				}

				return false
			case rtl ? 'ArrowRight' : 'ArrowLeft':
				if (this.parentMenu) {
					this.parentMenu.closeBranch(true)
					return true
				}

				return false
			case 'Escape':
				// Taken (which also keeps the browser from closing the outer menu) and kept from whatever surrounds
				// the menu, so one Escape closes one submenu. With none open, the outermost menu is closed natively.
				if (this.branch !== undefined || this.parentMenu) {
					event.stopPropagation()
					if (this.branch !== undefined) {
						this.closeBranch(true)
					}
					else {
						this.parentMenu?.closeBranch(true)
					}

					return true
				}

				return false
			case 'Tab':
				// The menu closes and gives the focus back to its opener, from where Tab carries on as usual.
				this.dismiss()
				return false
			default:
				return false
		}
	}

	private onToggle(event: Event) {
		if (event.target !== event.currentTarget) {
			return
		}

		this.shown = (event as Event & { newState?: string }).newState === 'open'
		if (this.shown || this.parentMenu) {
			return
		}

		// Light-dismissed or closed after an action: the focus goes back where it was, if the menu still holds it (a
		// press outside has already moved it), and the transient instance has done its job.
		this.clearHoverIntent()
		if (this.holdsFocus()) {
			this.returnFocusTo?.focus({ preventScroll: true })
		}

		this.remove()
	}

	/** Whether the focus is inside this menu, or inside a submenu drawn in it. */
	private holdsFocus(): boolean {
		for (let node: Node | null = deepActiveElement(); node; node = node.parentNode instanceof ShadowRoot ? node.parentNode.host : node.parentNode) {
			if (node === this) {
				return true
			}
		}

		return false
	}

	private rowAt(index: number): HTMLElement | null {
		return this.renderRoot.querySelector<HTMLElement>(`button[data-branch='${index}']`)
	}

	private submenuAt(index: number): ContextMenu | null {
		return this.renderRoot.querySelector<ContextMenu>(`p7t-context-menu[data-branch='${index}']`)
	}
}

/** The focused element, looked for through open shadow roots. */
function deepActiveElement(): Element | null {
	let element = document.activeElement
	while (element?.shadowRoot?.activeElement) {
		element = element.shadowRoot.activeElement
	}

	return element
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-context-menu': ContextMenu
	}
}
