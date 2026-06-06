/**
 * A view that shows the elapsed time from a given epoch, updating every second.
 */

import { Component, component, css, html, property } from "@a11d/lit";

@component('p7t-elapsed-view')
export class LiveElapsedView extends Component {
	@property() epoch = new Date()

	private intervalId?: number

	override connectedCallback() {
		super.connectedCallback()
		this.intervalId = window.setInterval(() => this.requestUpdate(), 1000)
	}

	override disconnectedCallback() {
		super.disconnectedCallback()
		if (this.intervalId) {
			clearInterval(this.intervalId)
		}
	}

	static override get styles() {
		return css`
			:host {
			}

			small {
				opacity: 0.7;
			}
		`
	}

	protected override get template() {
		const elapsedSeconds = Math.floor((Date.now() - new Date(this.epoch).getTime()) / 1000)
		const elapsedMinutes = Math.floor(elapsedSeconds / 60)
		const elapsedHours = Math.floor(elapsedMinutes / 60)

		return html`
			<span>${elapsedHours}<small>h</small></span>
			<span>${elapsedMinutes % 60}'</span>
			<small>${elapsedSeconds % 60}<small>s</small></small>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-elapsed-view': LiveElapsedView
	}
}