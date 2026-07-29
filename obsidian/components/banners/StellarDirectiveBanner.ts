import { component, css, html } from "@a11d/lit"
import { EntityBanner } from './EntityBanner'
import { Directive, DirectiveStatus, StellarDirectiveUpdate } from '@pleiades/sdk'
import { core, IconName, ReactiveBinder, SelectDirectiveStatusModal } from ".."
import { App } from "obsidian"

/**
 * Banner for a Stellar directive (PEP100) — the classic lifecycle-driven directive kind. It
 * carries the {@link DirectiveStatus} lifecycle and can hold scheduling dates.
 */
@component('p7t-sdirective-banner')
export class StellarDirectiveBanner extends EntityBanner<Directive> {
	override icon: IconName = 'directive'

	protected override get preHeadingTemplate() {
		return !this.entity?.codename ? html`
			<span>Stellar Directive</span>
		` : html`
			<span>Codename ${this.entity.codename.toUpperCase()}</span>
		`
	}

	protected override readonly entityTypeName = 'Directive' as const

	protected binder = new ReactiveBinder<Directive>(this, 'entity', {
		sourceUpdate: () => this.beginEntityEdit(),
		sourceUpdated: async (_, keyPath) => {
			const entity = this.entity!
			if (keyPath !== 'status' && keyPath !== 'title') {
				return
			}

			const saved = await this.commitEntityEdit(async () => {
				if (keyPath === 'status') {
					return await core.directives.shiftStellarWorkflow(entity.id, { status: entity.status as DirectiveStatus })
				}

				const update: StellarDirectiveUpdate = { title: entity.title }
				return await core.directives.updateStellar(entity.id, update)
			})

			if (saved && keyPath === 'title') {
				await this.revealAssociatedNote(entity.id)
			}
		}
	})

	private async revealAssociatedNote(directiveId: string) {
		const existence = await core.repos.entityResolution.refresh(directiveId)
		const app = (window as any).app as App
		if (!existence?.associatedNote
			|| app.workspace.activeEditor?.file?.path === existence.associatedNote) return

		const file = app.vault.getFileByPath(existence.associatedNote)!
		app.workspace.getLeaf(true).openFile(file)
	}

	static override get styles() {
		return css`
			${super.styles}

			:host {
				padding-inline: 1.2em;
			}

			:host::part(sub-heading) {
				font-weight: 300;
				font-size: .9em;
				margin-top: -.2em;
				opacity: 1;
			}

			p7t-status-item::part(icon) {
				height: 1.4em;
			}
		`
	}

	protected override get secondary() {
		const directiveTitle = this.entity!.parentDirective?.title
		return !directiveTitle ? html`
			<span style='opacity: .5'>Constellation Directive</span>
		` : html`
			<span>${directiveTitle}</span>
		`
	}

	protected override get headingTemplate() {
		return html`
			<p7t-editable-plaintext ${this.binder.bind('title')}></p7t-editable-plaintext>
		`
	}

	protected override get subHeadingTemplate() {
		return html`
			<p7t-editable .doEdit=${SelectDirectiveStatusModal.prompt} ${this.binder.bind('status')}>
				<p7t-status-item
					.status=${DirectiveStatus[this.entity!.status as DirectiveStatus] as keyof typeof DirectiveStatus}>
				</p7t-status-item>
			</p7t-editable>
		`
	}
}

declare global {
	interface HTMLTagNameMap {
		'p7t-sdirective-banner': StellarDirectiveBanner
	}
}
