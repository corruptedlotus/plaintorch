import { App, Modal } from 'obsidian'
import './PreferencesPanel'

/**
 * Hosts the {@link PreferencesPanel} in a modal (PEP116), opened from the briefing's settings button. Mirrors the
 * other entity modals: an Obsidian {@link Modal} whose content is a single lit element.
 */
export class PreferenceModal extends Modal {
	public constructor(app: App) {
		super(app)
	}

	public override onOpen(): void {
		this.titleEl.setText('PLAINTORCH settings')
		this.contentEl.addClass('plaintorch-root')

		const panel = document.createElement('p7t-preferences')
		panel.addClass('plaintorch-modal-content')
		this.contentEl.appendChild(panel)
	}

	public override onClose(): void {
		this.contentEl.empty()
	}
}
