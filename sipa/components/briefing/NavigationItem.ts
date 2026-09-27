import { Component, component, css, event, html, ifDefined, property } from "@a11d/lit"
import { IconName } from '..'

@component('p7t-navitem')
export class NavigationItem extends Component {
	@property() key?: string
	@property() icon?: IconName
	@property({ type: Boolean, reflect: true }) active = false
	@event({ bubbles: true, composed: true }) requestKeyNavigation!: EventDispatcher<string>

	static override get styles() {
		return css`
			:host {
				display: flex;
				flex-direction: column;
				gap: .3em;
				align-items: center;
				font-family: var(--font-text);
				border-radius: 12px 12px 0 0;
				padding-inline: 12px;
				padding-top: .3em;
				transition: .3s ease;
				border: 1px solid transparent;
				border-bottom: none;
				cursor: pointer;
			}

			:host(:hover) {
				background-color: var(--background-secondary);
			}

			:host([active]) {
				color: color-mix(in srgb, var(--interactive-accent) 70%, var(--text-normal));
				/*border-color: color-mix(in srgb, var(--interactive-accent) 30%, transparent);*/
				background-color: color-mix(in srgb, black 20%, transparent);
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
				font-size: 1.05em;
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
				height: 2px;
				background-color: currentColor;
				width: 8px;
				transition: ease .6s;
				opacity: 0;

				:host(:not([active])) & {
					width: .6em;
					opacity: .3;
					border-radius: 3px 3px 0 0;
				}

				:host(:not([active]):hover) & {
					width: 1em;
					opacity: .3;
					border-radius: 3px 3px 0 0;
				}
			}
		`
	}

	protected override get template() {
		return html`
			<div class='main' @click=${() => this.requestKeyNavigation.dispatch(this.key ?? '')}>
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