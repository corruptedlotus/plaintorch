import { component, css, html, property } from '@a11d/lit'
import { InfoItem } from '../design/InfoItem'
import './AllocationProgress'
import '../design/TimeUnit'

/**
 * An executive's time allocation (PEP098) — the chip a knock shows. It carries the whole allocation state, not just
 * the time: **resolved** once executed, **no allocation** with no estimation, the **time still left** as a working
 * unit while inside the estimate, **active** once past it but within the maximum, and **overworked** beyond it.
 * Its built-in tooltip is the progress summary (see {@link AllocationProgress}) — the elapsed against the
 * estimation, within the min–max band.
 */
@component('p7t-allocation-item')
export class AllocationItem extends InfoItem {
	@property({ type: Boolean }) executed = false
	@property({ type: Number }) estimation?: number
	@property({ type: Number }) minimum?: number
	@property({ type: Number }) maximum?: number
	@property({ type: Number }) elapsed = 0

	static override get styles() {
		return css`
			${super.styles}

			p7t-icon {
				width: 1.9em;
				height: 1.9em;
			}

			p7t-time-unit {
				font-size: 1.5em;
				font-weight: 400;
			}
		`
	}

	protected override get content() {
		if (this.executed) {
			return html`<p7t-icon icon='state-done'></p7t-icon>`
		}

		if (!this.estimation) {
			return html`<p7t-icon icon='state-zero'></p7t-icon>`
		}

		if (this.elapsed < this.estimation) {
			// Still within the estimate: show the time that remains, counting down as it is worked.
			return html`<p7t-time-unit .value=${this.estimation - this.elapsed}></p7t-time-unit>`
		}

		if (this.elapsed < (this.maximum ?? Number.POSITIVE_INFINITY)) {
			return html`<p7t-icon icon='state-active'></p7t-icon>`
		}

		return html`<p7t-icon icon='state-warn'></p7t-icon>`
	}

	protected override get tooltip() {
		return html`
			<p7t-allocation-progress
				?executed=${this.executed}
				.estimation=${this.estimation}
				.minimum=${this.minimum}
				.maximum=${this.maximum}
				.elapsed=${this.elapsed}>
			</p7t-allocation-progress>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-allocation-item': AllocationItem
	}
}
