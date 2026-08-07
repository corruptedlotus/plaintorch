import { App, Modal } from 'obsidian'
import type { OnrushSprint } from '@pleiades/sdk'

/** The onrush banner properties this modal sets. */
interface OnrushBannerElement extends HTMLElement {
	app?: App
	puck?: string
	entity?: OnrushSprint
}

/**
 * Views and edits one onrush through its own banner, with its executive orders listed and edited beneath
 * (PEP102.5).
 *
 * The banner resolves and observes the canonical sprint from its id, so a rename made here reaches every other
 * surface at once; the orders grid manages its own list. Together they are the onrush's detail window, opened
 * from the management tray on the graph.
 */
export class OnrushDetailModal extends Modal {
	public constructor(app: App, private readonly sprint: OnrushSprint) {
		super(app)
	}

	public override onOpen(): void {
		this.titleEl.setText(this.sprint.title)
		this.contentEl.addClass('plaintorch-root')

		const banner = document.createElement('p7t-onrush-banner') as OnrushBannerElement
		banner.addClass('plaintorch-modal-content')
		banner.app = this.app
		// The provided sprint shows immediately; the id resolves the canonical instance the banner then edits.
		banner.entity = this.sprint
		banner.puck = this.sprint.id
		this.contentEl.appendChild(banner)

		const orders = document.createElement('p7t-onrush-orders') as HTMLElement & { onrushId?: string }
		orders.onrushId = this.sprint.id
		this.contentEl.appendChild(orders)
	}

	public override onClose(): void {
		this.contentEl.empty()
	}
}
