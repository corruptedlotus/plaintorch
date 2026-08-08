import { component, html, property } from "@a11d/lit"
import { Attentive, AttentiveResolution } from "@pleiades/sdk"
import { Notice } from "obsidian"
import { OccurrenceItem } from "./OccurrenceItem"
import { core } from ".."

/**
 * A single attentive occurrence of a decree. Its notch quick-switches between undone (Pending) and done;
 * skip/reschedule are deliberately deferred. Its toplane surfaces the decree's lunar directive.
 *
 * Rendered both for the agenda's unbound attentives and for a cycle's bound attentives, so a committed
 * toggle is announced upward with a bubbling `attentivechange` event and the host reconciles.
 */
@component('p7t-attentive-item')
export class AttentiveItem extends OccurrenceItem {
	@property({ type: Object }) attentive?: Attentive

	private get done() {
		return this.attentive?.resolution === AttentiveResolution.Done
	}

	protected override get heading() {
		return this.attentive?.decree?.title ?? 'Attentive'
	}

	protected override get directive() {
		return this.attentive?.decree?.directive
	}

	protected override get notchTemplate() {
		return html`<p7t-icon icon=${this.done ? 'state-done' : 'state-zero'}></p7t-icon>`
	}

	protected override async notchAction() {
		const attentive = this.attentive
		if (!attentive) return

		const resolution = this.done ? AttentiveResolution.Pending : AttentiveResolution.Done
		const updated = await core.declaratives.updateAttentive(attentive.id, { resolution })
		if (!updated) {
			new Notice('Failed to update attentive.')
			return
		}

		// The update response carries no decree navigation, so the known decree is kept for the directive line.
		const merged: Attentive = { ...attentive, ...updated, decree: updated.decree ?? attentive.decree }
		this.attentive = merged
		this.dispatchEvent(new CustomEvent<Attentive>('attentivechange', { detail: merged, bubbles: true, composed: true }))
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-attentive-item': AttentiveItem
	}
}
