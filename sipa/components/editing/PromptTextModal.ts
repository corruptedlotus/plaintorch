import { createDeferredExecutor, DeferredPromiseExecutor } from '@open-draft/deferred-promise'
import { createChild, ModalBase } from '../../host'

/**
 * Asks for a single line of text.
 *
 * Follows the same deferred-promise shape as the select modals, so a caller awaits a value instead of
 * threading callbacks through the component that opened it.
 *
 * The content is plain light-DOM markup — an input and a row of buttons, the confirming one marked `mod-cta` — so it
 * takes the host's own form styling (Obsidian's, or the standalone shell's stylesheet).
 */
export class PromptTextModal extends ModalBase {
	protected dpe?: DeferredPromiseExecutor<string | undefined>
	private value = ''
	private settled = false

	/**
	 * Opens the prompt. Resolves with the entered text, or rejects when dismissed without confirming. `confirmLabel`
	 * names the confirming button.
	 */
	static prompt(heading: string, placeholder = '', initial = '', confirmLabel = 'Create') {
		const modal = new PromptTextModal()
		modal.dpe = createDeferredExecutor()
		modal.heading = heading
		modal.placeholder = placeholder
		modal.value = initial
		modal.confirmLabel = confirmLabel
		modal.open()
		return new Promise<string | undefined>(modal.dpe)
	}

	private heading = ''
	private placeholder = ''
	private confirmLabel = 'Create'

	override onOpen() {
		this.setTitle(this.heading)

		const input = createChild(this.contentEl, 'input')
		input.type = 'text'
		input.placeholder = this.placeholder
		input.value = this.value
		input.style.width = '100%'
		input.addEventListener('input', () => this.value = input.value)
		input.addEventListener('keydown', (e: KeyboardEvent) => {
			if (e.key === 'Enter') {
				e.preventDefault()
				this.confirm()
			}
		})
		window.setTimeout(() => input.focus(), 0)

		const buttons = createChild(this.contentEl, 'div')
		buttons.style.cssText = 'display: flex; justify-content: flex-end; gap: 0.5em; margin-top: 1em;'
		createChild(buttons, 'button', { text: this.confirmLabel, cls: 'mod-cta' })
			.addEventListener('click', () => this.confirm())
		createChild(buttons, 'button', { text: 'Cancel' })
			.addEventListener('click', () => this.close())
	}

	override onClose() {
		if (!this.settled) {
			this.dpe?.reject()
		}
	}

	private confirm() {
		const trimmed = this.value.trim()
		if (!trimmed) {
			return
		}

		this.settled = true
		this.dpe?.resolve(trimmed)
		this.close()
	}
}
