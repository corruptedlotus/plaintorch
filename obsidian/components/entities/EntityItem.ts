import { component, Component, css, html, HTMLTemplateResult, nothing, property } from '@a11d/lit'
import { EntityWatch, navigateToEntity } from '..'
import { itemLayoutStyles } from './itemStyles'

@component('p7t-entity-item')
export class EntityItem<T extends { id: string, title: string }> extends Component {
	@property({ type: Object }) entity?: T
	@property({ type: Boolean, reflect: true }) interactive = false

	get disabled() { return false }

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
			${itemLayoutStyles}

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
		`
	}

	protected override get template() {
		return html`
			<div class='grid ${this.disabled ? 'disabled' : ''}'>
				<div @click=${async () => await this.notchAction()} class='notch part'>${this.notchTemplate}</div>
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

	protected get notchTemplate() {
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