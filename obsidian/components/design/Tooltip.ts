import { Component, component, css, eventListener, html, property, state } from "@a11d/lit"

/**
 * A hover/focus tooltip. Wraps a trigger (its default slot) and shows an overlay of information beside it.
 *
 * The overlay is a Popover-API element promoted to the top layer, so it is never clipped by an ancestor's
 * overflow or trapped under a stacking context, and it is positioned against the trigger's box in JavaScript
 * (no anchor-name collisions when many tooltips share a page). Give it plain text via the `text` property, or
 * rich markup through the `tooltip` slot.
 *
 * @slot - the trigger content the tooltip is attached to.
 * @slot tooltip - rich overlay content; overrides `text` when present.
 * @csspart tooltip - the overlay container.
 */
@component('p7t-tooltip')
export class Tooltip extends Component {
	/** Plain-text overlay content. Ignored when the `tooltip` slot is filled. */
	@property() text = ''
	/** Delay before the overlay appears on hover, in milliseconds. */
	@property({ type: Number }) showDelay = 150
	/** Suppresses the overlay entirely. */
	@property({ type: Boolean, reflect: true }) disabled = false

	@state() private open = false
	private showTimer?: number

	static override get styles() {
		return css`
			:host {
				display: inline-flex;
			}

			.tooltip {
				position: fixed;
				margin: 0;
				inset: auto;
				box-sizing: border-box;
				max-width: 24rem;
				padding: .55em .7em;
				border: 1px solid color-mix(in srgb, var(--text-normal) 18%, transparent);
				border-radius: 10px;
				background-color: var(--background-secondary, #1e1e1e);
				color: var(--text-normal);
				box-shadow: 0 8px 26px color-mix(in srgb, black 45%, transparent);
				font-family: var(--font-interface);
				/*
				 * Absolute (rem) size, not em: the trigger may be a large heading, and an em-relative tooltip would
				 * balloon to match it. rem pins the overlay to the app's root size so every tooltip reads the same.
				 * The padding/max-width below are em, now relative to this fixed rem base — so they scale with it, not
				 * with the trigger.
				 */
				font-size: .85rem;
				font-weight: 400;
				line-height: 1.35;
				pointer-events: none;
				overflow: clip;
				opacity: 0;
				transform: translateY(4px);
				transition: opacity .14s ease, transform .14s ease;
			}

			.tooltip:popover-open,
			.tooltip.open {
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
			<slot></slot>
			<div class='tooltip ${this.open ? 'open' : ''}' part='tooltip' popover='manual'>
				<slot name='tooltip'>${this.text}</slot>
			</div>
		`
	}

	private get overlay(): HTMLElement | null {
		return this.renderRoot.querySelector<HTMLElement>('.tooltip')
	}

	private get isEmpty(): boolean {
		return !this.text && this.querySelector('[slot="tooltip"]') === null
	}

	@eventListener('pointerenter')
	protected onPointerEnter() {
		if (this.disabled || this.isEmpty) return
		clearTimeout(this.showTimer)
		this.showTimer = window.setTimeout(() => this.reveal(), this.showDelay)
	}

	@eventListener('pointerleave')
	protected onPointerLeave() {
		this.conceal()
	}

	@eventListener('focusin')
	protected onFocusIn() {
		if (this.disabled || this.isEmpty) return
		this.reveal()
	}

	@eventListener('focusout')
	protected onFocusOut() {
		this.conceal()
	}

	public override disconnectedCallback() {
		super.disconnectedCallback()
		this.conceal()
	}

	private reveal() {
		clearTimeout(this.showTimer)
		if (this.open || this.disabled || this.isEmpty) return
		const popover = this.overlay
		if (!popover) return

		this.open = true
		try {
			popover.showPopover()
		}
		catch {
			// Popover unsupported or already open; the `.open` class still renders it.
		}

		this.reposition()
		window.addEventListener('scroll', this.reposition, true)
		window.addEventListener('resize', this.reposition)
	}

	private conceal() {
		clearTimeout(this.showTimer)
		window.removeEventListener('scroll', this.reposition, true)
		window.removeEventListener('resize', this.reposition)
		if (!this.open) return

		this.open = false
		const popover = this.overlay
		if (popover) {
			try {
				popover.hidePopover()
			}
			catch {
				// Already hidden.
			}
		}
	}

	/** Places the overlay above the trigger, flipping below when there is no room, clamped to the viewport. */
	private readonly reposition = () => {
		const popover = this.overlay
		if (!popover || !this.open) return

		const trigger = this.getBoundingClientRect()
		const overlay = popover.getBoundingClientRect()
		const gap = 8
		const margin = 8

		let top = trigger.top - overlay.height - gap
		if (top < margin) {
			top = trigger.bottom + gap
		}

		let left = trigger.left + (trigger.width - overlay.width) / 2
		left = Math.max(margin, Math.min(left, window.innerWidth - overlay.width - margin))

		popover.style.top = `${Math.round(top)}px`
		popover.style.left = `${Math.round(left)}px`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-tooltip': Tooltip
	}
}
