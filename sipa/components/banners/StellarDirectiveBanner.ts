import { component, css, html } from "@a11d/lit"
import { DirectiveBanner } from './DirectiveBanner'
import { Directive, DirectiveStatus, StellarDirectiveUpdate } from '@pleiades/sdk'
import { core, IconName, openNoteWhenReady, ReactiveBinder, SelectDirectiveStatusModal } from ".."
import { App } from "obsidian"

/**
 * Banner for a Stellar directive (PEP100) — the classic lifecycle-driven directive kind. It
 * carries the {@link DirectiveStatus} lifecycle and can hold scheduling dates.
 */
@component('p7t-sdirective-banner')
export class StellarDirectiveBanner extends DirectiveBanner {
	override icon: IconName = 'directive'

	protected override get preHeadingTemplate() {
		return html`
			<span>
				${this.entity?.codename ? 'Codename' : 'Stellar Directive ·'}
				<p7t-editable-plaintext class='codename' placeholder='-' ${this.binder.bind('codename')}></p7t-editable-plaintext>
			</span>
		`
	}

	protected override readonly entityTypeName = 'StellarDirective' as const

	protected binder = new ReactiveBinder<Directive>(this, 'entity', {
		sourceUpdate: () => this.beginEntityEdit(),
		sourceUpdated: async (_, keyPath) => {
			const entity = this.entity!
			if (keyPath !== 'status' && keyPath !== 'title' && keyPath !== 'codename' && keyPath !== 'due') {
				return
			}

			const saved = await this.commitEntityEdit(async () => {
				if (keyPath === 'status') {
					return await core.directives.shiftStellarWorkflow(entity.id, { status: entity.status as DirectiveStatus })
				}

				// Codename and due are nullable: a cleared field commits as null (a clear), not undefined (a no-op).
				const update: StellarDirectiveUpdate = {}
				if (keyPath === 'title') update.title = entity.title
				if (keyPath === 'codename') update.codename = entity.codename ?? null
				if (keyPath === 'due') update.due = entity.due ?? null
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

		await openNoteWhenReady(existence.associatedNote, core.lastWriteNotePending)
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

			.codename {
				text-transform: uppercase;
			}

			.due {
				display: flex;
				align-items: center;
				gap: .5em;
				font-weight: 300;
				opacity: .85;

				& .label {
					font-size: .7em;
					text-transform: uppercase;
					letter-spacing: .08em;
					opacity: .7;
				}
			}
		`
	}

	protected override get secondary() {
		return html`
			<p7t-directive-breadcrumb placeholder='Constellation Directive' .rootId=${this.entity!.parentDirectiveId}></p7t-directive-breadcrumb>
		`
	}

	protected override get actions() {
		// The scheduling period (start/end) is intentionally withheld for now; only the due date is surfaced.
		return html`
			<div class='due'>
				<span class='label'>Due</span>
				<p7t-editable-date ${this.binder.bind('due')}></p7t-editable-date>
			</div>
		`
	}

	protected override get headingTemplate() {
		return html`
			<p7t-editable-plaintext required label='Title' placeholder='Untitled' ${this.binder.bind('title')}></p7t-editable-plaintext>
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
