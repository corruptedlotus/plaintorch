import { Component, component, css, event, html, ifDefined, property } from "@a11d/lit"
import { IconName } from 'components'

@component('p7t-navitem')
export class NavigationItem extends Component {
	@property() key?: string
	@property() icon?: IconName
	@property({ type: Boolean, reflect: true }) active = false
	@event({ bubbles: true }) requestKeyNavigation!: EventDispatcher<string>

	static override get styles() {
		return css`
			:host {
				display: flex;
				flex-direction: column;
				gap: .3em;
				align-items: center;
				font-family: var(--font-text);
				border-radius: 8px;
				padding-inline: 8px;
				padding-top: 5px;
			}

			:host(:hover) {
				background-color: var(--background-secondary);
			}

			:host([active]) {
				color: color-mix(in srgb, var(--interactive-accent) 70%, var(--text-normal));
			}

			.main {
				display: flex;
				align-items: center;
				gap: 0;
				transition: ease .3s;
				
				:host([active]) & {
					gap: .5em;
				}
			}

			.icon {
				width: 28px;
				height: 28px;
			}

			.title {
				width: 0px;
				transition: ease .3s;
				font-size: 1.2em;
				font-weight: 300;
				interpolate-size: allow-keywords;
				overflow: hidden;
				white-space: nowrap;
				opacity: 0;
				
				:host([active]) & {
					width: auto;
					opacity: 1;
				}
			}

			.indicator {
				grid-area: indicator;
				height: 3px;
				border-radius: 3px;
				background-color: currentColor;
				width: 8px;
				transition: ease .6s;
				opacity: .2;

				:host([active]) & {
					width: 2em;
					opacity: 1;
				}
			}
		`
	}

	protected override get template() {
		return html`
			<div class='main'>
				<p7t-icon class='icon' icon=${ifDefined(this.icon)}></p7t-icon>
				<span class='title'>
					<slot></slot>
				</span>
			</div>
			<div class='indicator'></div>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-navitem': NavigationItem
	}
}