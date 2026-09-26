import { component, html, nothing, property } from "@a11d/lit"
import { Eventive, type DeclarativeCalendar } from "@pleiades/sdk"
import { OccurrenceItem } from "./OccurrenceItem"
import "../system/DatetimeView"
import { occurrenceMenu, openOccurrenceModal, type OccurrenceTarget } from "./OccurrenceModal"
import { type ContextMenuSpec } from ".."

/**
 * A single upcoming eventive — a per-occurrence instance of a fate or of an objective's due date. Eventives
 * happen rather than get done (PEP100), so the notch is display-only; the row surfaces the owner's title,
 * the owner's directive, and when the occurrence falls — read at the occurrence's granularity, on its fate's
 * calendar (PEP111), like the attentive rows beside it.
 */
@component('p7t-eventive-item')
export class EventiveItem extends OccurrenceItem {
	@property({ type: Object }) eventive?: Eventive

	/**
	 * The owning fate's calendar, supplied by a host that knows it when the eventive arrives without its fate (a fate's
	 * own agenda lists them bare). The eventive's own fate wins when it carries one.
	 */
	@property({ type: Number }) calendar?: DeclarativeCalendar

	protected override get heading() {
		return this.eventive?.fate?.title ?? this.eventive?.objective?.title ?? 'Eventive'
	}

	protected override get directive() {
		return this.eventive?.fate?.directive ?? this.eventive?.objective?.directive
	}

	protected override get notchTemplate() {
		return html`<p7t-icon icon='eventive'></p7t-icon>`
	}

	protected override get info() {
		const eventive = this.eventive
		if (!eventive) return nothing
		// A live relative phrase at the occurrence's granularity ("Tomorrow at 18:00", "This week"), falling back to the
		// absolute reading beyond its reach, with the exact moment in its tooltip.
		return html`
			<p7t-datetime-view relative
				.date=${eventive.epoch.date}
				.time=${eventive.epoch.timeOfDay}
				.granularity=${eventive.epoch.granularity}
				.calendar=${eventive.fate?.calendar ?? this.calendar}>
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
