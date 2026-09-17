import { component, html, property, state } from "@a11d/lit"
import { PolarisExecutivePlanningMode, type Activity, type Attentive, type DirectiveTimeframeRecord, type Executive, type PolarisCycle, type PolarisExecutivePlan } from "@pleiades/sdk"
import { Notice } from "obsidian"
import { core, ExecutiveModal, getApp, isObjectiveInCycle, openEntityEditor, type ActivityChoice, type EditableTimeUnit, type TimeframeSelect, type ActivitySelect } from ".."
import { CreationRowBase } from "../editing/CreationRowBase"

/** What the Polaris row makes: an executive (from an objective) or an attentive (from a decree). */
export type PolarisActivityCreated =
	| { readonly kind: 'executive', readonly executive: Executive }
	| { readonly kind: 'attentive', readonly attentive: Attentive }

/**
 * The inline row that adds an activity to the Polaris cycle: an estimation, the activity — an existing objective
 * or decree, or a new one named on the spot — and a timeframe affinity.
 *
 * Committing plans the activity into the cycle by the path its kind takes: an objective becomes an executive
 * (a new objective is created standalone by the same plan call), a decree materializes an attentive (a new
 * decree is created first). The estimation seeds the allocation; the affinity is applied to the executive
 * afterwards, since planning does not take one. Objectives already in the cycle are not offered — a cycle
 * holds one instance of an objective.
 *
 * The editor a committed row opens is the executive's allocation modal; an attentive has no allocation editor
 * of its own yet, so its decree's editor stands in.
 */
@component('p7t-polaris-creation-row')
export class PolarisCreationRow extends CreationRowBase<PolarisActivityCreated> {
	/** The cycle the row adds to — what "already in the cycle" is judged against. */
	@property({ type: Object }) cycle?: PolarisCycle

	@state() private estimation?: number
	@state() private activity?: ActivityChoice
	@state() private timeframe?: DirectiveTimeframeRecord

	private readonly excludeInCycle = (activity: Activity) =>
		activity.kind === 'objective' && !!activity.objective && isObjectiveInCycle(activity.objective.id, this.cycle)

	protected override get cells() {
		return html`
			<div class='cell'>
				<span class='caption'>Estimation</span>
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
					.exclude=${this.excludeInCycle}
					.value=${this.activity}
					@change=${(e: Event) => this.activity = (e.target as ActivitySelect).value}>
				</p7t-activity-select>
			</div>
			<div class='cell'>
				<span class='caption'>Affinity</span>
				<p7t-timeframe-select
					.value=${this.timeframe}
					@change=${(e: Event) => this.timeframe = (e.target as TimeframeSelect).value}>
				</p7t-timeframe-select>
			</div>
		`
	}

	protected override async create(): Promise<PolarisActivityCreated | undefined> {
		const activity = this.activity
		if (!activity) {
			new Notice('Pick an activity, or name a new one.')
			return undefined
		}

		return activity.kind === 'decree' ? await this.createAttentive(activity) : await this.createExecutive(activity)
	}

	private async createExecutive(activity: ActivityChoice): Promise<PolarisActivityCreated | undefined> {
		const plan: PolarisExecutivePlan = activity.isNew
			? { mode: PolarisExecutivePlanningMode.Standalone, title: activity.title, estimation: this.estimation }
			: { mode: PolarisExecutivePlanningMode.FromObjective, objectiveId: activity.objective!.id, estimation: this.estimation }

		// An existing objective changes state when planned, so its write runs through its repository; a new one
		// has no canonical instance yet and is simply created.
		const result = activity.isNew
			? await core.polaris.planExecutive(plan)
			: await core.repos.objectives.mutate(activity.objective!.id, async () => await core.polaris.planExecutive(plan))
		if (!result) {
			new Notice(`PLAINTORCH could not add ${activity.title} to the cycle.`)
			return undefined
		}

		let executive: Executive = { ...result.executive, objective: result.executive.objective ?? result.objective ?? activity.objective }
		if (this.timeframe) {
			const updated = await core.polaris.updateExecutive(executive.id, { affinityTimeframeId: this.timeframe.id })
			if (updated) {
				executive = { ...executive, ...updated, objective: updated.objective ?? executive.objective, affinityTimeframe: executive.affinityTimeframe }
			}
			else {
				new Notice('The executive was added, but its affinity could not be set.')
			}
		}

		await this.refresh(activity.isNew ? core.repos.objectiveList : undefined)
		new Notice(`Added ${activity.title} to the active Polaris cycle.`)
		return { kind: 'executive', executive }
	}

	private async createAttentive(activity: ActivityChoice): Promise<PolarisActivityCreated | undefined> {
		const decree = activity.isNew ? await core.declaratives.createDecree({ title: activity.title }) : activity.decree
		if (!decree) {
			new Notice(`PLAINTORCH could not create the decree ${activity.title}.`)
			return undefined
		}

		const attentive = await core.polaris.addAttentive({ decreeId: decree.id, estimation: this.estimation })
		if (!attentive) {
			new Notice(`PLAINTORCH could not add ${activity.title} to the cycle.`)
			return undefined
		}

		await this.refresh(activity.isNew ? core.repos.decreeList : undefined)
		new Notice(`Added ${activity.title} to the active Polaris cycle.`)
		return { kind: 'attentive', attentive: { ...attentive, decree: attentive.decree ?? decree } }
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
		if (created.kind === 'executive') {
			new ExecutiveModal(getApp(), created.executive).open()
			return
		}

		// No attentive allocation editor exists yet; the decree behind it is the nearest thing to edit.
		if (created.attentive.decree) {
			openEntityEditor(created.attentive.decree)
		}
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
