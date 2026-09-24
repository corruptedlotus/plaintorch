import { ModalBase } from '../../host'
import './PreferencesPanel'

/**
 * Hosts the {@link PreferencesPanel} in a modal (PEP116), opened from the briefing's settings button. Mirrors the
 * other entity modals: a {@link ModalBase} whose content is a single lit element.
 */
export class PreferenceModal extends ModalBase {
	public override onOpen(): void {
		this.setTitle('PLAINTORCH settings')

		const panel = document.createElement('p7t-preferences')
		panel.classList.add('plaintorch-modal-content')
		this.contentEl.appendChild(panel)
	}
}
