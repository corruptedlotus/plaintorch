import { component, css, html, property, state } from "@a11d/lit"
import { PolarisExecutivePlanningMode, type Activity, type Executive, type PolarisCycle, type PolarisExecutivePlan } from "@pleiades/sdk"
import { core, ExecutiveModal, isObjectiveInCycle, type ActivityChoice, type EditableTimeUnit, type TimeframeChoice, type TimeframeSelect, type ActivitySelect } from ".."
import { CreationRowBase } from "../editing/CreationRowBase"
import { toast } from "../../host"

/** What the Polaris row makes: an executive — either objective- or decree-backed (PEP111). */
export type PolarisActivityCreated = Executive

/**
 * The inline row that adds an activity to the Polaris cycle: an estimation, the activity — an existing objective
 * or decree, or a new one named on the spot — and a timeframe affinity.
 *
 * Committing plans the activity into the cycle as an executive (PEP111): an objective is planned (a new one
 * created standalone by the same plan call), a decree is added as a decree-backed executive (a new decree is
 * created first). The estimation seeds the allocation. Objectives already in the cycle are listed but cannot be
 * chosen — a cycle holds one instance of an objective.
 *
 * The affinity is sent inside the create request itself, on both paths (PEP100 patch 2), and defaults to **Auto**:
 * left on Auto the key is omitted and the core assigns the affinity — the incentive's directive availability, else
 * its college; an explicit none sends `null`; a picked timeframe sends its id. The core returns the executive with
 * its resolved affinity, so nothing is patched afterwards.
 *
 * The editor a committed row opens is the executive's allocation modal.
 */
@component('p7t-polaris-creation-row')
export class PolarisCreationRow extends CreationRowBase<PolarisActivityCreated> {
	/** The cycle the row adds to — what "already in the cycle" is judged against. */
	@property({ type: Object }) cycle?: PolarisCycle

	@state() private estimation?: number
	@state() private activity?: ActivityChoice
	/** The affinity: undefined is Auto (the reset default), `null` an explicit none, a record a picked timeframe. */
	@state() private timeframe?: TimeframeChoice

	/** A cycle holds one instance of an objective, so one already in it is found but cannot be added again. */
	private readonly unavailableInCycle = (activity: Activity) =>
		activity.kind === 'objective' && !!activity.objective && isObjectiveInCycle(activity.objective.id, this.cycle)
			? 'In cycle'
			: undefined

	static override get styles() {
		return css`
			${super.styles}

			p7t-editable-time-unit {
				font-size: 2em;
			}
			
			.cells {
				grid-template-columns: 3em 1fr auto;
			}
		`
	}

	protected override get cells() {
		return html`
			<div class='cell'>
				<span class='caption'>Est.</span>
				<p7t-editable-time-unit
					nullable
					accent
					.value=${this.estimation}
					@change=${(e: Event) => this.estimation = (e.target as EditableTimeUnit).value}>
				</p7t-editable-time-unit>
			</div>
			<div class='cell grow'>
				<span class='caption'>Activity</span>
				<p7t-activity-select
					allowCreation
					.unavailable=${this.unavailableInCycle}
					.value=${this.activity}
					@change=${(e: Event) => this.activity = (e.target as ActivitySelect).value}>
				</p7t-activity-select>
			</div>
			<div class='cell'>
				<span class='caption'>Affinity</span>
				<p7t-timeframe-select
					thumbnail
					auto
					.value=${this.timeframe}
					@change=${(e: Event) => this.timeframe = (e.target as TimeframeSelect).value}>
				</p7t-timeframe-select>
			</div>
		`
	}

	protected override async create(): Promise<PolarisActivityCreated | undefined> {
		const activity = this.activity
		if (!activity) {
			toast('Pick an activity, or name a new one.', 'warning')
			return undefined
		}

		return activity.kind === 'decree' ? await this.createDecreeExecutive(activity) : await this.createExecutive(activity)
	}

	/**
	 * The affinity as the create requests carry it: undefined (Auto) omits the key so the core assigns it, `null`
	 * asks for none, and a picked timeframe sends its id.
	 */
	private get affinityTimeframeId(): number | null | undefined {
		return this.timeframe === undefined ? undefined : this.timeframe?.id ?? null
	}

	private async createExecutive(activity: ActivityChoice): Promise<PolarisActivityCreated | undefined> {
		const affinityTimeframeId = this.affinityTimeframeId
		const plan: PolarisExecutivePlan = activity.isNew
			? { mode: PolarisExecutivePlanningMode.Standalone, title: activity.title, estimation: this.estimation, affinityTimeframeId }
			: { mode: PolarisExecutivePlanningMode.FromObjective, objectiveId: activity.objective!.id, estimation: this.estimation, affinityTimeframeId }

		// An existing objective changes state when planned, so its write runs through its repository; a new one
		// has no canonical instance yet and is simply created.
		const result = activity.isNew
			? await core.polaris.planExecutive(plan)
			: await core.repos.objectives.mutate(activity.objective!.id, async () => await core.polaris.planExecutive(plan))
		if (!result) {
			toast(`PLAINTORCH could not add ${activity.title} to the cycle.`, 'error')
			return undefined
		}

		const executive: Executive = { ...result.executive, incentive: result.executive.incentive ?? result.objective ?? activity.objective }
		await this.refresh(activity.isNew ? core.repos.objectiveList : undefined)
		toast(`Added ${activity.title} to the active Polaris cycle.`, 'success')
		return executive
	}

	private async createDecreeExecutive(activity: ActivityChoice): Promise<PolarisActivityCreated | undefined> {
		const decree = activity.isNew ? await core.declaratives.createDecree({ title: activity.title }) : activity.decree
		if (!decree) {
			toast(`PLAINTORCH could not create the decree ${activity.title}.`, 'error')
			return undefined
		}

		const executive = await core.polaris.addDecreeExecutive({ decreeId: decree.id, estimation: this.estimation, affinityTimeframeId: this.affinityTimeframeId })
		if (!executive) {
			toast(`PLAINTORCH could not add ${activity.title} to the cycle.`, 'error')
			return undefined
		}

		await this.refresh(activity.isNew ? core.repos.decreeList : undefined)
		toast(`Added ${activity.title} to the active Polaris cycle.`, 'success')
		return { ...executive, incentive: executive.incentive ?? decree }
	}

	/** The new activity rides on the owning cycle and the briefing; a newly created incentive also on its listing. */
	private async refresh(listing?: { revalidateIfObserved(): Promise<void> }) {
		await Promise.all([
			core.repos.polaris.revalidateObserved(),
			core.repos.briefing.revalidateIfObserved(),
			listing?.revalidateIfObserved()
		])
	}

	protected override openEditor(created: PolarisActivityCreated) {
		new ExecutiveModal(created).open()
	}

	protected override reset() {
		this.estimation = undefined
		this.activity = undefined
		this.timeframe = undefined
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-polaris-creation-row': PolarisCreationRow
	}
}
