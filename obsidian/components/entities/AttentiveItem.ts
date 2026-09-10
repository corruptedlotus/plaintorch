import { component, css, html, property } from "@a11d/lit"
import { Attentive, AttentiveResolution } from "@pleiades/sdk"
import { Notice } from "obsidian"
import { OccurrenceItem } from "./OccurrenceItem"
import { core } from ".."
import { Temporal } from "@js-temporal/polyfill"

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

	static override get styles() {
		return css`
			${super.styles}

			.timer {
				font-weight: 400;
				display: inline-flex;
				align-items: center;
				gap: .4ch;

				&.past {
					color: var(--text-error);
				}

				&.future {
					opacity: .6;
				}
			}
		`
	}

	private get done() {
		return this.attentive?.resolution === AttentiveResolution.Done
	}

	override get disabled() {
		return this.done
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
		// Address the occurrence by its RECURRENCE-ID (plus cycle when bound) so a still-projected agenda item
		// hardens on interaction instead of failing on an absent row id.
		const occurrence = {
			decreeId: attentive.decreeId,
			recurrenceDate: attentive.recurrenceDate,
			recurrenceTime: attentive.recurrenceTime,
			polarisCycleId: attentive.polarisCycleId
		}
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
		if (!this.done) {
			const epoch = Temporal.PlainDateTime.from(`${this.attentive?.date ?? ''}T${this.attentive?.time ?? ''}`)
			const past = epoch.since(Temporal.Now.plainDateTimeISO()).sign === -1

			return html`
				<span class='timer ${past ? 'past' : 'future'}'>
					${past ? html`` : html`in`}
					<p7t-elapsed-view absolute showDays .epoch=${epoch.toString()}></p7t-elapsed-view>
					${past ? html`<p7t-icon icon='lucide:clock-alert'></p7t-icon>` : html``}
				</span>
			`
		} else {
			const epoch = Temporal.Instant.from(this.attentive?.resolvedOn ?? '')
				.toZonedDateTimeISO('UTC').withTimeZone(Intl.DateTimeFormat().resolvedOptions().timeZone).toPlainDateTime()

			return html`
				<span class='timer'>
					<p7t-elapsed-view absolute showDays .epoch=${epoch.toString()}></p7t-elapsed-view>
					ago
				</span>
			`
		}
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-attentive-item': AttentiveItem
	}
}
