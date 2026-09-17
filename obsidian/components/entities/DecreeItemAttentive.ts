import { component, html, property } from "@a11d/lit"
import { DecreeItem } from "./DecreeItem"
import { Attentive, AttentiveResolution } from "@pleiades/sdk"
import { AttentiveModal, getApp } from ".."
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

	/**
	 * The notch is the allocation chip, as on an executive, and opens the attentive's allocation modal — where the
	 * occurrence is also marked done or pending. The chip owns the state read: resolved, no allocation, time left.
	 */
	protected override get notchTemplate() {
		const attentive = this.attentive
		return html`
			<p7t-allocation-item
				?executed=${this.done}
				.estimation=${attentive?.estimation}
				.minimum=${attentive?.minimum}
				.maximum=${attentive?.maximum}
				.elapsed=${0}>
			</p7t-allocation-item>
		`
	}

	/** An executive shows its affinity where the decree would show its Celestron; so does the attentive. */
	protected override get info() {
		return html`<p7t-timeframe-item affinity small mode='icon' .timeframe=${this.attentive?.affinityTimeframe}></p7t-timeframe-item>`
	}

	protected override async notchAction() {
		const attentive = this.attentive
		if (!attentive) return

		new AttentiveModal(getApp(), attentive, updated => {
			this.attentive = updated
			this.dispatchEvent(new CustomEvent<Attentive>('attentivechange', { detail: updated, bubbles: true, composed: true }))
		}).open()
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-decree-item-attentive': DecreeItemAttentive
	}
}
