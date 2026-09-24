import { Component, component, css, html, property, state } from '@a11d/lit'
import { IntervalController } from '@3mo/interval-controller'
import { PointerController } from '@3mo/pointer-controller'
import type { ToastKind } from '../../host'
import type { IconName } from '../../components/PleiadesIcon'

/** How long a toast of each kind stays up; the more a message matters, the longer it lingers. */
const durations: Record<ToastKind, number> = { info: 4_500, success: 4_500, warning: 7_000, error: 10_000 }

const kindIcons: Record<ToastKind, IconName> = {
	info: 'lucide:info',
	success: 'lucide:circle-check',
	warning: 'lucide:triangle-alert',
	error: 'lucide:circle-alert',
}

/** How often a toast counts down; also the resolution of its progress line. */
const tickMs = 200

/** At most this many toasts show at once; the oldest makes room for a new one. */
const maximumToasts = 5

/**
 * One message in the {@link ToastStack}: an icon and a colour for its kind, a line counting down its time, and a click
 * to dismiss it early. Hovering it holds the countdown, so a message being read does not vanish mid-sentence.
 */
@component('p7t-toast')
export class Toast extends Component {
	@property() message = ''
	@property({ reflect: true }) kind: ToastKind = 'info'
	@state() private elapsed = 0

	private readonly pointer = new PointerController(this)
	protected readonly countdown = new IntervalController(this, tickMs, () => this.tick())

	static override get styles() {
		return css`
			:host {
				--p7t-toast-accent: var(--interactive-accent);
				display: grid;
				grid-template-columns: auto 1fr;
				align-items: start;
				gap: 10px;
				position: relative;
				overflow: hidden;
				width: min(360px, calc(100vw - 32px));
				box-sizing: border-box;
				padding: 10px 14px 12px 12px;
				border: 1px solid var(--background-modifier-border);
				border-inline-start: 3px solid var(--p7t-toast-accent);
				border-radius: 8px;
				background: var(--background-secondary);
				color: var(--text-normal);
				font-family: var(--font-interface);
				font-size: 0.9em;
				line-height: 1.4;
				box-shadow: 0 6px 24px rgba(0, 0, 0, 0.35);
				cursor: pointer;
				pointer-events: auto;
				animation: enter 160ms ease-out;
			}

			:host([kind=success]) { --p7t-toast-accent: var(--color-green); }
			:host([kind=warning]) { --p7t-toast-accent: var(--color-yellow); }
			:host([kind=error]) { --p7t-toast-accent: var(--text-error); }

			p7t-icon {
				color: var(--p7t-toast-accent);
				margin-top: 1px;
			}

			.message {
				white-space: pre-wrap;
				overflow-wrap: anywhere;
			}

			.progress {
				position: absolute;
				inset-inline-start: 0;
				bottom: 0;
				height: 2px;
				background: var(--p7t-toast-accent);
				opacity: 0.6;
				transition: width ${tickMs}ms linear;
			}

			@keyframes enter {
				from { opacity: 0; transform: translateY(-6px); }
			}
		`
	}

	private get duration() {
		return durations[this.kind]
	}

	private tick() {
		if (this.pointer.hover) {
			return
		}

		this.elapsed += tickMs
		if (this.elapsed >= this.duration) {
			this.dismiss()
		}
	}

	/** Takes the toast away. */
	dismiss() {
		const stack = this.parentElement
		this.remove()
		if (stack instanceof ToastStack) {
			stack.settle()
		}
	}

	protected override get template() {
		const left = Math.max(0, 1 - this.elapsed / this.duration)
		return html`
			<p7t-icon icon=${kindIcons[this.kind]} @click=${() => this.dismiss()}></p7t-icon>
			<span class='message' role=${this.kind === 'error' ? 'alert' : 'status'} @click=${() => this.dismiss()}>${this.message}</span>
			<div class='progress' style='width: ${left * 100}%'></div>
		`
	}
}

/**
 * The stack toasts appear in: the top-right corner, newest at the bottom, as Obsidian shows its notices. One per
 * document, created on the first toast.
 *
 * It is a manual popover, raised to the top of the top layer on every new toast: a modal `<dialog>` lives in the top
 * layer, above anything a z-index can reach, and a message raised while one is open — a failed save in an editor —
 * must still be seen.
 */
@component('p7t-toast-stack')
export class ToastStack extends Component {
	static override get styles() {
		return css`
			:host {
				position: fixed;
				inset: 16px 16px auto auto;
				margin: 0;
				padding: 0;
				border: none;
				background: transparent;
				overflow: visible;
				display: flex;
				flex-direction: column;
				align-items: flex-end;
				gap: 8px;
				pointer-events: none;
			}
		`
	}

	override connectedCallback() {
		super.connectedCallback()
		this.popover = 'manual'
	}

	protected override get template() {
		return html`<slot></slot>`
	}

	/** Raises the stack above whatever took the top layer since, or hides it once the last toast is gone. */
	settle() {
		if (this.matches(':popover-open')) {
			this.hidePopover()
		}

		if (this.childElementCount > 0) {
			this.showPopover()
		}
	}

	/** Shows a message in the document's stack. */
	static show(message: string, kind: ToastKind = 'info'): Toast {
		const stack = document.querySelector('p7t-toast-stack') ?? document.body.appendChild(new ToastStack())
		while (stack.children.length >= maximumToasts) {
			stack.firstElementChild?.remove()
		}

		const toast = new Toast()
		toast.message = message
		toast.kind = kind
		stack.appendChild(toast)
		stack.settle()
		return toast
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-toast': Toast
		'p7t-toast-stack': ToastStack
	}
}
