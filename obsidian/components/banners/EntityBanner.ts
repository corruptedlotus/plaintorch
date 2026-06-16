import { component, css, html, nothing, property, state } from '@a11d/lit'
import { CardComponent } from 'components/design'
import { IconName } from 'components/PleiadesIcon'
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
				gap: 1.2em .6em;
				align-items: center;

				& .icon {
					grid-area: icon;
					align-self: center;
					width: 48px;
					height: 48px;
				}

				& .indicator {
					grid-area: horizon;
					display: block;
					border-top: 2px solid var(--text-normal);
					align-self: start;
					margin: .8em .6em;
				}

				& .secondary {
					grid-area: secondary;
					display: flex;
					flex-direction: column;
					font-weight: 400;
					font-size: 1.1em;
					font-family: var(--font-interface);
					align-items: flex-start;
				}

				& .info {
					grid-area: info;
					display: flex;
					flex-direction: column;
					font-weight: 400;
					font-size: 1.06em;
					font-family: var(--font-text);
					align-items: flex-start;
				}

				& .actions {
					grid-area: actions;
					display: flex;
					gap: .8em;
					justify-content: flex-end;
				}
			}

			.stamp {
				grid-area: stamp;
				height: 40px;
				width: auto;
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
		return !this.entity ? html`` : html`
			<div class='render-grid'>
				<p7t-icon class='icon' icon='${this.icon}'></p7t-icon>
				${this.headerTemplate}

				<span class='indicator'></span>
				<div class='secondary'>
					${this.secondary}
				</div>

				${!this.stamp ? nothing : html`<p7t-icon class='stamp' icon=${this.stamp}></p7t-icon>`}
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
		`
	}
	
	protected get secondary() {
		return html`
			<span>Type: ${this.xtype}</span>
		`
	}

	protected get actions() {
		return html`
			
		`
	}

	protected get stamp() : IconName | undefined {
		return undefined
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-entity-banner': EntityBanner<any>
	}
}