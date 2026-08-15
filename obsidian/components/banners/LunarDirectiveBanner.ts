import { component, css, html } from "@a11d/lit"
import { DirectiveBanner } from './DirectiveBanner'
import { Directive, LunarDirectiveStatus, LunarDirectiveUpdate } from '@pleiades/sdk'
import { core, IconName, ReactiveBinder, SelectLunarDirectiveStatusModal } from ".."
import { App } from "obsidian"

/**
 * Banner for a Moonlight (lunar) directive (PEP100). Lunar directives are everglow: they
 * carry the {@link LunarDirectiveStatus} lifecycle (on hold / active / stale) rather than the
 * stellar one, and no scheduling dates.
 */
@component('p7t-ldirective-banner')
export class LunarDirectiveBanner extends DirectiveBanner {
	override icon: IconName = 'directive-lunar'

	protected override get preHeadingTemplate() {
		return !this.entity?.codename ? html`
			<span>Lunar Directive</span>
		` : html`
			<span>Codename ${this.entity.codename.toUpperCase()}</span>
		`
	}

	protected override readonly entityTypeName = 'LunarDirective' as const

	protected binder = new ReactiveBinder<Directive>(this, 'entity', {
		sourceUpdate: () => this.beginEntityEdit(),
		sourceUpdated: async (_, keyPath) => {
			const entity = this.entity!
			if (keyPath !== 'status' && keyPath !== 'title') {
				return
			}

			const saved = await this.commitEntityEdit(async () => {
				if (keyPath === 'status') {
					return await core.directives.shiftLunarWorkflow(entity.id, { status: entity.status as LunarDirectiveStatus })
				}

				const update: LunarDirectiveUpdate = { title: entity.title }
				return await core.directives.updateLunar(entity.id, update)
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
			<p7t-editable-plaintext required label='Title' placeholder='Untitled' ${this.binder.bind('title')}></p7t-editable-plaintext>
		`
	}

	protected override get subHeadingTemplate() {
		return html`
			<p7t-editable .doEdit=${SelectLunarDirectiveStatusModal.prompt} ${this.binder.bind('status')}>
				<p7t-status-item
					.status=${LunarDirectiveStatus[this.entity!.status as LunarDirectiveStatus] as keyof typeof LunarDirectiveStatus}>
				</p7t-status-item>
			</p7t-editable>
		`
	}
}

declare global {
	interface HTMLTagNameMap {
		'p7t-ldirective-banner': LunarDirectiveBanner
	}
}
