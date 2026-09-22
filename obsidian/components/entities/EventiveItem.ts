import { component, html, nothing, property } from "@a11d/lit"
import { Eventive } from "@pleiades/sdk"
import { OccurrenceItem } from "./OccurrenceItem"
import { navigateToEntity } from ".."

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

	protected override navigate() {
		if (!this.interactive) return
		const target = this.eventive?.fate?.id ?? this.eventive?.objective?.id
		if (target) navigateToEntity(target)
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
