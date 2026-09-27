import { ModalBase } from '../../host'
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
export class OnrushDetailModal extends ModalBase {
	public constructor(private readonly sprint: OnrushSprint) {
		super()
	}

	public override onOpen(): void {
		this.setTitle(this.sprint.title)

		const banner = document.createElement('p7t-full-banner') as FullBannerElement
		banner.classList.add('plaintorch-modal-content')
		banner.xtype = 'OnrushSprint'
		banner.puck = this.sprint.id
		this.contentEl.appendChild(banner)
	}
}
