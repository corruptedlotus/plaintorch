/**
 * A view that shows the elapsed time from a given epoch, updating every second off the app-wide {@link TickController}
 * (one shared timer, not a per-view interval).
 */

import { Component, component, css, html, nothing, property } from "@a11d/lit";
import { TickController } from "./globalTick";

@component('p7t-elapsed-view')
export class LiveElapsedView extends Component {
	@property({ type: Object }) epoch = new Date()
	@property({ type: Boolean }) showDays = false
	@property({ type: Boolean }) absolute = false

	/** Re-renders once a second off the shared app tick. */
	protected readonly tick = new TickController(this)

	static override get styles() {
		return css`
			:host {
				font-variant-numeric: tabular-nums;
			}

			small {
				opacity: 0.7;
				font-size: 0.7em;
			}
		`
	}

	protected override get template() {
		const elapsedSecondsRaw = Math.floor((Date.now() - new Date(this.epoch).getTime()) / 1000)
		const elapsedSeconds = Math.abs(elapsedSecondsRaw)
		const elapsedMinutes = Math.floor(elapsedSeconds / 60)
		const elapsedHoursRaw = Math.floor(elapsedMinutes / 60)
		const elapsedHours = this.showDays ? Math.floor(elapsedHoursRaw % 24) : elapsedHoursRaw
		const elapsedDays = Math.floor(elapsedHoursRaw / 24)

		return html`
			${!this.showDays || !elapsedDays ? nothing : html`
				<span>${!this.absolute ? html`<span>${elapsedSecondsRaw < 0 ? '-' : ''}</span>` : nothing}${elapsedDays.toString()}<small>d</small></span>
			`}
			<span>
				${!this.absolute && (!this.showDays || !elapsedDays) ? html`<span>${elapsedSecondsRaw < 0 ? '-' : ''}</span>` : nothing}${elapsedHours.toString().padStart(2, '0')}<small>h</small>
			</span>
			<span>
				${(elapsedMinutes % 60).toString().padStart(2, '0')}'
			</span>
			<small>${(elapsedSeconds % 60).toString().padStart(2, '0')}</small>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-elapsed-view': LiveElapsedView
	}
}