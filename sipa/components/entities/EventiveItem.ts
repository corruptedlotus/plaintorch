import { component, html, nothing, property } from "@a11d/lit"
import { Eventive } from "@pleiades/sdk"
import { OccurrenceItem } from "./OccurrenceItem"
import { occurrenceMenu, openOccurrenceModal, type OccurrenceTarget } from "./OccurrenceModal"
import { getApp, type ContextMenuSpec } from ".."

/**
 * A single upcoming eventive — a per-occurrence instance of a fate or of an objective's due date. Eventives
 * happen rather than get done (PEP100), so the notch is display-only; the row surfaces the owner's title,
 * the owner's directive, and when the occurrence falls.
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

	protected override get info() {
		const eventive = this.eventive
		if (!eventive) return nothing
		const timeOfDay = eventive.epoch.timeOfDay
		return html`
			<div style='display: flex; align-items: center; gap: 6px; font-weight: 300; opacity: .8; line-height: .9'>
				<span>${formatDate(eventive.epoch.date)}</span>
				${!timeOfDay ? nothing : html`<span>${formatTime(timeOfDay)}</span>`}
			</div>
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
		openOccurrenceModal(getApp(), target, this.applyChange)
	}
}

/** 'YYYY-MM-DD' → a short 'Mon 12' label, parsed as local time so the day never shifts across a timezone. */
function formatDate(date: string): string {
	const parsed = new Date(`${date}T00:00:00`)
	if (Number.isNaN(parsed.getTime())) return date
	return parsed.toLocaleDateString(undefined, { weekday: 'short', day: 'numeric' })
}

/** 'HH:MM[:SS]' → 'HH:MM'. */
function formatTime(time: string): string {
	return time.slice(0, 5)
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-eventive-item': EventiveItem
	}
}
