import { component, html, property } from "@a11d/lit"
import { DecreeItem } from "./DecreeItem"
import { Attentive, AttentiveResolution } from "@pleiades/sdk"
import { Notice } from "obsidian"
import { core } from ".."
import "../system/DatetimeView"

/**
 * A Polaris-bound attentive rendered in the decree lineage — the attentive-side twin of {@link ObjectiveItemExecutive}.
 * It carries the decree chrome (directive glyph, college, context menu, live watch) from {@link DecreeItem} and
 * overrides the occurrence-specific parts: the notch quick-toggles the occurrence between done and pending, and the
 * toplane shows when it is due (or when it was resolved) where a decree would show its Celestron.
 *
 * A committed toggle is announced upward with a bubbling `attentivechange` event so the hosting cycle card reconciles.
 */
@component('p7t-decree-item-attentive')
export class DecreeItemAttentive extends DecreeItem {

	@property({
		updated(this: DecreeItemAttentive, value: Attentive | undefined) {
			this.entity = value?.decree
		}
	}) attentive?: Attentive

	private get done() {
		return this.attentive?.resolution === AttentiveResolution.Done
	}

	override get disabled() {
		return this.done
	}

	protected override get extraActionTemplate() {
		// Already bound to this cycle, so there is nothing to add — the add affordance belongs to the bare decree item.
		return undefined
	}

	protected override get notchTemplate() {
		return html`
			<p7t-status-item icon-only status=${this.done ? 'Done' : 'Standby'}></p7t-status-item>
		`
	}

	protected override async notchAction() {
		const attentive = this.attentive
		if (!attentive) return

		const resolution = this.done ? AttentiveResolution.Pending : AttentiveResolution.Done
		// A Polaris-bound occurrence has no meaningful recurrence-id and always exists as a row, so it is addressed by
		// its id; an unbound one is addressed by its RECURRENCE-ID so a still-projected agenda item hardens on
		// interaction instead of failing on an absent row id.
		const occurrence = attentive.polarisCycleId
			? { id: attentive.id }
			: {
				decreeId: attentive.decreeId,
				recurrenceDate: attentive.recurrenceDate,
				recurrenceTime: attentive.recurrenceTime
			}
		const updated = await core.declaratives.updateAttentive(occurrence, { resolution })
		if (!updated) {
			new Notice('Failed to update attentive.')
			return
		}

		// The update response carries no decree navigation, so the known decree is kept for the directive/college lines.
		const merged: Attentive = { ...attentive, ...updated, decree: updated.decree ?? attentive.decree }
		this.attentive = merged
		this.dispatchEvent(new CustomEvent<Attentive>('attentivechange', { detail: merged, bubbles: true, composed: true }))
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-decree-item-attentive': DecreeItemAttentive
	}
}
