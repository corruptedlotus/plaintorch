import { Component, component, css, html, property } from "@a11d/lit"
import { IconName } from 'components'

@component('p7t-value-progress')
export class ValueProgress extends Component {
	@property({ type: Number }) value = 0
	@property({ type: Number }) max = 100
	@property() icon: IconName = 'starfire'
	@property({ type: Object }) valueTemplate = (value: number) => html`${value}`
	@property({ type: Object }) maxTemplate = (max: number) => this.valueTemplate(max)

	static override get styles() {
		return css`
			:host {
				display: grid;
				grid-template-columns: auto 1fr;
				grid-template-rows: auto auto;
				grid-template-areas:
					'label extra'
					'bar bar';
				gap: .4em;
			}
			
			.value-box {
				display: flex;
				align-items: center;
				gap: .3ch;
				
				& p7t-icon {
					width: 2em;
					height: 2em;
				}

				& > span {

					font-family: var(--font-text);
					font-weight: 300;
					font-size: 1.1em;
					margin-bottom: -.2em;
					
					& span:last-child {
						color: color-mix(in srgb, currentColor 50%, transparent);
					}
				}

				& .extra::slotted(span) {
					display: inline-block;
					font-family: var(--font-text);
					font-weight: 400;
					font-size: 1.1em;
					opacity: .8;
					margin-bottom: -.2em;
					margin-inline-start: .8ch;
					padding-inline-start: 1ch;
					border-inline-start: 1px solid color-mix(in srgb, currentColor 40%, transparent);
				}
			}
			
			.progress-base {
				grid-area: bar;
				position: relative;
				display: flex;
				align-items: stretch;
				height: .5em;
				border-radius: .5em;
				background-color: color-mix(in srgb, var(--text-normal) 10%, transparent);
				overflow: clip;

				& .progress-bar {
					background-color: color-mix(in srgb, var(--p7t-flare-accent, var(--interactive-accent)) 80%, var(--text-normal));
				}
			}

		`
	}

	protected override get template() {
		return html`
			<slot name='label' style='grid-area: label'>
				<div class='value-box'>
					<p7t-icon icon=${this.icon}></p7t-icon>
					<span>
						${this.valueTemplate(this.value)}<span> / ${this.maxTemplate(this.max)}</span>
					</span>
					<slot class='extra'></slot>
				</div>
			</slot>
			<div class='progress-base'>
				<div class='progress-bar' style='flex-basis: ${Math.min(this.value, this.max) * 100 / this.max}%'></div>
			</div>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-value-progress': ValueProgress
	}
}