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
				--allocation-bar-inactive: color-mix(in srgb, var(--text-normal) 30%, var(--background-primary));
			}

			.rail {
				position: relative;
				height: .8em;
			}

			.full,
			.track,
			.fill,
			.extra {
				position: absolute;
				top: 50%;
				left: 0;
				translate: 0 -50%;
				border-radius: 1em;
			}

			.full {
				height: .1em;
				background-color: var(--allocation-bar-inactive);
			}

			.track {
				height: .3em;
				background-color: var(--allocation-bar-inactive);
			}

			.fill {
				height: .4em;
				background-color: var(--p7t-flare-accent, var(--interactive-accent));
			}

			.extra {
				height: .16em;
				background-color: var(--p7t-flare-accent, var(--interactive-accent));
				background: repeating-linear-gradient(145deg, black 0 2px, var(--p7t-flare-accent, var(--interactive-accent)) 2px 4px);
				filter: saturate(3) brightness(1.2);
			}

			.cap {
				position: absolute;
				top: 50%;
				translate: -50% -50%;
				width: .3em;
				height: 1.1em;
				border-radius: .4em;
				background-color: var(--allocation-bar-inactive);
			}

			.marker {
				position: absolute;
				top: 50%;
				width: 1em;
				height: 1em;
				translate: -50% -50%;
				rotate: 45deg;
				border-radius: .16em;
				background-color: var(--allocation-bar-inactive);
				transition: background-color .3s ease;
				display: flex;
				align-items: center;
				justify-content: center;

				p7t-icon {
					rotate: -45deg;
					font-size: .7em;
					color: var(--background-primary);
					font-weight: 600;
				}
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
				<div class='full' style='width: 100%'></div>
				<div class='track' style='width: ${offset(this.estimation)}'></div>
				${this.estimation <= 0 ? nothing : html`
					<div class='cap' style='left: ${offset(this.estimation)}'></div>
				`}
				<div class='fill' style='width: ${offset(Math.min(this.elapsed, this.maximum))}'></div>
				<div class='extra' style='left: ${offset(this.maximum)}; width: ${offset(this.elapsed - this.maximum)}'></div>
				${this.minimum <= 0 ? nothing : html`
					<div
						class='marker'
						?reached=${this.elapsed >= this.minimum}
						style='left: ${offset(this.minimum)}'>
						<p7t-icon icon='lucide:arrow-down'></p7t-icon>
					</div>
				`}
				${this.maximum <= 0 ? nothing : html`
					<div
						class='marker'
						?reached=${this.elapsed >= this.maximum}
						style='left: ${offset(this.maximum)}'>
						<p7t-icon icon='lucide:arrow-up'></p7t-icon>
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
