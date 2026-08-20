import { App, Modal } from 'obsidian'
import type { Directive } from '@pleiades/sdk'

/** The lunar directive banner properties this modal sets. */
interface LunarDirectiveBannerElement extends HTMLElement {
	app?: App
	puck?: string
	entity?: Directive
}

/**
 * Views and edits one lunar directive through its own banner, with its timeframes listed and edited beneath
 * (PEP100 patch).
 *
 * Modelled on the onrush detail window: the banner resolves and observes the canonical directive from its id, so a
 * rename or status shift made here reaches every other surface at once, while the timeframes editor manages its own
 * list. Together they are the lunar directive's detail window, opened from its row in the entity grid.
 */
export class LunarDirectiveModal extends Modal {
	public constructor(app: App, private readonly directive: Directive) {
		super(app)
	}

	public override onOpen(): void {
		this.titleEl.setText(this.directive.title)
		this.contentEl.addClass('plaintorch-root')

		const banner = document.createElement('p7t-ldirective-banner') as LunarDirectiveBannerElement
		banner.addClass('plaintorch-modal-content')
		banner.app = this.app
		// The provided directive shows immediately; the id resolves the canonical instance the banner then edits.
		banner.entity = this.directive
		banner.puck = this.directive.id
		this.contentEl.appendChild(banner)

		const editor = document.createElement('p7t-timeframes-editor') as HTMLElement & { directiveId?: string }
		editor.directiveId = this.directive.id
		this.contentEl.appendChild(editor)
	}

	public override onClose(): void {
		this.contentEl.empty()
	}
}
