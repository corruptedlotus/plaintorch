import { Component, component, css, html, nothing, property } from "@a11d/lit"

/**
 * Progress bar for an executive's time allocations, all expressed in whole-minute working time units.
 *
 * The bar proper is scaled to the estimation: its track runs from zero to `estimation` and carries a
 * cap at that end. When a `maximum` reaches beyond the estimation the widget extends past the track to
 * accommodate it, so the extended stretch is what the maximum marker lives on. The fill tracks
 * `elapsed`, and both the minimum and maximum markers switch to the accent colour once elapsed
 * has reached them.
 */
@component('p7t-allocation-bar')
export class AllocationBar extends Component {
	@property({ type: Number }) elapsed = 0
	@property({ type: Number }) estimation = 0
	@property({ type: Number }) minimum = 0
	@property({ type: Number }) maximum = 0

	static override get styles() {
		return css`
			:host {
				display: block;
				padding-block: .9em;
			}

			.rail {
				position: relative;
				height: .8em;
			}

			.track,
			.fill {
				position: absolute;
				top: 50%;
				left: 0;
				translate: 0 -50%;
				border-radius: 1em;
			}

			.track {
				height: .25em;
				background-color: color-mix(in srgb, var(--text-normal) 22%, transparent);
			}

			.fill {
				height: .55em;
				background-color: var(--p7t-flare-accent, var(--interactive-accent));
			}

			.cap {
				position: absolute;
				top: 50%;
				translate: -50% -50%;
				width: .16em;
				height: 1.1em;
				border-radius: 1em;
				background-color: color-mix(in srgb, var(--text-normal) 30%, transparent);
			}

			.marker {
				position: absolute;
				top: 50%;
				width: .62em;
				height: .62em;
				translate: -50% -50%;
				rotate: 45deg;
				border-radius: .12em;
				background-color: color-mix(in srgb, var(--text-normal) 32%, transparent);
				transition: background-color .3s ease;
			}

			.marker[reached] {
				background-color: var(--p7t-flare-accent, var(--interactive-accent));
			}
		`
	}

	protected override get template() {
		// Everything is positioned against the widest of the values so nothing can overflow the rail.
		const domain = Math.max(this.estimation, this.maximum, this.elapsed, 1)
		const offset = (value: number) => `${Math.min(Math.max(value, 0), domain) * 100 / domain}%`

		return html`
			<div class='rail'>
				<div class='track' style='width: ${offset(this.estimation)}'></div>
				<div class='fill' style='width: ${offset(this.elapsed)}'></div>
				${this.estimation <= 0 ? nothing : html`
					<div class='cap' style='left: ${offset(this.estimation)}'></div>
				`}
				${this.minimum <= 0 ? nothing : html`
					<div
						class='marker'
						?reached=${this.elapsed >= this.minimum}
						style='left: ${offset(this.minimum)}'>
					</div>
				`}
				${this.maximum <= 0 ? nothing : html`
					<div
						class='marker'
						?reached=${this.elapsed >= this.maximum}
						style='left: ${offset(this.maximum)}'>
					</div>
				`}
			</div>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-allocation-bar': AllocationBar
	}
}
