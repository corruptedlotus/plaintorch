import { Component, component, css, html, nothing, property } from "@a11d/lit";

@component('p7t-card')
export class CardComponent extends Component {
	static override get styles() {
		return css`
			:host {
				display: flex;
				flex-direction: column;
				align-items: stretch;
				margin: 0 0 1rem;
				padding: 0.9rem 0.9rem;
				border: 1px solid color-mix(in srgb, var(--background-modifier-border) 70%, transparent);
				border-radius: 20px;
				background: linear-gradient(
					40deg,
					color-mix(in srgb, var(--p7t-flare-accent, var(--interactive-accent)) 50%, transparent) -30%,
					color-mix(in srgb, black 80%, transparent) 80%
				);

				color: var(--text-normal);
				font-weight: 700;
				margin-bottom: .5rem;
				position: relative;
				box-sizing: border-box;
				gap: .5rem;
			}

			.head-wrapper {
				display: flex;
				justify-content: space-between;
				align-items: flex-start;
				gap: .3em;
			}

			.collapse-chevron {
				top: 0.9rem;
				inset-inline-end: 0.9rem;
				z-index: 10;
				display: inline-flex;
				align-items: center;
				justify-content: center;
				background: none;
				border: none;
				padding: .2em;
				cursor: pointer;
				color: color-mix(in srgb, var(--text-normal) 55%, transparent);
				transition: color .3s ease;
				grid-column: 2;
				
				&:hover {
					color: var(--text-normal);
				}
	
				& p7t-icon {
					width: 1.4em;
					height: 1.4em;
					transition: transform .3s ease;
				}
	
				&.collapsed p7t-icon {
					transform: rotate(-90deg);
				}
			}

			:host::part(header) {
				display: flex;
				padding-inline: .5em;
				flex-direction: column;
				align-items: flex-start;
				gap: .4em;
				margin-block: .1em .2em;
			}
	
			:host::part(pre-heading) {
				font-size: 1.1em;
				font-weight: 450;
			}

			:host::part(heading) {
				font-size: 1.65em;
				line-height: .9;
				font-weight: 250;
			}

			:host::part(sub-heading) {
				font-size: .8em;
				font-weight: 500;
				margin-top: -.4em;
				display: flex;
				align-items: center;
				gap: .2ch;
				color: color-mix(in srgb, currentColor 85%, transparent);
			}

			:host::part(content) {
				flex: 1;
			}

			:host([collapsed]) {
				&::part(content) {
					display: none;
				}

				&::part(footer) {
					display: none;
				}
			}
		`
	}

	@property() preHeading = ''
	@property() heading = ''
	@property() subHeading = ''

	/** Opt-in: renders a collapse chevron. Off leaves existing cards untouched. */
	@property({ type: Boolean }) collapsible = false
	/**
	 * Whether the body is collapsed. Controlled by the parent — the chevron only announces a `collapsetoggle`;
	 * it never flips this itself — so cards can be linked into a mutually-exclusive pair. Reflected so a parent
	 * stylesheet can size the collapsed card differently from the expanded one.
	 */
	@property({ type: Boolean, reflect: true }) collapsed = false

	protected override get template() {
		return html`
			<div class='head-wrapper'>
				${this.headerTemplate}
				${!this.collapsible ? nothing : this.collapseToggleTemplate}
			</div>
			<div part='content'>
				${this.content}
			</div>
			<div part='footer'>
				${this.footer}
			</div>
		`
	}

	protected get collapseToggleTemplate() {
		return html`
			<button
				class='collapse-chevron ${this.collapsed ? 'collapsed' : ''}'
				part='collapse-toggle'
				aria-label=${this.collapsed ? 'Expand card' : 'Collapse card'}
				aria-expanded=${!this.collapsed}
				@click=${() => this.dispatchEvent(new CustomEvent('collapsetoggle', { bubbles: true, composed: true }))}>
				<p7t-icon icon='lucide:chevron-down'></p7t-icon>
			</button>
		`
	}

	protected get headerTemplate() {
		return html`
			<slot name='header'>
				<div part='header'>
					<slot part='pre-heading' name='pre-heading'>${this.preHeadingTemplate}</slot>
					<slot part='heading' name='heading'>${this.headingTemplate}</slot>
					<slot part='sub-heading' name='sub-heading'>${this.subHeadingTemplate}</slot>
				</div>
			</slot>
		`
	}

	protected get preHeadingTemplate() {
		return html`
			<span>${this.preHeading}</span>
		`
	}

	protected get headingTemplate() {
		return html`
			<span>${this.heading}</span>
		`
	}

	protected get subHeadingTemplate() {
		return html`
			<span>${this.subHeading}</span>
		`
	}

	protected get content() {
		return html`<slot></slot>`
	}

	protected get footer() {
		return html`<slot name='footer'></slot>`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-card': CardComponent
	}
}