import { OnrushSlot, type Objective, type OnrushSprint } from '@pleiades/sdk'
import { createChild, SuggestModalBase, toast } from '../../host'
import { confirmAction, core, removeConfirmation, type IconItem } from '..'

/** The planning placeholder's id: the one sprint being planned. */
const planningId = 'x0000'

/** What the onrush picker offers: a slot to join, or `null` to leave the sprint the objective is in. */
type OnrushChoice = OnrushSlot | null

/**
 * Picks the onrush sprint an objective joins (PEP103 patch 4): the active one or the one being planned. Both are always
 * offered; one that does not exist yet says it will be made, and the core makes it — the active slot begins the plan, or
 * starts a sprint for today, and the planning slot plans one. A picker opened from a banner also offers "No Onrush", which
 * takes the objective out of its sprint once that is confirmed where the picker was opened.
 */
export class OnrushPickerModal extends SuggestModalBase<OnrushChoice> {
	private active?: OnrushSprint
	private planning?: OnrushSprint

	constructor(
		private readonly objective: Pick<Objective, 'id' | 'title' | 'onrushSprintId'>,
		private readonly options: { removable?: boolean, anchor?: HTMLElement } = {}
	) {
		super()
	}

	override async getSuggestions(): Promise<OnrushChoice[]> {
		const available = await core.onrush.available()
		this.active = available.find(sprint => sprint.id !== planningId)
		this.planning = available.find(sprint => sprint.id === planningId)
		return [OnrushSlot.Active, OnrushSlot.Planning, ...(this.options.removable ? [null] : [])]
	}

	renderSuggestion(choice: OnrushChoice, el: HTMLElement) {
		if (choice === null) {
			const item = createChild(el, 'p7t-icon-item') as IconItem<OnrushChoice>
			item.icon = 'lucide:circle-off'
			item.text = 'No Onrush'
			item.small = true
			return
		}

		const [sprint, title, missing] = choice === OnrushSlot.Active
			? [this.active, 'Active Onrush', this.planning ? 'The planned Onrush will begin' : 'A new Onrush will start']
			: [this.planning, 'Planning Onrush', 'A new plan will be made']
		createChild(el, 'div', { text: sprint?.title ?? title })
		createChild(el, 'small', { text: sprint ? title : missing })
	}

	/** Joins the chosen slot; "No Onrush" takes the objective out of its sprint, which is confirmed first. */
	override async onChooseSuggestion(choice: OnrushChoice) {
		if (choice === null) {
			await removeObjectiveFromOnrush(this.objective, this.options.anchor)
			return
		}

		await addObjectiveToOnrushSlot(this.objective, choice)
	}
}

/**
 * Puts an objective in the active or the planning sprint, which the core makes when it does not exist yet, and tells every
 * surface showing the objective or the sprints.
 */
export async function addObjectiveToOnrushSlot(objective: Pick<Objective, 'id'>, slot: OnrushSlot): Promise<boolean> {
	const added = await core.repos.objectives.mutate(objective.id, async () => await core.objectives.addToOnrush(objective.id, slot))
	toast(added
		? `Added to ${slot === OnrushSlot.Active ? 'the active' : 'the planning'} Onrush.`
		: 'Could not add to the Onrush.', added ? 'success' : 'error')
	if (added) {
		await Promise.all([core.repos.onrushCurrent.refresh(), core.repos.onrushPlanning.refresh()])
	}

	return added
}

/** Takes an objective out of its sprint, once the reader confirms it where the picker was opened. */
async function removeObjectiveFromOnrush(objective: Pick<Objective, 'id' | 'title' | 'onrushSprintId'>, anchor?: HTMLElement) {
	if (!objective.onrushSprintId) {
		return
	}

	// The picker is gone by now, so the question is asked where it was opened from.
	if (!await confirmAction(anchor ?? document.body, removeConfirmation(`Remove "${objective.title}" from its Onrush?`, 'default'))) {
		return
	}

	if (await core.repos.objectives.mutate(objective.id, async () => await core.objectives.removeFromOnrush(objective.id))) {
		toast('Removed from Onrush.', 'success')
	}
}
