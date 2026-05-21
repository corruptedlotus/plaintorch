import { Component, component, css, html, property, state } from '@a11d/lit'
import { App } from 'obsidian'

@component('p7t-entity-banner')
export class EntityBanner<T> extends Component {
	@property() xtype?: string
	@property() puck = ''

	@state() entity?: T
	
	app?: App

	readonly icon: string = 'plaintorch'

	async fetchEntity(puck: string): Promise<T | undefined> {
		return Promise.resolve(undefined)
	}

	protected override async initialized() {
		this.entity = await this.fetchEntity(this.puck)
	}

	static override get styles() {
		return css`
			:host {
				display: flex;
				flex-direction: column;
				align-items: stretch;
			}

			.render-grid {
				margin: 5px;
				display: grid;
				grid-template-columns: 48px 1fr;
				gap: 10px;
				align-items: center;
				font-family: var(--font-interface);

				& .icon {
					grid-area: 1 / 1;
				}

				& h2 {
					grid-area: 1 / 2;
					margin-block: 5px;
				}

				& .indicator {
					grid-area: 2 / 1;
					display: block;
					border-top: 2px solid var(--text-normal);
					align-self: start;
					margin: .8em 5px;
				}

				& .info {
					grid-area: 2 / 2;
					display: flex;
					flex-direction: column;
					font-weight: 400;
					font-size: 1.1em;
				}

				& .actions {
					grid-area: 3 / 2;
					display: flex;
					gap: 10px;
					justify-content: flex-end;
				}
			}

			button {
				background-color: rgba(0, 0, 0, .3);
				outline: none;
				border: 1px solid rgba(255, 255, 255, 0.05);
				display: flex;
				align-items: center;
				gap: 5px;
				vertical-align: middle;
				padding: 4px 14px 4px 10px;
				font-family: var(--font-interface);
				border-radius: 6px;

				& p7t-icon {
					width: 32px;
					height: 32px;
				}

				&:hover {
					background-color: rgba(0, 0, 0, .6);
					border-color: rgba(255, 255, 255, 0.2);
				}
			}

			.puck {
				grid-area: 3 / 1;
				opacity: .6;
				display: flex;
				font-weight: 200;
				flex-direction: column;
				align-items: center;

				& p7t-icon {
					height: 36px;
					margin-bottom: -.3em;
				}

				& pre {
					margin: 0;
					font-size: .6em;

					&::before {
						content: '[';
						opacity: .4;
					}
	
					&::after {
						content: ']';
						opacity: .4;
					}
				}

			}
		`
	}

	protected override render() {
		return html`
			<div class='render-grid'>
				<p7t-icon class='icon' icon='${this.icon}'></p7t-icon>
				<h2>${this.heading}</h2>
				<span class='indicator'></span>
				<div class='info'>
					${this.info}
				</div>
				<div class='actions'>
					${this.actions}
				</div>
				<div class='puck'>
					<p7t-icon class='icon' icon='puck'></p7t-icon>
					<pre>${this.puck}</pre>
				</div>
			</div>
		`
	}

	protected get heading() {
		return html`Pleiades Entity`
	}

	protected get info() {
		return html`
			<span>Type: ${this.xtype}</span>
		`
	}

	protected get actions() {
		return html`
			
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-entity-banner': EntityBanner<any>
	}
}