import { Component, component, css, html, property, state } from '@a11d/lit'
import { App } from 'obsidian'

@component('p7t-entity-banner')
export class EntityBanner<T extends { id: string, title: string }> extends Component {
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
				
				& .heading {
					display: flex;
					flex-direction: column;
				}

				& .subheading {
					font-size: .7em;
					font-weight: 400;
					opacity: .5;
					display: block;
					grid-area: 1 / 2;
				}

				& h1 {
					grid-area: 1 / 2;
					margin-block: -5px 5px;
					font-family: var(--font-interface);
					font-weight: 600;
				}

				& .indicator {
					grid-area: 2 / 1;
					display: block;
					border-top: 2px solid var(--text-normal);
					align-self: start;
					margin: .8em 10px;
				}

				& .info {
					grid-area: 2 / 2;
					display: flex;
					flex-direction: column;
					font-weight: 400;
					font-size: 1.06em;
					font-family: var(--font-text);
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
				flex-direction: row;
				align-items: center;
				align-self: flex-end;
				gap: 3px;
				margin-bottom: -.4rem;

				& p7t-icon {
					height: 32px;
					width: 32px;
					flex: 0 0 32px;
				}

				& pre {
					margin: 0;
					font-size: .6em;
				}

			}
		`
	}

	protected override render() {
		return html`
			<div class='render-grid'>
				<p7t-icon class='icon' icon='${this.icon}'></p7t-icon>
				<div class='heading'>
					<span class='subheading'>${this.subheading}</span>
					<h1>${this.heading}</h1>
				</div>
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
		return html`${this.entity?.title}`
	}

	protected get subheading() {
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