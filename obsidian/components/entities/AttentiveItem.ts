import { component, html, property } from "@a11d/lit"
import { Attentive, AttentiveResolution } from "@pleiades/sdk"
import { Notice } from "obsidian"
import { OccurrenceItem } from "./OccurrenceItem"
import { occurrenceMenu, openOccurrenceModal, type OccurrenceTarget } from "./OccurrenceModal"
import { core, getApp, type ContextMenuSpec } from ".."
import "../system/DatetimeView"

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

	override get disabled() {
		return this.done
	}

	private get target(): OccurrenceTarget | undefined {
		return this.attentive ? { kind: 'attentive', attentive: this.attentive } : undefined
	}

	/** Folds a committed change from the modal or the menu back into the row and announces it upward. */
	private readonly applyChange = (updated: OccurrenceTarget) => {
		if (updated.kind !== 'attentive') return
		this.attentive = updated.attentive
		this.dispatchEvent(new CustomEvent<Attentive>('attentivechange', { detail: updated.attentive, bubbles: true, composed: true }))
	}

	/** Its menu opens the editor, navigates to the decree, and quick-sets each resolution. */
	protected override contextMenuSpec(): ContextMenuSpec | undefined {
		const target = this.target
		return this.interactive && target ? occurrenceMenu(target, this.applyChange) : undefined
	}

	/** Clicking the title opens the occurrence editor (status, navigate, reschedule). */
	protected override navigate() {
		const target = this.target
		if (!this.interactive || !target) return
		openOccurrenceModal(getApp(), target, this.applyChange)
	}

	protected override get heading() {
		return this.attentive?.decree?.title ?? 'Attentive'
	}

	protected override get directive() {
		return this.attentive?.decree?.directive
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
		// An attentive is always unbound (PEP111), addressed by its RECURRENCE-ID so a still-projected agenda item
		// hardens on interaction instead of failing on an absent row id.
		const occurrence = { decreeId: attentive.decreeId, recurrenceId: attentive.recurrenceId }
		const updated = await core.declaratives.updateAttentive(occurrence, { resolution })
		if (!updated) {
			new Notice('Failed to update attentive.')
			return
		}

		// The update response carries no decree navigation, so the known decree is kept for the directive line.
		const merged: Attentive = { ...attentive, ...updated, decree: updated.decree ?? attentive.decree }
		this.attentive = merged
		this.dispatchEvent(new CustomEvent<Attentive>('attentivechange', { detail: merged, bubbles: true, composed: true }))
	}

	override get info() {
		const attentive = this.attentive
		if (!attentive) {
			return html``
		}

		// The relative chip states the direction itself ("in 2 hours", "3 minutes ago"), ticking live off the shared
		// clock, and — while pending — flags an overdue occurrence red with a clock-alert via warn="past". It carries
		// the exact date/time in its tooltip.
		if (!this.done) {
			return html`
				<p7t-datetime-view relative warn='past' .date=${attentive.epoch.date} .time=${attentive.epoch.timeOfDay}></p7t-datetime-view>
			`
		}

		if (!attentive.resolvedOn) {
			return html``
		}

		return html`
			<p7t-datetime-view relative .date=${attentive.resolvedOn}></p7t-datetime-view>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-attentive-item': AttentiveItem
	}
}
