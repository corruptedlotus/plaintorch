import { component, css, html, nothing, state } from "@a11d/lit"
import { BriefingCard } from "./BriefingCard"
import { DependencyEndpointKind, Objective, ObjectiveStatus, OnrushSprint, type EndpointRef } from "@pleiades/sdk"
import { concludeConfirmation, confirmAction, core, DerivedRef, tooltip, TransferController } from ".."
import { endpointKey, immediatePrerequisites, unresolvedPrerequisites } from "../canvas/graphModel"
import { fuzzyFilter } from "../editing/fuzzy"
import { createChild, SuggestModalBase } from "../../host"

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

	/** The members whose dependency group is unfolded, by endpoint key; every group starts folded. */
	@state() private expandedGroups: ReadonlySet<string> = new Set()

	static override get styles() {
		return css`
			${super.styles}

			:host {
				--p7t-flare-accent: var(--p7t-accent-onrush);
			}

			.dependency-group-heading {
				cursor: pointer;
				user-select: none;

				&:hover {
					opacity: .9;
				}
			}

			.dependency-group-chevron {
				width: 1em;
				height: 1em;
				flex: 0 0 auto;
				margin-inline-start: -.3em;
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

	/**
	 * The members, each followed by its own dependency group when it has one, then the groups of the members that
	 * have no row here (the sprint's checkpoints).
	 */
	protected override get listContent() {
		const groups = this.unresolvedGroups
		const byMember = new Map(groups.map(group => [group.key, group]))
		const placed = new Set<string>()
		return html`
			${this.data!.objectives.map(objective => {
				const key = endpointKey({ kind: DependencyEndpointKind.Objective, id: objective.id })
				const group = byMember.get(key)
				if (group) {
					placed.add(key)
				}

				return html`
					<p7t-objective-item interactive .currentOnrush=${this.data} .entity=${objective} ${this.transfer.draggable(objective)}></p7t-objective-item>
					${group ? this.dependencyGroupTemplate(group) : nothing}
				`
			})}
			<!--<p7t-button @click=${() => this.addObjective()} icon='lucide:plus' class='add-button'>Add Objective</p7t-button>-->
			${groups.filter(group => !placed.has(group.key)).map(group => this.dependencyGroupTemplate(group))}
		`
	}

	/**
	 * One member's unresolved dependencies, under a heading that folds them away — folded by default, the heading
	 * still reading the member and the count, and unfolded per member for as long as the card lives.
	 *
	 * A group exists for every objective or checkpoint with at least one unmet prerequisite, walked
	 * recursively down the blocking chain. The count is the true total across every kind of prerequisite;
	 * the rows are the objective ones — the other kinds (checkpoints, directives, fates) count but do not
	 * list, so a group can read a higher number than the rows beneath it. In the count's tooltip, a prerequisite that is
	 * not holding the member's next transition back right now — one reached only through a gate on a later transition,
	 * or on one already made — is drawn faint.
	 */
	private dependencyGroupTemplate(group: DependencyGroup) {
		const expanded = this.expandedGroups.has(group.key)
		return html`
			<div class='dependency-group' ?data-expanded=${expanded}>
				<div class='dependency-group-heading' role='button' aria-expanded=${expanded} @click=${() => this.toggleGroup(group.key)}>
					<p7t-icon class='dependency-group-chevron' icon=${expanded ? 'lucide:chevron-down' : 'lucide:chevron-right'}></p7t-icon>
					<p7t-icon-item small icon='lucide:route-off' class='dependency-group-title'>${group.title}</p7t-icon-item>
					<span
						class='dependency-count'
						?data-detail=${group.nonObjectiveCount > 0}
						@click=${(e: Event) => e.stopPropagation()}
						${group.nonObjectiveCount > 0
							? tooltip(() => html`
								<p7t-endpoint-list .endpoints=${group.refs} .nonImmediate=${group.nonImmediate} filter='non-objective'>
									Blocked by
								</p7t-endpoint-list>
							`)
							: nothing}
					>${group.total}</span>
				</div>
				${!expanded ? nothing : html`
					<div class='dependency-group-objectives'>
						${group.objectives.map(objective => html`
							<p7t-objective-item interactive .entity=${objective} ${this.transfer.draggable(objective)}></p7t-objective-item>
						`)}
					</div>
				`}
			</div>
		`
	}

	/** Folds or unfolds one member's group. */
	private toggleGroup(key: string) {
		const expanded = new Set(this.expandedGroups)
		if (!expanded.delete(key)) {
			expanded.add(key)
		}

		this.expandedGroups = expanded
	}

	private get unresolvedGroups(): DependencyGroup[] {
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
		const dependencies = this.dependencies.value ?? []
		const reachable = unresolvedPrerequisites(dependencies, memberKeys)
		const immediate = immediatePrerequisites(dependencies, memberKeys)

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
			const holding = immediate.get(member.key)
			const nonImmediate = new Set(refs.map(endpointKey).filter(key => !holding?.has(key)))
			return [{ key: member.key, title: member.title, total: refs.length, objectives, refs, nonObjectiveCount, nonImmediate }]
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

	/** The sprint's day, 1 on its start date; undefined while it is in planning and has no start date yet. */
	private get currentDay() {
		const start = this.data?.startDate ? Date.parse(this.data.startDate) : NaN
		return Number.isNaN(start) ? undefined : Math.floor((Date.now() - start) / (1000 * 60 * 60 * 24)) + 1
	}

	protected override get footer() {
		const currentDay = this.currentDay
		return html`
			<p7t-value-progress icon='starfire' value=${this.currentStarfire} max=${this.maxStarfire}>
				<span>${currentDay === undefined ? 'In Planning' : `Day ${currentDay}`}</span>
			</p7t-value-progress>
			<p7t-button @click=${(e: Event) => this.conclude(e)}>Conclude</p7t-button>
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

	/** Concludes the running sprint now, once confirmed beside the button. */
	private async conclude(e: Event) {
		if (!this.data) return
		if (!await confirmAction(e, concludeConfirmation(`Conclude the Onrush "${this.data.title}"? It ends now.`)) || !this.data) return
		core.onrush.end(this.data.id).then(sprint => {
			if (!!sprint) this.data = undefined
		})
	}

	private addObjective() {
		if (!this.data) return
		new AddObjectiveModal(this).open()
	}
}

/** One member's unresolved prerequisites, as the card draws them (see `dependencyGroupTemplate`). */
interface DependencyGroup {
	/** The member's endpoint key — what the group is placed beneath, and folded or unfolded by. */
	key: string
	title: string
	total: number
	objectives: Objective[]
	refs: EndpointRef[]
	nonObjectiveCount: number
	nonImmediate: ReadonlySet<string>
}

/** Guards a collection that travelled here inside another entity's payload against a gap or a stub. */
function hasId<T extends { id?: string }>(value: T | undefined | null): value is T {
	return !!value && typeof value.id === 'string' && value.id.length > 0
}

/** Picks a live objective to add to the sprint, matching the objective listing — fetched once per opening — by {@link fuzzyFilter}. */
class AddObjectiveModal extends SuggestModalBase<Objective> {
	private objectives?: Promise<Objective[]>

	constructor(protected readonly host: BriefingCardOnrush) {
		super()
	}

	override async getSuggestions(query: string) {
		this.objectives ??= core.objectives.list()
		const live = (await this.objectives).filter(objective => objective.status < ObjectiveStatus.Archived)
		return fuzzyFilter(query, live, objective => objective.title)
	}

	renderSuggestion(objective: Objective, el: HTMLElement) {
		const item = createChild(el, 'p7t-objective-item')
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