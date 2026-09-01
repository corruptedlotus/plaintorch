import { component, css, html, nothing, property } from '@a11d/lit'
import { InfoItem } from '../design/InfoItem'
import '../design/TimeUnit'

/** Formats a whole-minute working-time unit as "Xh Ym" (or "Ym"), for the tooltip's readable summary. */
function humanizeMinutes(minutes: number | undefined): string {
	if (minutes === undefined) {
		return '—'
	}

	const hours = Math.floor(minutes / 60)
	const remainder = minutes % 60
	return hours > 0 ? `${hours}h${remainder > 0 ? ` ${remainder}m` : ''}` : `${remainder}m`
}

/**
 * An executive's time allocation (PEP098) — the chip a knock shows. It carries the whole allocation state, not just
 * the time: **resolved** once executed, **no allocation** with no estimation, the **time still left** as a working
 * unit while inside the estimate, **active** once past it but within the maximum, and **overworked** beyond it.
 * Its built-in tooltip is the progress summary — the elapsed against the estimation, within the min–max band.
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

			.progress {
				display: flex;
				flex-direction: column;
				gap: .4em;
				min-width: 10em;
			}

			.row {
				display: flex;
				justify-content: space-between;
				gap: 1.2em;
			}

			.key {
				opacity: .6;
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
		const band = this.minimum !== undefined || this.maximum !== undefined
			? `${humanizeMinutes(this.minimum)} – ${humanizeMinutes(this.maximum)}`
			: undefined
		return html`
				${this.executed ? html`
					<div>
						<span class='key'>Executed in</span>
						<span>${humanizeMinutes(this.elapsed)}</span>
					</div>
				` : html`
					<div class='progress'>
						<p7t-value-progress
							icon='lucide:timer'
							.max=${this.estimation}
							.value=${this.elapsed}
							.valueTemplate=${(value: number) => html`<span>${humanizeMinutes(value)}</span>`}>
						</p7t-value-progress>
						${!band ? nothing : html`<div class='row'><span class='key'>Range</span><span>${band}</span></div>`}
					</div>
				`}
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-allocation-item': AllocationItem
	}
}
