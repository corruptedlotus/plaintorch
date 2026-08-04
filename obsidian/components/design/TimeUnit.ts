import { Component, component, css, html, property } from "@a11d/lit";

@component('p7t-time-unit')
export class TimeUnit extends Component {
	@property({ type: Number }) value?: number

	static override get styles() {
		return css`
			:host {
				display: inline-flex;
				align-items: center;
			}

			svg {
				margin: 0.02ch 0.15ch;
				width: 0.7ch;
				height: 0.7ch;
				align-self: flex-start;
			}

			span {
				line-height: .9em;
				vertical-align: middle;
			}

			.cls-1 {
				fill: none;
				stroke: var(--p7t-time-unit-arc, var(--p7t-flare-accent, var(--interactive-accent)));
				stroke-linecap: round;
				stroke-miterlimit: 10;
				stroke-width: 7px;
				transform: rotate(-90deg);
				transform-origin: center;
			}

			.cls-2 {
				fill: currentColor;
				opacity: .4;
			}
		`
	}

	override get template() {
		if (this.value !== undefined) {
			const minf = (this.value % 60) / 60
			return html`
				<span>${Math.floor(this.value / 60)}</span>
				<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 36.46 36.46">
					<defs>
						<style>
							.cls-1 {
								stroke-dasharray: ${minf * 92.52} ${92.52 - minf * 92.52};
							}
						</style>
					</defs>
					<circle class="cls-2" cx="18.23" cy="3.5" r="3.5"/>
					<circle class="cls-2" cx="32.96" cy="18.23" r="3.5"/>
					<circle class="cls-2" cx="18.23" cy="32.96" r="3.5"/>
					<circle class="cls-2" cx="3.5" cy="18.23" r="3.5"/>
					<circle class="cls-1" cx="18.23" cy="18.23" r="14.73"/>
				</svg>
			`
		} else {
			return html`<span>?</span>`
		}
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-time-unit': TimeUnit
	}
}