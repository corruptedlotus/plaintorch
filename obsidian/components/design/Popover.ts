import { Component, component, css, html, property, query, state } from "@a11d/lit"

/**
 * An interactive click-to-open popover. Wraps a trigger (its default slot) and reveals an overlay panel of rich,
 * *interactive* content (its `content` slot) beside it — unlike {@link Tooltip}, whose overlay is inert and hover-only.
 *
 * The panel is a Popover-API element promoted to the top layer, so it is never clipped by an ancestor's overflow or
 * trapped under a stacking context, and it is positioned against the trigger in JavaScript (no anchor-name collisions
 * when many popovers share a page). It uses `popover='manual'` and implements its own light-dismiss — an outside
 * pointer-down or Escape closes it — so a click on the trigger toggles cleanly instead of racing the browser's
 * automatic dismissal. Clicks *inside* the panel keep it open, so it can hold buttons and other controls.
 *
 * @slot - the trigger content the popover is attached to.
 * @slot content - the interactive overlay content.
 * @fires p7t-popover-toggle - a `CustomEvent<{ open: boolean }>` whenever the panel opens or closes.
 * @csspart panel - the overlay container.
 */
@component('p7t-popover')
export class Popover extends Component {
	/** Preferred side to open on; it flips to the other side when the preferred one lacks room. */
	@property() placement: 'top' | 'bottom' = 'bottom'
	/** Suppresses opening entirely. */
	@property({ type: Boolean, reflect: true }) disabled = false

	@state() private open = false
	@query('.panel') private readonly panel!: HTMLElement

	static override get styles() {
		return css`
			:host { display: inline-flex; }

			.trigger { display: inline-flex; align-items: center; cursor: pointer; }

			.panel {
				position: fixed;
				margin: 0;
				inset: auto;
				box-sizing: border-box;
				max-width: min(92vw, 26rem);
				max-height: min(70vh, 32rem);
				overflow: auto;
				padding: .6rem .7rem;
				border: 1px solid color-mix(in srgb, var(--text-normal) 18%, transparent);
				border-radius: 12px;
				background-color: var(--background-secondary, #1e1e1e);
				color: var(--text-normal);
				box-shadow: 0 10px 30px color-mix(in srgb, black 48%, transparent);
				font-family: var(--font-interface);
				font-size: .85rem;
				line-height: 1.4;
				pointer-events: auto;
				opacity: 0;
				transform: translateY(4px);
				transition: opacity .14s ease, transform .14s ease;
			}

			.panel:popover-open,
			.panel.open {
				opacity: 1;
				transform: translateY(0);

				@starting-style {
					opacity: 0;
					transform: translateY(4px);
				}
			}
		`
	}

	protected override get template() {
		return html`
			<div class='trigger' @click=${() => this.toggleOpen()}><slot></slot></div>
			<div class='panel ${this.open ? 'open' : ''}' part='panel' popover='manual'>
				<slot name='content'></slot>
			</div>
		`
	}

	/** Toggles the panel. */
	public toggleOpen() {
		if (this.open) {
			this.conceal()
		}
		else {
			this.reveal()
		}
	}

	/** Opens the panel, positions it, and begins listening for a light-dismiss. */
	public reveal() {
		if (this.open || this.disabled) return
		this.open = true
		try {
			this.panel.showPopover()
		}
		catch {
			// Popover unsupported or already open; the `.open` class still renders it.
		}

		this.reposition()
		window.addEventListener('scroll', this.reposition, true)
		window.addEventListener('resize', this.reposition)
		document.addEventListener('pointerdown', this.onDocumentPointerDown, true)
		document.addEventListener('keydown', this.onKeyDown, true)
		this.dispatchEvent(new CustomEvent('p7t-popover-toggle', { detail: { open: true }, bubbles: true, composed: true }))
	}

	/** Closes the panel and stops listening. */
	public conceal() {
		if (!this.open) return
		this.open = false
		try {
			this.panel.hidePopover()
		}
		catch {
			// Already hidden.
		}

		window.removeEventListener('scroll', this.reposition, true)
		window.removeEventListener('resize', this.reposition)
		document.removeEventListener('pointerdown', this.onDocumentPointerDown, true)
		document.removeEventListener('keydown', this.onKeyDown, true)
		this.dispatchEvent(new CustomEvent('p7t-popover-toggle', { detail: { open: false }, bubbles: true, composed: true }))
	}

	public override disconnectedCallback() {
		super.disconnectedCallback()
		this.conceal()
	}

	// A pointer-down anywhere outside the trigger (this host) and the panel light-dismisses the popover. A press on the
	// trigger stays inside `this`, so its own click handler does the toggle without the outside handler interfering.
	private readonly onDocumentPointerDown = (event: PointerEvent) => {
		const path = event.composedPath()
		if (path.includes(this) || path.includes(this.panel)) return
		this.conceal()
	}

	private readonly onKeyDown = (event: KeyboardEvent) => {
		if (event.key === 'Escape') {
			event.stopPropagation()
			this.conceal()
		}
	}

	/** Places the panel on the preferred side of the trigger, flipping when there is no room, clamped to the viewport. */
	private readonly reposition = () => {
		if (!this.open) return
		const trigger = this.getBoundingClientRect()
		const overlay = this.panel.getBoundingClientRect()
		const gap = 8
		const margin = 8

		const above = trigger.top - overlay.height - gap
		const below = trigger.bottom + gap
		let top = this.placement === 'top' ? above : below
		if (this.placement === 'top' && top < margin) {
			top = below
		}
		else if (this.placement === 'bottom' && below + overlay.height + margin > window.innerHeight) {
			top = Math.max(margin, above)
		}

		let left = trigger.left + (trigger.width - overlay.width) / 2
		left = Math.max(margin, Math.min(left, window.innerWidth - overlay.width - margin))

		this.panel.style.top = `${Math.round(top)}px`
		this.panel.style.left = `${Math.round(left)}px`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-popover': Popover
	}
}
