import { component, css, html, property, state } from '@a11d/lit'
import { CardComponent } from 'components/design'
import { App } from 'obsidian'

@component('p7t-entity-banner')
export class EntityBanner<T extends { id: string, title: string }> extends CardComponent {
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
			${super.styles}

			:host {
				display: flex;
				flex-direction: column;
				align-items: stretch;
			}

			:host::part(header) {
				grid-area: header;
			}

			:host::part(pre-heading) {
				color: color-mix(in srgb, currentColor 60%, transparent);
				font-size: .8em;
				line-height: .8;
			}

			.render-grid {
				margin: 5px;
				display: grid;
				grid-template-columns: 48px 1fr;
				grid-template-rows: auto auto 1fr auto;
				grid-template-areas:
					'icon		header'
					'horizon	secondary'
					'stamp		info'
					'puck		actions';
				gap: 10px;
				align-items: center;

				& .indicator {
					grid-area: 2 / 1;
					display: block;
					border-top: 2px solid var(--text-normal);
					align-self: start;
					margin: .8em 10px;
				}

				& .info {
					grid-area: info;
					display: flex;
					flex-direction: column;
					font-weight: 400;
					font-size: 1.06em;
					font-family: var(--font-text);
				}

				& .actions {
					grid-area: actions;
					display: flex;
					gap: 10px;
					justify-content: flex-end;
				}
			}

			.puck {
				grid-area: puck;
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

	protected override get template() {
		return html`
			<div class='render-grid'>
				<p7t-icon class='icon' icon='${this.icon}'></p7t-icon>
				${this.headerTemplate}
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

	protected override get headingTemplate() {
		return html`<span>${this.entity?.title}</span>`
	}

	protected override get preHeadingTemplate() {
		return html`<span>Pleiades Entity</span>`
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