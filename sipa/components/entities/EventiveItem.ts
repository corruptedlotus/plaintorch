import { component, html, nothing, property } from "@a11d/lit"
import { Eventive } from "@pleiades/sdk"
import { OccurrenceItem } from "./OccurrenceItem"
import "../system/DatetimeView"
import { occurrenceMenu, openOccurrenceModal, type OccurrenceTarget } from "./OccurrenceModal"
import { eventiveStateDescriptors } from "./occurrenceDescriptors"
import { occurrenceTime } from "./occurrenceTime"
import { type ContextMenuSpec } from ".."

/**
 * A single upcoming eventive — a per-occurrence instance of a fate or of an objective's due date. Eventives
 * happen rather than get done (PEP100), so the notch is display-only; the row surfaces the owner's title,
 * the owner's directive, and when the occurrence falls — read at the occurrence's granularity, on its fate's
 * calendar (PEP111), like the attentive rows beside it. As a {@link mini} line it reads its resolution, its time,
 * and whether it came from the fate's orbit or was added by hand.
 */
@component('p7t-eventive-item')
export class EventiveItem extends OccurrenceItem {
	@property({ type: Object }) eventive?: Eventive


	protected override get heading() {
		return this.eventive?.fate?.title ?? this.eventive?.objective?.title ?? 'Eventive'
	}

	protected override get directive() {
		return this.eventive?.fate?.directive ?? this.eventive?.objective?.directive
	}

	protected override get notchTemplate() {
		return html`<p7t-icon icon='eventive'></p7t-icon>`
	}

	/** Born of its fate's orbit, when it carries its fate. */
	protected override get schedule() {
		return this.eventive?.fate
	}

	protected override get occurrence() {
		return this.eventive
	}

	protected override get state() {
		return this.eventive ? eventiveStateDescriptors[this.eventive.resolution] : undefined
	}

	protected override get info() {
		const eventive = this.eventive
		if (!eventive) return nothing
		// A live relative phrase at the occurrence's granularity ("Tomorrow at 18:00", "This week"), falling back to the
		// absolute reading beyond its reach, with the exact moment in its tooltip. A zoned occurrence reads on this
		// device's clock, its own wall time in the tooltip.
		const time = occurrenceTime(eventive.epoch)
		return html`
			<p7t-datetime-view relative
				.date=${time.date}
				.time=${time.time}
				.anchor=${time.anchor?.label}
				.granularity=${eventive.epoch.granularity}
				.calendar=${this.ownerCalendar}>
			</p7t-datetime-view>
		`
	}

	private get target(): OccurrenceTarget | undefined {
		return this.eventive ? { kind: 'eventive', eventive: this.eventive } : undefined
	}

	/** Folds a committed change from the modal or the menu back into the row. */
	private readonly applyChange = (updated: OccurrenceTarget) => {
		if (updated.kind !== 'eventive') return
		this.eventive = updated.eventive
		this.dispatchEvent(new CustomEvent<OccurrenceTarget>('occurrencechange', { detail: updated, bubbles: true, composed: true }))
	}

	/** Its menu opens the editor, navigates to the fate/objective, and quick-sets each resolution. */
	protected override contextMenuSpec(): ContextMenuSpec | undefined {
		const target = this.target
		return this.interactive && target ? occurrenceMenu(target, this.applyChange) : undefined
	}

	/** Clicking the title opens the occurrence editor (status, navigate, reschedule). */
	protected override navigate() {
		const target = this.target
		if (!this.interactive || !target) return
		openOccurrenceModal(target, this.applyChange)
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-eventive-item': EventiveItem
	}
}
