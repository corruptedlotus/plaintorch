import { component, css, eventListener, html } from '@a11d/lit'
import type { PolarisCycle } from '@pleiades/sdk'
import { tooltip, type ContextMenuSpec, type IconName } from '..'
import { openEntityNote } from '../grid/entityActions'
import { EntityItem } from './EntityItem'
import { entityMenuEntries } from './entityMenu'

/** Where a cycle stands: not begun, running, or concluded, as its banner names the three. */
function cycleState(cycle: PolarisCycle): { readonly icon: IconName, readonly label: string } {
	if (!cycle.startTime) {
		return { icon: 'state-zero', label: 'Planned' }
	}

	return !cycle.endTime
		? { icon: 'state-active', label: 'Active' }
		: { icon: 'state-archived', label: 'Concluded' }
}

/**
 * List-row for a Polaris cycle: its state in the notch, its day above its title, and how much of its plan was done.
 *
 * A cycle is a journal day, read in its note, so the whole row opens the note when it is {@link interactive}, and its
 * menu offers the same. The generic entity menu is not raised: a cycle has no editor of its own here, and nothing in
 * it can be moved or deleted from a list.
 */
@component('p7t-polaris-cycle-item')
export class PolarisCycleItem extends EntityItem<PolarisCycle> {
	static override get styles() {
		return css`
			${super.styles}

			:host([interactive]) {
				cursor: pointer;
			}

			.tally {
				font-size: .85em;
				opacity: .7;
				font-variant-numeric: tabular-nums;
			}
		`
	}

	protected override get notchTemplate() {
		const state = cycleState(this.entity!)
		return html`<p7t-icon class='notch-icon' icon=${state.icon} ${tooltip(state.label)}></p7t-icon>`
	}

	protected override get preTitle() {
		return !this.entity?.day ? html`` : html`<p7t-datetime-view .date=${this.entity.day}></p7t-datetime-view>`
	}

	/** The executives done of those planned, when the cycle planned any. */
	protected override get info() {
		const executives = this.entity?.executives ?? []
		if (executives.length === 0) {
			return html``
		}

		const done = executives.filter(executive => executive.executed).length
		return html`<span class='tally' ${tooltip(`${done} of ${executives.length} executives done`)}>${done}/${executives.length}</span>`
	}

	protected override contextMenuSpec(): ContextMenuSpec | undefined {
		const cycle = this.entity
		return this.menu ?? (this.interactive && cycle ? { title: cycle.title, entries: [entityMenuEntries.openNote(cycle)] } : undefined)
	}

	/** The title takes no click of its own: the whole row opens the note. */
	protected override async titleAction() { }

	@eventListener('click')
	protected onClick() {
		if (this.interactive && this.entity) {
			void openEntityNote(this.entity)
		}
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-polaris-cycle-item': PolarisCycleItem
	}
}
