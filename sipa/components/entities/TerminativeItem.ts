import { component, html, nothing, property } from "@a11d/lit"
import { TerminativeResolution, type Directive, type Terminative } from "@pleiades/sdk"
import { OccurrenceItem } from "./OccurrenceItem"
import { openTerminativeModal, terminativeMenu, terminativeTitle } from "./TerminativeModal"
import { occurrenceSourceDescriptors, terminativeStateDescriptors } from "./occurrenceDescriptors"
import { occurrenceTime } from "./occurrenceTime"
import { type ContextMenuSpec } from ".."
import "../system/DatetimeView"

/**
 * A single terminative (PEP103 patch 7): the due of an objective, a stellar directive or a checkpoint, listed beside the
 * eventives. Its row names the origin, the origin's directive, and when the due falls — flagged overdue while it is still
 * pending. Its place and resolution follow the origin, so its notch only shows its state; its title opens its editor,
 * which navigates to the origin and raises or lowers the ignore flag. A resolved or ignored terminative is drawn dimmed.
 *
 * A committed change is announced upward with a bubbling `terminativechange` event, and the agenda reconciles.
 */
@component('p7t-terminative-item')
export class TerminativeItem extends OccurrenceItem {
	@property({ type: Object }) terminative?: Terminative

	private get pending() {
		return this.terminative?.resolution === TerminativeResolution.Pending
	}

	override get disabled() {
		return !!this.terminative && (!this.pending || this.terminative.ignored)
	}

	/** Folds a committed change from the modal or the menu back into the row and announces it upward. */
	private readonly applyChange = (updated: Terminative) => {
		this.terminative = updated
		this.dispatchEvent(new CustomEvent<Terminative>('terminativechange', { detail: updated, bubbles: true, composed: true }))
	}

	/** Its menu opens the editor, navigates to the origin, and raises or lowers the ignore flag. */
	protected override contextMenuSpec(): ContextMenuSpec | undefined {
		const terminative = this.terminative
		return this.interactive && terminative ? terminativeMenu(terminative, this.applyChange) : undefined
	}

	/** Clicking the title opens the terminative editor. */
	protected override navigate() {
		const terminative = this.terminative
		if (!this.interactive || !terminative) return
		openTerminativeModal(terminative, this.applyChange)
	}

	protected override get heading() {
		return this.terminative ? terminativeTitle(this.terminative) : 'Due'
	}

	/** The objective's directive, or the stellar directive's parent; a checkpoint names none. */
	protected override get directive(): Directive | undefined {
		return this.terminative?.objective?.directive ?? this.terminative?.directive?.parentDirective
	}

	protected override get notchTemplate() {
		return html`<p7t-icon icon='terminative'></p7t-icon>`
	}

	protected override get occurrence() {
		return this.terminative
	}

	protected override get source() {
		return this.terminative ? occurrenceSourceDescriptors.due : undefined
	}

	protected override get state() {
		return this.terminative ? terminativeStateDescriptors[this.terminative.resolution] : undefined
	}

	protected override get info() {
		return this.moment
	}

	/**
	 * When the due falls: a live relative phrase at its granularity ("Tomorrow", "Today at 18:00"), flagged red once it
	 * has passed while still pending, with the exact moment in its tooltip. A zoned due reads on this device's clock.
	 */
	protected override get moment() {
		const terminative = this.terminative
		if (!terminative) return nothing

		const time = occurrenceTime(terminative.epoch)
		return html`
			<p7t-datetime-view relative warn=${this.pending && !terminative.ignored ? 'past' : 'none'}
				.date=${time.date}
				.time=${time.time}
				.anchor=${time.anchor?.label}
				.granularity=${terminative.epoch.granularity}
				.calendar=${this.ownerCalendar}>
			</p7t-datetime-view>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-terminative-item': TerminativeItem
	}
}
