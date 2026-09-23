import { createDeferredExecutor, DeferredPromiseExecutor } from '@open-draft/deferred-promise'
import { Modal, Setting } from 'obsidian'
import { getApp } from '.'

/**
 * Asks for a single line of text.
 *
 * Follows the same deferred-promise shape as the select modals, so a caller awaits a value instead of
 * threading callbacks through the component that opened it.
 */
export class PromptTextModal extends Modal {
	protected dpe?: DeferredPromiseExecutor<string | undefined>
	private value = ''
	private settled = false

	/**
	 * Opens the prompt. Resolves with the entered text, or rejects when dismissed without confirming.
	 */
	static prompt(heading: string, placeholder = '', initial = '') {
		const modal = new PromptTextModal(getApp())
		modal.dpe = createDeferredExecutor()
		modal.heading = heading
		modal.placeholder = placeholder
		modal.value = initial
		modal.open()
		return new Promise<string | undefined>(modal.dpe)
	}

	private heading = ''
	private placeholder = ''

	override onOpen() {
		this.titleEl.setText(this.heading)

		new Setting(this.contentEl)
			.addText(text => {
				text.setPlaceholder(this.placeholder)
				text.setValue(this.value)
				text.onChange(value => this.value = value)
				text.inputEl.addEventListener('keydown', (e: KeyboardEvent) => {
					if (e.key === 'Enter') {
						e.preventDefault()
						this.confirm()
					}
				})
				window.setTimeout(() => text.inputEl.focus(), 0)
			})

		new Setting(this.contentEl)
			.addButton(button => button
				.setButtonText('Create')
				.setCta()
				.onClick(() => this.confirm()))
			.addButton(button => button
				.setButtonText('Cancel')
				.onClick(() => this.close()))
	}

	override onClose() {
		this.contentEl.empty()
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
