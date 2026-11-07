import { OnrushSlot, typeNameOf, type OnrushSprint } from '@pleiades/sdk'
import { createChild, SuggestModalBase, toast } from '../../host'
import { confirmAction, core, removeConfirmation, type IconItem } from '..'

/** The planning placeholder's id: the one sprint being planned. */
const planningId = 'x0000'

/**
 * The two kinds an onrush sprint holds (PEP102 patch 10): an objective, and a stellar directive. A lunar directive never
 * joins one, so it has no kind here.
 */
export type OnrushMemberKind = 'objective' | 'directive'

/** What the onrush picker and the membership calls need of a member: its identity, its title and the sprint it is in. */
export interface OnrushMember {
	id: string
	title: string
	onrushSprintId?: string | undefined
}

/** What the onrush picker offers: a slot to join, or `null` to leave the sprint the member is in. */
type OnrushChoice = OnrushSlot | null

/**
 * The membership kind of an entity, read off its runtime type: an objective or a stellar directive, or `undefined` for
 * anything no sprint can hold (a lunar directive among them).
 */
export function onrushMemberKindOf(entity: unknown): OnrushMemberKind | undefined {
	switch (typeNameOf(entity)) {
		case 'Objective': return 'objective'
		case 'StellarDirective': return 'directive'
		default: return undefined
	}
}

/**
 * Picks the onrush sprint an objective or a stellar directive joins (PEP103 patch 4): the active one or the one being
 * planned. Both are always offered; one that does not exist yet says it will be made, and the core makes it — the active
 * slot begins the plan, or starts a sprint for today, and the planning slot plans one. A picker opened from a banner also
 * offers "No Onrush", which takes the member out of its sprint once that is confirmed where the picker was opened.
 */
export class OnrushPickerModal extends SuggestModalBase<OnrushChoice> {
	private active?: OnrushSprint
	private planning?: OnrushSprint

	constructor(
		private readonly kind: OnrushMemberKind,
		private readonly member: OnrushMember,
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

	/** Joins the chosen slot; "No Onrush" takes the member out of its sprint, which is confirmed first. */
	override async onChooseSuggestion(choice: OnrushChoice) {
		if (choice === null) {
			await this.removeFromOnrush()
			return
		}

		await addToOnrushSlot(this.kind, this.member, choice)
	}

	/** Takes the member out of its sprint, once the reader confirms it where the picker was opened. */
	private async removeFromOnrush() {
		if (!this.member.onrushSprintId) {
			return
		}

		// The picker is gone by now, so the question is asked where it was opened from.
		if (!await confirmAction(this.options.anchor ?? document.body, removeConfirmation(`Remove "${this.member.title}" from its Onrush?`, 'default'))) {
			return
		}

		if (await leaveOnrush(this.kind, this.member.id)) {
			toast('Removed from Onrush.', 'success')
		}
	}
}

/**
 * Puts an objective or a stellar directive in the active or the planning sprint, which the core makes when it does not
 * exist yet, and tells every surface showing the member or the sprints.
 */
export async function addToOnrushSlot(kind: OnrushMemberKind, member: Pick<OnrushMember, 'id'>, slot: OnrushSlot): Promise<boolean> {
	const added = kind === 'objective'
		? await core.repos.objectives.mutate(member.id, async () => await core.objectives.addToOnrush(member.id, slot))
		: await core.repos.directives.mutate(member.id, async () => await core.directives.addToOnrush(member.id, slot))
	toast(added
		? `Added to ${slot === OnrushSlot.Active ? 'the active' : 'the planning'} Onrush.`
		: 'Could not add to the Onrush.', added ? 'success' : 'error')
	if (added) {
		await Promise.all([core.repos.onrushCurrent.refresh(), core.repos.onrushPlanning.refresh()])
	}

	return added
}

/**
 * Takes an objective or a stellar directive out of whichever sprint holds it, through the member's own repository so
 * every surface showing it learns it left. The member itself survives; reporting the outcome is the caller's.
 */
export async function leaveOnrush(kind: OnrushMemberKind, memberId: string): Promise<boolean> {
	return kind === 'objective'
		? await core.repos.objectives.mutate(memberId, async () => await core.objectives.removeFromOnrush(memberId))
		: await core.repos.directives.mutate(memberId, async () => await core.directives.removeFromOnrush(memberId))
}
