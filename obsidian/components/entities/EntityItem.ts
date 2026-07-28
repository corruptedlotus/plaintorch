import { component, Component, css, html, HTMLTemplateResult, nothing, property } from '@a11d/lit'
import { EntityWatch, navigateToEntity } from '..'

@component('p7t-entity-item')
export class EntityItem<T extends { id: string, title: string }> extends Component {
	@property({ type: Object }) entity?: T
	@property({ type: Boolean, reflect: true }) interactive = false

	/**
	 * The entity arrives as a property from whichever aggregate rendered this item, and that instance is
	 * canonical. Observing it is what makes a list row follow an edit made in a banner elsewhere.
	 */
	protected readonly watch = new EntityWatch(this, () => this.entity)

	protected async navigateToEntity() {
		if (!this.interactive) return
		navigateToEntity(this.entity!.id)
	}

	static override get styles() {
		return css`
			@keyframes fade-in {
				from {
					opacity: 0;
					transform: translateY(-1em);
				}
			}

			:host {
				--flare-intensity: 0%;
				display: flex;
				align-items: center;
				justify-content: stretch;
				padding-inline: 5px 10px;
				padding-block: 8px;
				font-family: var(--font-interface);
				transition: .6s ease;
				border-radius: 12px;
				box-sizing: border-box;
				position: relative;
				anchor-name: --entity-item;
				gap: .5em;
				animation: fade-in .3s ease;
			}
			
				:host(:not([interactive])) {
					padding: 0;
					pointer-events: none;
				}

				:host([interactive]:hover) {
					background-color: color-mix(in srgb, var(--text-normal) 10%, transparent);
				}
			
			.grid {
				flex: 1;
				anchor-scope: --entity-item;
				display: grid;
				gap: 0 .6ch;
				grid-template-columns: auto 1fr;
				grid-template-rows: auto auto;
				grid-template-areas:
					"notch toplane"
					"notch title";
				align-items: center;
			}
			
			.notch {
				display: flex;
				align-items: stretch;
				justify-content: center;
				grid-area: notch;
				padding: 4px;
				box-sizing: border-box;
				position: relative;

				& ::slotted(p7t-icon),
				& p7t-icon {
					width: 1.9em;
					height: 1.9em;
				}
			}
			
			.toplane {
				display: flex;
				align-items: center;
				gap: 5px;
				grid-area: toplane;
				font-family: var(--font-text);
				margin-top: -2px;
				
				& .filler {
					flex: 1;
				}
			}

			.title {
				font-weight: 300;
				font-size: 1.3em;
				line-height: 1;
				margin-block: -.1em .1em;
				grid-area: title;
				display: flex;
				align-items: center;
				justify-content: flex-start;
				cursor: pointer;
			}

			.extra-action {
				position: fixed;
				display: flex;
				align-items: center;
				justify-content: center;
				padding: .2em;
				position-anchor: --entity-item;
				/*position-area: inline-end span-all;*/
				top: anchor(top);
				bottom: anchor(bottom);
				left: anchor(right);
				background-color: color-mix(in srgb, var(--p7t-flare-accent, var(--interactive-accent)) 60%, black);
				z-index: 30;
				interpolate-size: allow-keywords;
				transform: translateX(0);
				margin-block: .3em;
				margin-inline: -.4em;
				width: 2em;
				cursor: pointer;

				@starting-style {
					opacity: 0;
					transform: translateX(-3em);
				}

				:host(:not(:hover)) & {
					opacity: 0;
					display: none;
					transform: translateX(-3em);
				}

				& p7t-icon {
					width: 1.9em;
					height: 1.9em;
				}
			}

			.part {
				border-radius: 8px;
				border: 1px solid transparent;
				transition: .3s ease;

				&:hover {
					border-color: color-mix(in srgb, var(--p7t-flare-accent, var(--interactive-accent)) 60%, transparent);
				}
			}
		`
	}

	protected override get template() {
		return html`
			<div class='grid'>
				<div @click=${async () => await this.notchAction()} class='notch part'>${this.notch}</div>
				<div class='toplane'>
					${this.preTitle}
					<div class='filler'></div>
					${this.info}
				</div>
				<div class='title'>
					<span @click=${() => this.navigateToEntity()}>${this.entity?.title}</span>
				</div>
			</div>
			${this.highlightInfo}
			${!this.extraActionTemplate ? nothing : html`
				<div @click=${async () => await this.extraAction()} class='extra-action part'>${this.extraActionTemplate}</div>
			`}
		`
	}

	protected get highlightInfo() : HTMLTemplateResult | undefined {
		return html``
	}

	protected get preTitle() {
		return html``
	}

	protected get info() {
		return html``
	}

	protected get notch() {
		return html`
			<slot>
				<p7t-icon class='notch-icon' icon='state-active'></p7t-icon>
			</slot>
		`
	}

	protected get extraActionTemplate() : HTMLTemplateResult | undefined {
		return undefined
	}

	protected async extraAction() { }

	protected async notchAction() { }
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-entity-item': EntityItem<unknown & { id: string, title: string }>
	}
}