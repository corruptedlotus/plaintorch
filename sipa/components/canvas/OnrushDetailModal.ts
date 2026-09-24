import { App, Modal } from 'obsidian'
import type { OnrushSprint } from '@pleiades/sdk'

/** The full-banner properties this modal sets; it resolves and observes everything else from the PUCK. */
interface FullBannerElement extends HTMLElement {
	puck?: string
	xtype?: string
}

/**
 * Views and edits one onrush through the full banner (PEP102.5).
 *
 * The whole surface — the onrush's own banner, its entity actions, and its executive orders listed and edited beneath —
 * is composed by `<p7t-full-banner>`, which used to be assembled here by hand. Handed the type and PUCK it asks the
 * onrush repository for the sprint directly and renders the rest.
 */
export class OnrushDetailModal extends Modal {
	public constructor(app: App, private readonly sprint: OnrushSprint) {
		super(app)
	}

	public override onOpen(): void {
		this.titleEl.setText(this.sprint.title)
		this.contentEl.addClass('plaintorch-root')

		const banner = document.createElement('p7t-full-banner') as FullBannerElement
		banner.addClass('plaintorch-modal-content')
		banner.xtype = 'OnrushSprint'
		banner.puck = this.sprint.id
		this.contentEl.appendChild(banner)
	}

	public override onClose(): void {
		this.contentEl.empty()
	}
}
