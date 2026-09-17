import { component, css, html, nothing } from "@a11d/lit"
import { BriefingCard } from "./BriefingCard"
import { DependencyEndpointKind, Objective, ObjectiveStatus, OnrushSprint } from "@pleiades/sdk"
import { core, DerivedRef, tooltip, TransferController } from ".."
import { endpointKey, unresolvedPrerequisites } from "../canvas/graphModel"
import { App, SuggestModal } from "obsidian"

@component('p7t-briefing-onrush')
export class BriefingCardOnrush extends BriefingCard<OnrushSprint> {
	override readonly icon = 'onrush'
	override readonly preHeading = 'Active Onrush'

	/**
	 * Offers every objective the card shows — the sprint's members and the unresolved objective prerequisites
	 * listed beneath them — to any host taking objectives: dragging one onto the Polaris card adds it to the
	 * cycle. Outbound is `clone`: an objective planned into the cycle stays where it is here. Source-only for
	 * now — receiving would mean an add-to-onrush translation, and the default receive (appending to the bag)
	 * must not touch an array the repository owns, so nothing is accepted rather than something wrong.
	 */
	protected readonly transfer = new TransferController<Objective>(this, {
		kind: 'Objective',
		accepts: [],
		bag: () => this.offeredObjectives,
		outbound: 'clone',
	})

	// The whole dependency edge set, and every objective, observed so a member's unmet prerequisites can be
	// walked (recursively) and an external one resolved to a real entity for its row. Shared derived records,
	// so this rides the same cache the dependency canvas keeps rather than fetching anew.
	private readonly dependencies = new DerivedRef(this, core.repos.dependencyList)
	private readonly objectiveList = new DerivedRef(this, core.repos.objectiveList)

	static override get styles() {
		return css`
			${super.styles}

			:host {
				--p7t-flare-accent: var(--p7t-accent-onrush);
			}

			.add-button {
				margin: .4em 1.2em;

				&::part(icon) {
					height: 1.4em;
					width: 1.4em;
				}
			}

			.dependency-group-heading {
				display: flex;
				align-items: center;
				gap: .6ch;
				margin-block: .8em .3em;
				padding-inline: 1.2em;
				font-size: .82em;
				opacity: .65;
				border-inline-start: 2px solid color-mix(in srgb, var(--p7t-flare-accent, var(--interactive-accent)) 50%, transparent);
			}

			.dependency-group-objectives {
				display: flex;
				flex-direction: column;
				align-items: stretch;
				padding-inline-start: .2em;
				border-inline-start: 2px solid color-mix(in srgb, var(--p7t-flare-accent, var(--interactive-accent)) 50%, transparent);
			}

			.dependency-count {
				font-variant-numeric: tabular-nums;
				opacity: .8;
			}

			/* When the count hides non-objective prerequisites, mark it as hoverable for their tooltip. */
			.dependency-count[data-detail] {
				cursor: help;
				text-decoration: underline dotted;
				text-underline-offset: 2px;
			}
		`
	}

	protected override get offlineTemplate() {
		return html`
			<span class='no-data'>Celestial Brazier Idle</span>
			<p7t-button @click=${() => this.begin()} large class='start-button' icon='onrush'>Begin Onrush</p7t-button>
		`
	}

	override get headingTemplate() {
		return html`<span>${this.data!.title}</span>`
	}

	protected override get listContent() {
		return html`
			${this.data!.objectives.map(objective => html`
				<p7t-objective-item interactive .currentOnrush=${this.data} .entity=${objective} ${this.transfer.draggable(objective)}></p7t-objective-item>
			`)}
			<p7t-button @click=${() => this.addObjective()} icon='lucide:plus' class='add-button'>Add Objective</p7t-button>
			${this.dependencySection}
		`
	}

	/**
	 * The onrush's unresolved dependencies, grouped under the member each holds back.
	 *
	 * A group appears for every objective or checkpoint with at least one unmet prerequisite, walked
	 * recursively down the blocking chain. The count is the true total across every kind of prerequisite;
	 * the rows are the objective ones — the other kinds (checkpoints, directives, fates) count but do not
	 * list, so a group can read a higher number than the rows beneath it.
	 */
	private get dependencySection() {
		const groups = this.unresolvedGroups
		if (groups.length === 0) {
			return nothing
		}

		return html`
			${groups.map(group => html`
				<div class='dependency-group'>
					<div class='dependency-group-heading'>
						<p7t-icon-item small icon='lucide:route-off' class='dependency-group-title'>${group.title}</p7t-icon-item>
						<span
							class='dependency-count'
							?data-detail=${group.nonObjectiveCount > 0}
							${group.nonObjectiveCount > 0
								? tooltip(() => html`<p7t-endpoint-list .endpoints=${group.refs} filter='non-objective'></p7t-endpoint-list>`)
								: nothing}
						>${group.total}</span>
					</div>
					<div class='dependency-group-objectives'>
						${group.objectives.map(objective => html`
							<p7t-objective-item interactive .entity=${objective} ${this.transfer.draggable(objective)}></p7t-objective-item>
						`)}
					</div>
				</div>
			`)}
		`
	}

	private get unresolvedGroups() {
		const sprint = this.data
		if (!sprint) {
			return []
		}

		const members = [
			...sprint.objectives.filter(hasId).map(objective => ({
				key: endpointKey({ kind: DependencyEndpointKind.Objective, id: objective.id }),
				title: objective.title
			})),
			...sprint.checkpoints.filter(hasId).map(checkpoint => ({
				key: endpointKey({ kind: DependencyEndpointKind.Checkpoint, id: checkpoint.id }),
				title: checkpoint.title
			}))
		]

		const memberKeys = new Set(members.map(member => member.key))
		const reachable = unresolvedPrerequisites(this.dependencies.value ?? [], memberKeys)

		return members.flatMap(member => {
			const refs = reachable.get(member.key)
			if (!refs || refs.length === 0) {
				return []
			}

			const objectives = refs
				.filter(ref => ref.kind === DependencyEndpointKind.Objective)
				.map(ref => this.objectiveFor(ref.id))
				.filter(hasId)
			const nonObjectiveCount = refs.filter(ref => ref.kind !== DependencyEndpointKind.Objective).length
			return [{ title: member.title, total: refs.length, objectives, refs, nonObjectiveCount }]
		})
	}

	/**
	 * Every objective this card puts on offer: the sprint's members, then each unresolved objective prerequisite
	 * listed beneath them, once each. The transfer bag — what a receiver may be handed from here.
	 */
	private get offeredObjectives(): Objective[] | undefined {
		const sprint = this.data
		if (!sprint) {
			return undefined
		}

		const offered = new Map(sprint.objectives.map(objective => [objective.id, objective]))
		for (const group of this.unresolvedGroups) {
			for (const objective of group.objectives) {
				offered.set(objective.id, objective)
			}
		}

		return [...offered.values()]
	}

	/** An objective prerequisite as its live entity — a member from the sprint, else one from the listing. */
	private objectiveFor(id: string) {
		return this.data?.objectives.find(objective => objective.id === id)
			?? this.objectiveList.value?.find(objective => objective.id === id)
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

/** Guards a collection that travelled here inside another entity's payload against a gap or a stub. */
function hasId<T extends { id?: string }>(value: T | undefined | null): value is T {
	return !!value && typeof value.id === 'string' && value.id.length > 0
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