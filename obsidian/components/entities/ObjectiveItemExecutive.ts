import { component, html, property } from "@a11d/lit"
import { ObjectiveItem } from "./ObjectiveItem"
import { Executive } from "@pleiades/sdk"
import { ExecutiveModal, getApp, type ContextMenuSpec } from ".."
import { polarisActivityMenu } from "./polarisActivity"

@component('p7t-objective-item-exec')
export class ObjectiveItemExecutive extends ObjectiveItem {

	@property({
		updated(this: ObjectiveItemExecutive, value: Executive | undefined) {
			this.entity = value?.objective
		}
	}) executive?: Executive

	override get disabled() {
		return !!this.executive?.executed
	}

	/**
	 * Inside a cycle the row is the executive, not the objective: its menu marks it done, manages its time, edits
	 * the objective behind it, or takes it out of the cycle — never the objective's own "Delete". A host-supplied
	 * menu still wins, as on every entity item.
	 */
	protected override contextMenuSpec(): ContextMenuSpec | undefined {
		if (this.menu || !this.interactive || !this.executive) {
			return super.contextMenuSpec()
		}

		return polarisActivityMenu({ kind: 'executive', executive: this.executive }, updated => {
			if (updated.kind === 'executive') {
				this.executive = updated.executive
				this.dispatchEvent(new CustomEvent<void>('updateRequest', { bubbles: true, composed: true }))
			}
		})
	}

	/**
	 * An executive shows its timeframe **affinity** where an objective would show its Celestron — the Celestron is
	 * replaced outright (PEP100 patch). The affinity chip carries its own empty state ("No Affinity"), so it stands
	 * in whether or not a timeframe is in play.
	 */
	protected override get info() {
		return html`<p7t-timeframe-item affinity small mode='icon' .timeframe=${this.executive?.affinityTimeframe}></p7t-timeframe-item>`
	}

	protected override async notchAction() {
		if (!this.executive) return
		new ExecutiveModal(getApp(), this.executive, executive => {
			this.executive = executive
			this.dispatchEvent(new CustomEvent<void>('updateRequest', { bubbles: true, composed: true }))
		}).open()
	}

	protected override get notchTemplate() {
		// The allocation chip owns the whole state read — resolved, no-allocation, time-left, active, overworked —
		// and its progress tooltip.
		const executive = this.executive
		return html`
			<p7t-allocation-item
				?executed=${!!executive?.executed}
				.estimation=${executive?.estimation}
				.minimum=${executive?.minimum}
				.maximum=${executive?.maximum}
				.elapsed=${executive?.elapsed ?? 0}>
			</p7t-allocation-item>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-objective-item-exec': ObjectiveItemExecutive
	}
}
