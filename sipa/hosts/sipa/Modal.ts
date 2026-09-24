import { Component, component, css, html, property, query } from '@a11d/lit'
import type { DialogShell, ModalView } from '../../host'
import { modalLayers, overlaySlot } from './modalLayers'

/** Whether a click landed on a dialog's backdrop rather than on the dialog box itself. */
export function isBackdropClick(dialog: HTMLDialogElement, event: MouseEvent): boolean {
	if (event.target !== dialog) {
		return false
	}

	const box = dialog.getBoundingClientRect()
	return event.clientX < box.left || event.clientX > box.right || event.clientY < box.top || event.clientY > box.bottom
}

/**
 * A modal dialog: a native `<dialog>` shown with `showModal()`, so it sits in the top layer with a backdrop, makes the
 * rest of the page inert, and stacks above an earlier one. Its content is its light DOM, slotted under a heading and a
 * close button. Escape, the close button and a click on the backdrop each ask to be dismissed (a `dismiss` event); the
 * {@link ModalShell} owning it decides. Whatever must stay interactive above it (the toasts) goes into its overlay slot.
 */
@component('p7t-modal')
export class Modal extends Component {
	@property() heading = ''
	@query('dialog') private readonly dialog?: HTMLDialogElement

	static override get styles() {
		return css`
			:host {
				display: contents;
			}

			dialog {
				box-sizing: border-box;
				width: min(var(--dialog-width, 560px), calc(100vw - 32px));
				max-height: calc(100vh - 64px);
				padding: 0;
				border: 1px solid var(--background-modifier-border);
				border-radius: var(--modal-radius, 12px);
				background: var(--background-primary);
				color: var(--text-normal);
				font-family: var(--font-interface);
				box-shadow: 0 16px 48px rgba(0, 0, 0, 0.45);
				overflow: hidden;
			}

			dialog[open] {
				display: flex;
				flex-direction: column;
			}

			dialog::backdrop {
				background: rgba(0, 0, 0, 0.55);
			}

			header {
				display: flex;
				align-items: center;
				gap: 8px;
				padding: 14px 12px 6px 20px;
			}

			.heading {
				flex: 1;
				margin: 0;
				font-size: 1.15em;
				font-weight: 600;
				line-height: 1.6;
			}

			.close {
				all: unset;
				display: inline-flex;
				padding: 4px;
				border-radius: 4px;
				color: var(--text-muted);
				cursor: pointer;
			}

			.close:hover, .close:focus-visible {
				background: var(--background-modifier-hover);
				color: var(--text-normal);
			}

			.content {
				padding: 4px 20px 20px;
				overflow: auto;
			}
		`
	}

	/** Shows the dialog once it has rendered. */
	async show() {
		await this.updateComplete
		if (this.isConnected && this.dialog && !this.dialog.open) {
			this.dialog.showModal()
			modalLayers.opened(this)
		}
	}

	/** Takes the dialog down (without asking). */
	hide() {
		this.dialog?.close()
		modalLayers.closed(this)
	}

	override disconnectedCallback() {
		super.disconnectedCallback()
		modalLayers.closed(this)
	}

	private requestDismiss() {
		this.dispatchEvent(new Event('dismiss'))
	}

	protected override get template() {
		return html`
			<dialog part='dialog' aria-label=${this.heading}
				@cancel=${(e: Event) => { e.preventDefault(); this.requestDismiss() }}
				@click=${(e: MouseEvent) => isBackdropClick(this.dialog!, e) && this.requestDismiss()}>
				<header part='header'>
					<h2 class='heading' part='heading'>${this.heading}</h2>
					<button class='close' aria-label='Close' @click=${() => this.requestDismiss()}>
						<p7t-icon icon='lucide:x'></p7t-icon>
					</button>
				</header>
				<div class='content' part='content'>
					<slot></slot>
				</div>
				<slot name=${overlaySlot}></slot>
			</dialog>
		`
	}
}

/**
 * Hosts a {@link ModalView} (a `ModalBase` dialog) in a {@link Modal}. The view's content element becomes the modal's
 * light DOM, so the host document's stylesheet reaches it; the modal is created on open and removed on close.
 */
export class ModalShell implements DialogShell {
	private element?: Modal

	constructor(private readonly view: ModalView) { }

	open(): void {
		if (this.element) {
			return
		}

		const element = new Modal()
		element.heading = this.view.title
		element.append(this.view.contentEl)
		element.addEventListener('dismiss', () => this.close())
		document.body.append(element)
		this.element = element
		// Like Obsidian's, the view draws while opening, before the dialog is on screen.
		this.view.handleOpen()
		void element.show()
	}

	close(): void {
		const element = this.element
		if (!element) {
			return
		}

		this.element = undefined
		element.hide()
		element.remove()
		this.view.handleClose()
		this.view.contentEl.remove()
	}

	setTitle(title: string): void {
		if (this.element) {
			this.element.heading = title
		}
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-modal': Modal
	}
}
