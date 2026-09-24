import { App, Modal } from 'obsidian'
import type { Directive } from '@pleiades/sdk'

/** The full-banner properties this modal sets; it resolves and observes everything else from the PUCK. */
interface FullBannerElement extends HTMLElement {
	puck?: string
	xtype?: string
}

/**
 * Views and edits one lunar directive through the full banner (PEP100 patch).
 *
 * The whole surface — the directive's own banner, its entity actions, and its timeframes listed and edited beneath —
 * is composed by `<p7t-full-banner>`, which used to be assembled here by hand. Handed the type and PUCK it asks the
 * directive repository for the directive directly and renders the rest.
 */
export class LunarDirectiveModal extends Modal {
	public constructor(app: App, private readonly directive: Directive) {
		super(app)
	}

	public override onOpen(): void {
		this.titleEl.setText(this.directive.title)
		this.contentEl.addClass('plaintorch-root')

		const banner = document.createElement('p7t-full-banner') as FullBannerElement
		banner.addClass('plaintorch-modal-content')
		banner.xtype = 'LunarDirective'
		banner.puck = this.directive.id
		this.contentEl.appendChild(banner)
	}

	public override onClose(): void {
		this.contentEl.empty()
	}
}
