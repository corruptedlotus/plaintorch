import { component, css, html } from "@a11d/lit"
import { EntityBanner } from './EntityBanner'
import { Directive, DirectiveStatus } from '@pleiades/sdk'
import { core, ReactiveBinder, SelectDirectiveStatusModal } from ".."
import { App } from "obsidian"

@component('p7t-directive-banner')
export class DirectiveBanner extends EntityBanner<Directive> {
	override icon = 'directive'

	override get preHeadingTemplate() {
		return !this.entity?.codename ? html`
			<span>Stellar Directive</span>
		` : html`
			<span>Codename ${this.entity.codename.toUpperCase()}</span>
		`
	}
	
	protected binder = new ReactiveBinder<Directive>(this, 'entity', {
		sourceUpdated: async (_, keyPath) => {
			const entity = this.entity
			switch (keyPath) {
				case 'status':
					await core.directives.shiftWorkflow(entity!.id, { status: entity!.status })
					break
				default:
					await core.directives.update(entity!.id, entity!) ?? entity
					break
			}
			this.entity = await core.directives.get(entity!.id)

			if (keyPath === 'title')
			{
				const existence = await core.system.resolveEntity(entity!.id)
	
				const app = (window as any).app as App
				if (!existence?.associatedNote
					|| app.workspace.activeEditor?.file?.path === existence?.associatedNote) return
	
				const file = app.vault.getFileByPath(existence.associatedNote)!
				app.workspace.getLeaf(true).openFile(file)
			}
		}
	})

	override async fetchEntity(puck: string) {
		return core.directives.get(puck)
	}

	static override get styles() {
		return css`
			${super.styles}

			:host {
				padding-inline: 1.2em;
			}

			.college {
				display: flex;
				align-items: center;
				user-select: none;

				& span {
					padding: 0.08em 0.8ch;
					border-radius: 4px;
					background: color-mix(in srgb, var(--text-normal) 15%, transparent);
					color: color-mix(in srgb, var(--text-normal) 60%, transparent);
					font-family: var(--font-interface);
				}
			}

			.switcher {
				font-size: .7em;
				opacity: .6;
				line-height: .9;
			}

			.marker-icon {
				width: 1.4em;
				height: 1.4em;
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
					.status=${DirectiveStatus[this.entity!.status] as keyof typeof DirectiveStatus}>
				</p7t-status-item>
			</p7t-editable>
		`
	}
}

declare global {
	interface HTMLTagNameMap {
		'p7t-directive-banner': DirectiveBanner
	}
}