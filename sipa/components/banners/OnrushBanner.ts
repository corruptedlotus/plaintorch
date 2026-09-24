import { component, css, html, nothing } from "@a11d/lit"
import { EntityBanner } from './EntityBanner'
import { OnrushSprint } from '@pleiades/sdk'
import { core, followRenamedNote, ReactiveBinder, SelectDirectiveStatusModal } from ".."

@component('p7t-onrush-banner')
export class OnrushBanner extends EntityBanner<OnrushSprint> {
	override icon = 'onrush'

	override get preHeadingTemplate() {
		return html`
			<span>Onrush Sprint</span>
		`
	}
	
	protected override readonly entityTypeName = 'OnrushSprint' as const

	protected binder = new ReactiveBinder<OnrushSprint>(this, 'entity', {
		sourceUpdate: () => this.beginEntityEdit(),
		sourceUpdated: async (_, keyPath) => {
			const entity = this.entity!
			const saved = await this.commitEntityEdit(async () => await core.onrush.update(entity.id, entity))
			if (!saved) {
				return
			}

			if (keyPath === 'title')
			{
				await followRenamedNote(entity.id)
			}
		}
	})

	static override get styles() {
		return css`
			${super.styles}

			:host {
				padding-inline: 1.2em;
				--p7t-flare-accent: #9d2818;
			}

			.switcher {
				font-size: .7em;
				opacity: .6;
				line-height: .9;
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
			<p7t-editable-plaintext required label='Title' placeholder='Untitled' ${this.binder.bind('title')}></p7t-editable-plaintext>
		`
	}

	protected override get subHeadingTemplate() {
		if (!this.entity) return html``
		if (!this.entity.startDate) return html`
			<p7t-icon-item class='status' icon='state-zero' text="Planned"></p7t-icon-item>
		`
		if (!this.entity.endDate) return html`
			<p7t-icon-item class='status' icon='state-active' text="Active"></p7t-icon-item>
		`
		else return html`
			<p7t-icon-item class='status' icon='state-archived' text="Concluded"></p7t-icon-item>
		`
	}

	protected override get actions() {
		return html`
			<div class='date-span'>
				${!this.entity!.startDate ? html`<span>Not Started</span>` : html`
					<p7t-datetime-view .date=${this.entity!.startDate}></p7t-datetime-view>
					${!this.entity!.endDate ? html`<p7t-icon icon='lucide:step-forward'></p7t-icon>` : html`
						<p7t-icon icon='lucide:arrow-right'></p7t-icon>
						<p7t-datetime-view .date=${this.entity!.endDate}></p7t-datetime-view>
					`}
				`}
			</div>
		`
	}
}

declare global {
	interface HTMLTagNameMap {
		'p7t-onrush-banner': OnrushBanner
	}
}