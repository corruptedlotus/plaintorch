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
 * An executive's time allocation (PEP098) — a working-time unit — drawn through {@link TimeUnit}, the chip the notch
 * ("knock") of an executive shows. Its built-in tooltip is the progress summary: the elapsed against the estimation,
 * within the min–max band.
 *
 * NOTE: an extra behaviour beyond display (to be specified) will hang off this chip; the wrapper and its progress
 * tooltip are in place for it to build on.
 */
@component('p7t-allocation-item')
export class AllocationItem extends InfoItem {
	/** The value the face shows — usually the time still left, or the estimation. */
	@property({ type: Number }) value?: number
	@property({ type: Number }) estimation?: number
	@property({ type: Number }) minimum?: number
	@property({ type: Number }) maximum?: number
	@property({ type: Number }) elapsed?: number

	static override get styles() {
		return css`
			${super.styles}

			.progress {
				display: flex;
				flex-direction: column;
				gap: .2em;
				min-width: 10em;
			}

			.progress .row {
				display: flex;
				justify-content: space-between;
				gap: 1.2em;
			}

			.progress .key {
				opacity: .6;
			}
		`
	}

	protected override get content() {
		return html`<p7t-time-unit .value=${this.value}></p7t-time-unit>`
	}

	protected override get tooltip() {
		if (this.estimation === undefined && this.elapsed === undefined) {
			return nothing
		}

		const band = this.minimum !== undefined || this.maximum !== undefined
			? `${humanizeMinutes(this.minimum)} – ${humanizeMinutes(this.maximum)}`
			: undefined
		return html`
			<div class='progress'>
				<div class='row'><span class='key'>Elapsed</span><span>${humanizeMinutes(this.elapsed ?? 0)}</span></div>
				<div class='row'><span class='key'>Estimation</span><span>${humanizeMinutes(this.estimation)}</span></div>
				${!band ? nothing : html`<div class='row'><span class='key'>Range</span><span>${band}</span></div>`}
			</div>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-allocation-item': AllocationItem
	}
}
