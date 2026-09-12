import { Component, component, css, html, nothing, property } from '@a11d/lit'
import '../design/ValueProgress'

/** Formats a whole-minute working-time unit as "Xh Ym" (or "Ym"), for the readable summary. */
function humanizeMinutes(minutes: number | undefined): string {
	if (minutes === undefined) {
		return '—'
	}

	const hours = Math.floor(minutes / 60)
	const remainder = minutes % 60
	return hours > 0 ? `${hours}h${remainder > 0 ? ` ${remainder}m` : ''}` : `${remainder}m`
}

/**
 * The allocation progress summary drawn in {@link AllocationItem}'s tooltip — elapsed against the estimation
 * within the min–max band, or the executed total. A self-contained element (its own shadow root and styles) so it
 * renders identically wherever the tooltip system places it, independent of any host's shadow scope.
 */
@component('p7t-allocation-progress')
export class AllocationProgress extends Component {
	@property({ type: Boolean }) executed = false
	@property({ type: Number }) estimation?: number
	@property({ type: Number }) minimum?: number
	@property({ type: Number }) maximum?: number
	@property({ type: Number }) elapsed = 0

	static override get styles() {
		return css`
			:host { display: contents; }

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

	protected override get template() {
		if (this.executed) {
			return html`
				<div>
					<span class='key'>Executed in</span>
					<span>${humanizeMinutes(this.elapsed)}</span>
				</div>
			`
		}

		const band = this.minimum !== undefined || this.maximum !== undefined
			? `${humanizeMinutes(this.minimum)} – ${humanizeMinutes(this.maximum)}`
			: undefined
		return html`
			<div class='progress'>
				<p7t-value-progress
					icon='lucide:timer'
					.max=${this.estimation}
					.value=${this.elapsed}
					.valueTemplate=${(value: number) => html`<span>${humanizeMinutes(value)}</span>`}>
				</p7t-value-progress>
				${!band ? nothing : html`<div class='row'><span class='key'>Range</span><span>${band}</span></div>`}
			</div>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-allocation-progress': AllocationProgress
	}
}
