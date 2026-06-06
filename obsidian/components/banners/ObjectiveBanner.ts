import { component, css, html, state } from "@a11d/lit"
import { EntityBanner } from './EntityBanner'
import { Objective, PolarisCycle } from '@pleiades/sdk'
import { ObjectiveCollege, ObjectiveStatus } from "@pleiades/sdk"
import { OnrushSprint } from "@pleiades/sdk"
import { App, Notice, SuggestModal } from "obsidian"
import { core, IconItem, IconName, ReactiveBinder, SelectCollegeModal, SelectObjectiveStatusModal } from ".."

@component('p7t-objective-banner')
export class ObjectiveBanner extends EntityBanner<Objective> {
	override icon = 'objective'

	@state() activePolaris?: PolarisCycle

	protected binder = new ReactiveBinder<Objective>(this, 'entity', {
		sourceUpdated: async (_, keyPath) => {
			const entity = this.entity
			switch (keyPath) {
				case 'status':
					await core.objectives.shiftWorkflow(entity!.id, { status: entity!.status })
					break
				default:
					await core.objectives.update(entity!.id, entity!) ?? entity
					break
			}
			this.entity = await core.objectives.get(entity!.id)

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
		this.activePolaris = await core.polaris.getCurrent()
		return core.objectives.get(puck)
	}

	protected get isInActivePolaris() {
		if (!this.activePolaris) return false
		if (!this.entity) return false
		return this.activePolaris.executives.some(exec => exec.objective!.id === this.entity!.id)
	}

	pickOnrush = () => {
		new AddToOnrushModal(this.app!, this).open()
	}

	addToPolaris = async () => {
		if (this.isInActivePolaris) return
		if (await core.polaris.addObjectiveToCurrent(this.entity!.id)) {
			new Notice('Added to active Polaris cycle.')
			this.entity = await core.objectives.get(this.entity!.id)
		}
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
		const directiveTitle = this.entity!.directive?.title
		return !directiveTitle ? html`
			<span style='opacity: .5'>World Quest</span>
		` : html`
			<span>${directiveTitle}</span>
		`
	}

	protected override get info() {
		let college = ObjectiveCollege[this.entity!.college]

		return html`
			<p7t-editable .doEdit=${SelectCollegeModal.prompt} ${this.binder.bind('college')} class='college'>
				<span>${college === 'Unspecified' ? 'No College' : 'College of ' + college}</span>
			</p7t-editable>
		`
	}

	protected override get headingTemplate() {
		return html`
			<p7t-editable-plaintext ${this.binder.bind('title')}></p7t-editable-plaintext>
		`
	}
	
	protected override get preHeadingTemplate() {
		return html`<span>Pleiades Objective</span>`
	}

	protected override get subHeadingTemplate() {
		return html`
			<p7t-editable .doEdit=${SelectObjectiveStatusModal.prompt} ${this.binder.bind('status')}>
				<p7t-status-item
					.status=${ObjectiveStatus[this.entity!.status] as keyof typeof ObjectiveStatus}>
				</p7t-status-item>
			</p7t-editable>
		`
	}

	protected override get actions() {
		const onrush = this.entity!.onrushSprint
		let onrushText = 'Past Onrush'
		if (!onrush?.endDate) {
			onrushText = 'Active Onrush'
			if (!onrush?.startDate) {
				onrushText = 'In Planning'
			}
		}

		let inPolaris = this.isInActivePolaris

		return html`
			<p7t-editable-starfire ${this.binder.bind('celestronValue')}></p7t-editable-starfire>
			<p7t-button large icon='onrush' @click=${() => this.pickOnrush()}>
				${!onrush ? html`<span>Add to Onrush</span>` : html`
					<span>${onrushText}</span>
					<span class='switcher'>Reassign</span>
				`}
			</p7t-button>
			<p7t-button ?disabled=${inPolaris} large icon='polaris' @click=${() => this.addToPolaris()}>
				${!inPolaris ? html`<span>Add to Polaris</span>` : html`
					<p7t-icon class='marker-icon' icon='lucide:check'></p7t-icon>
				`}
			</p7t-button>
		`
	}

	protected override get stamp() {
		let college = ObjectiveCollege[this.entity!.college]
		college = college === 'Unspecified' ? 'None' : college
		return `college-${college.toLowerCase()}` as IconName
	}
}

class AddToOnrushModal extends SuggestModal<OnrushSprint | null> {
	constructor(app: App, protected readonly objectiveBanner: ObjectiveBanner) {
		super(app)
	}

	override async getSuggestions(query: string) {
		return [...await core.onrush.available(), null]
	}

	renderSuggestion(sprint: OnrushSprint | null, el: HTMLElement) {
		if (!sprint) {
			const item = el.createEl('p7t-icon-item') as IconItem<OnrushSprint | null>
			item.icon = 'lucide:circle-off'
			item.text = 'No Onrush'
			item.small = true
			return
		}
		el.createEl('div', { text: sprint.title })
		el.createEl('small', { text: (sprint.id === '0' ? 'Planning' : 'Active') + ' Onrush' })
	}

	override async onChooseSuggestion(item: OnrushSprint | null, evt: MouseEvent | KeyboardEvent) {
		if (!!item) {
			if (await core.objectives.addToOnrush(this.objectiveBanner.entity!.id, item.id)) {
				new Notice(`Added to ${(item.id === '0' ? 'planning' : 'active')} Onrush.`)
				this.objectiveBanner.entity = await core.objectives.get(this.objectiveBanner.entity!.id)
			}
		} else {
			if (await core.objectives.removeFromOnrush(this.objectiveBanner.entity!.id)) {
				new Notice('Removed from Onrush.')
				this.objectiveBanner.entity = await core.objectives.get(this.objectiveBanner.entity!.id)
			}
		}
	}

}

declare global {
	interface HTMLTagNameMap {
		'p7t-objective-banner': ObjectiveBanner
	}
}