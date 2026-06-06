import { component, css, event, html } from "@a11d/lit"
import { BriefingCard } from "./BriefingCard"
import { Objective, ObjectiveStatus, OnrushSprint } from "@pleiades/sdk"
import { core } from ".."
import { App, SuggestModal } from "obsidian"

@component('p7t-briefing-onrush')
export class BriefingCardOnrush extends BriefingCard<OnrushSprint> {
	override readonly icon = 'onrush'
	override readonly preHeading = 'Active Onrush'

	static override get styles() {
		return css`
			${super.styles}

			:host {
				--p7t-flare-accent: #9d2818;
			}

			.add-button {
				margin: .4em 1.2em;

				&::part(icon) {
					height: 1.4em;
					width: 1.4em;
				}
			}
		`
	}

	protected override get offlineTemplate() {
		return html`
			<span class='no-data'>Celestial Brazier Idle</span>
			<p7t-button @click=${() => this.begin()} class='start-button' icon='onrush'>Begin Onrush</p7t-button>
		`
	}

	override get headingTemplate() {
		return html`<span>${this.data!.title}</span>`
	}

	protected override get listContent() {
		return html`
			${this.data!.objectives.map(objective => html`
				<p7t-objective-item interactive .entity=${objective}></p7t-objective-item>
			`)}
			<p7t-button @click=${() => this.addObjective()} icon='lucide:plus' class='add-button'>Add Objective</p7t-button>
		`
	}

	protected override get footer() {
		const timespan = Date.now() - new Date(Date.parse(this.data!.startDate!)).getTime()
		const currentDay = Math.floor(timespan / (1000 * 60 * 60 * 24)) + 1
		return html`
			<p7t-value-progress icon='starfire' value=${this.currentStarfire} max=${this.maxStarfire}>
				<span>Day ${currentDay}</span>
			</p7t-value-progress>
			<p7t-button @click=${() => this.conclude()}>Conclude</p7t-button>
		`
	}

	private get currentStarfire() {
		return this.data?.objectives.reduce((sum, objective) => objective.status < ObjectiveStatus.Done ? sum : sum + objective.celestronValue, 0) ?? 0
	}

	private get maxStarfire() {
		return this.data?.objectives.reduce((sum, objective) => sum + objective.celestronValue, 0) ?? 0
	}

	private begin() {
		if (this.data) return
		core.onrush.startNew().then(sprint => this.data = sprint)
	}

	private conclude() {
		if (!this.data) return
		core.onrush.end(this.data.id).then(sprint => {
			if (!!sprint) this.data = undefined
		})
	}

	private addObjective() {
		if (!this.data) return
		new AddObjectiveModal((window as any).app! as App, this).open()
	}
}

class AddObjectiveModal extends SuggestModal<Objective> {
	constructor(app: App, protected readonly host: BriefingCardOnrush) {
		super(app);
	}

	override async getSuggestions(query: string) {
		const results = query.length > 2 ? await core.objectives.search(query) : await core.objectives.list()
		return results.filter(objective => objective.status < ObjectiveStatus.Archived)
	}

	renderSuggestion(objective: Objective, el: HTMLElement) {
		const item = el.createEl('p7t-objective-item')
		item.entity = objective
	}

	override async onChooseSuggestion(item: Objective, _: MouseEvent | KeyboardEvent) {
		if (await core.objectives.addToOnrush(item.id, this.host.data!.id)) {
			const updatedSprint = await core.onrush.get(this.host.data!.id)
			if (!!updatedSprint) this.host.data = updatedSprint
		}
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-briefing-onrush': BriefingCardOnrush
	}
}