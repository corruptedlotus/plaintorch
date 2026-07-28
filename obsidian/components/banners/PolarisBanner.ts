import { component, css, html } from "@a11d/lit"
import { EntityBanner } from './EntityBanner'
import { PleiadeanDate, PolarisCycle } from '@pleiades/sdk'
import { core, ReactiveBinder } from ".."
import { App } from "obsidian"

@component('p7t-polaris-banner')
export class PolarisBanner extends EntityBanner<PolarisCycle> {
	override icon = 'polaris'

	override get preHeadingTemplate() {
		return html`
			<span>Polaris Cycle</span>
		`
	}
	
	protected override readonly entityTypeName = 'PolarisCycle' as const

	protected binder = new ReactiveBinder<PolarisCycle>(this, 'entity', {
		sourceUpdate: () => this.beginEntityEdit(),
		sourceUpdated: async (_, keyPath) => {
			const entity = this.entity!
			const saved = await this.commitEntityEdit(async () => await core.polaris.update(entity.id, entity))
			if (!saved) {
				return
			}

			if (keyPath === 'title')
			{
				const existence = await core.repos.entityResolution.refresh(entity.id)

				const app = (window as any).app as App
				if (!existence?.associatedNote
					|| app.workspace.activeEditor?.file?.path === existence?.associatedNote) return

				const file = app.vault.getFileByPath(existence.associatedNote)!
				app.workspace.getLeaf(true).openFile(file)
			}
		}
	})

	static override get styles() {
		return css`
			${super.styles}

			:host {
				padding-inline: 1.2em;
				--p7t-flare-accent: #038899;
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

			.status::part(icon) {
				width: 1.4em;
				height: 1.4em;
			}

			.date-span {
				display: flex;
				align-items: center;
				font-weight: 400;
				gap: .5em;
				opacity: .7;
				font-size: .9em;
			}
		`
	}

	
	protected override get secondary() {
		return html``
	}

	protected override get headingTemplate() {
		return html`
			<p7t-editable-plaintext ${this.binder.bind('title')}></p7t-editable-plaintext>
		`
	}

	protected override get subHeadingTemplate() {
		if (!this.entity) return html``
		if (!this.entity.startTime) return html`
			<p7t-icon-item class='status' icon='state-zero' text="Planned"></p7t-icon-item>
		`
		if (!this.entity.endTime) return html`
			<p7t-icon-item class='status' icon='state-active' text="Active"></p7t-icon-item>
		`
		else return html`
			<p7t-icon-item class='status' icon='state-archived' text="Concluded"></p7t-icon-item>
		`
	}

	protected override get actions() {
		return html`
			<div class='date-span'>
				${!this.entity!.startTime ? html`<span>Not Started</span>` : html`
					<span>${PleiadeanDate.fromDate(new Date(this.entity!.startTime)).toString()}</span>
				`}
			</div>
		`
	}
}

declare global {
	interface HTMLTagNameMap {
		'p7t-polaris-banner': PolarisBanner
	}
}