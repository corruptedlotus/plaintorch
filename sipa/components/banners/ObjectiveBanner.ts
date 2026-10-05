import { component, css, html, state } from "@a11d/lit"
import { EntityBanner } from './EntityBanner'
import { DependencyEndpointKind, Objective, ObjectiveUpdate, PolarisCycle } from '@pleiades/sdk'
import { ObjectiveCollege, ObjectiveStatus } from "@pleiades/sdk"
import { OnrushSprint } from "@pleiades/sdk"
import { addObjectiveToPolaris, confirmAction, core, IconItem, IconName, isObjectiveInCycle, followRenamedNote, objectiveStatusDescriptors, offerEstimation, removeConfirmation, SelectCollegeModal, SelectStatusModal } from ".."
import { createChild, SuggestModalBase, toast } from '../../host'

@component('p7t-objective-banner')
export class ObjectiveBanner extends EntityBanner<Objective> {
	override icon = 'objective'

	@state() activePolaris?: PolarisCycle

	protected override readonly entityTypeName = 'Objective' as const

	/** Takes part in dependencies as an objective endpoint, so the banner carries its dependency route. */
	protected override get dependencyEndpoint() {
		return this.entity ? { kind: DependencyEndpointKind.Objective, id: this.entity.id } : undefined
	}

	protected binder = this.ref.binder('entity', {
		status: (entity) => core.objectives.shiftWorkflow(entity.id, { status: entity.status }),
		// Send only the changed field. The whole objective is cyclic (directive → objectives → this objective)
		// once the identity map cross-links it, so serializing it throws; the update endpoint wants the one field.
		'*': (entity, keyPath) => core.objectives.update(entity.id, { [keyPath]: entity[keyPath as keyof Objective] } as ObjectiveUpdate)
	}, (keyPath, entity, saved) => {
		if (saved && keyPath === 'title') {
			void followRenamedNote(entity.id)
		}
	})

	protected override async loadRelated() {
		this.activePolaris = await core.polaris.getCurrent()
	}

	protected get isInActivePolaris() {
		return !!this.entity && isObjectiveInCycle(this.entity.id, this.activePolaris)
	}

	/** Opens the Onrush picker; `button` is where a removal it leads to is confirmed, once the picker has closed. */
	pickOnrush = (button?: HTMLElement) => {
		new AddToOnrushModal(this, button).open()
	}

	/** Plans the objective into the active Polaris cycle, then asks for its estimate beside `button`, where it was added. */
	addToPolaris = async (button?: HTMLElement) => {
		if (!this.entity || this.isInActivePolaris) return
		const executive = await addObjectiveToPolaris(this.entity)
		if (executive) {
			this.activePolaris = await core.polaris.getCurrent()
			// The cycle comes back as the canonical instance, merged in place: the same object, so assigning it redraws nothing.
			this.requestUpdate()
			void offerEstimation(executive, button ?? this)
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
		return html`
			<p7t-editable-hierarchy .entity=${this.entity}></p7t-editable-hierarchy>
		`
	}

	protected override get info() {
		return html`
			<p7t-editable .doEdit=${SelectCollegeModal.prompt} ${this.binder.bind('college')} class='college'>
				<p7t-college-item mode='badge' .college=${this.entity!.college}></p7t-college-item>
			</p7t-editable>
		`
	}

	protected override get headingTemplate() {
		return html`
			<p7t-editable-plaintext required label='Title' placeholder='Untitled' ${this.binder.bind('title')}></p7t-editable-plaintext>
		`
	}
	
	protected override get preHeadingTemplate() {
		return html`<span>Pleiades Objective</span>`
	}

	protected override get subHeadingTemplate() {
		return html`
			<p7t-editable .doEdit=${() => SelectStatusModal.prompt(objectiveStatusDescriptors)} ${this.binder.bind('status')}>
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
			<p7t-button large icon='onrush' @click=${(e: Event) => this.pickOnrush(e.currentTarget as HTMLElement)}>
				${!onrush ? html`<span>Add to Onrush</span>` : html`
					<span>${onrushText}</span>
					<span class='switcher'>Reassign</span>
				`}
			</p7t-button>
			<p7t-button ?disabled=${inPolaris} large icon='polaris' @click=${(e: Event) => this.addToPolaris(e.currentTarget as HTMLElement)}>
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

class AddToOnrushModal extends SuggestModalBase<OnrushSprint | null> {
	constructor(protected readonly objectiveBanner: ObjectiveBanner, private readonly button?: HTMLElement) {
		super()
	}

	override async getSuggestions() {
		return [...await core.onrush.available(), null]
	}

	renderSuggestion(sprint: OnrushSprint | null, el: HTMLElement) {
		if (!sprint) {
			const item = createChild(el, 'p7t-icon-item') as IconItem<OnrushSprint | null>
			item.icon = 'lucide:circle-off'
			item.text = 'No Onrush'
			item.small = true
			return
		}
		createChild(el, 'div', { text: sprint.title })
		createChild(el, 'small', { text: (sprint.id === 'x0000' ? 'In-Planning' : 'Active') + ' Onrush' })
	}

	/** Moves the objective to the chosen sprint; "No Onrush" takes it out of its sprint, which is confirmed first. */
	override async onChooseSuggestion(item: OnrushSprint | null) {
		const objective = this.objectiveBanner.entity!
		if (!item && (objective.onrushSprintId || objective.onrushSprint)) {
			// The picker is gone by now, so the question is asked where it was opened from: the banner's Onrush button.
			const removing = removeConfirmation(`Remove "${objective.title}" from its Onrush?`, 'default')
			if (!await confirmAction(this.button ?? this.objectiveBanner, removing)) {
				return
			}
		}

		const objectiveId = objective.id
		const succeeded = await core.repos.objectives.mutate(objectiveId, async () => !item
			? await core.objectives.removeFromOnrush(objectiveId)
			: await core.objectives.addToOnrush(objectiveId, item.id))

		if (!succeeded) {
			return
		}

		toast(!item
			? 'Removed from Onrush.'
			: `Added to ${(item.id === 'x0000' ? 'planning' : 'active')} Onrush.`, 'success')
	}

}

declare global {
	interface HTMLTagNameMap {
		'p7t-objective-banner': ObjectiveBanner
	}
}