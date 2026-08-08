import { Component, component, css, html, nothing, property, state } from "@a11d/lit";

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

			.collapse-switch {
				position: absolute;
				top: 0.9rem;
				inset-inline-end: 0.9rem;
				z-index: 10;
				display: inline-flex;
				align-items: center;
				background: none;
				border: none;
				padding: 0;
				cursor: pointer;
			}

			.collapse-switch .track {
				box-sizing: border-box;
				display: inline-flex;
				align-items: center;
				width: 2.4em;
				height: 1.3em;
				padding: .15em;
				border-radius: 1em;
				background-color: color-mix(in srgb, var(--text-normal) 20%, transparent);
				transition: background-color .3s ease;
			}

			.collapse-switch:not(.collapsed) .track {
				background-color: color-mix(in srgb, var(--p7t-flare-accent, var(--interactive-accent)) 70%, transparent);
			}

			.collapse-switch .thumb {
				width: 1em;
				height: 1em;
				border-radius: 50%;
				background-color: var(--text-normal);
				transition: transform .3s ease;
				transform: translateX(1.1em);
			}

			.collapse-switch.collapsed .thumb {
				transform: translateX(0);
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
		`
	}

	@property() preHeading = ''
	@property() heading = ''
	@property() subHeading = ''

	/** Opt-in: renders a switch in the corner that collapses the card body. Off leaves existing cards untouched. */
	@property({ type: Boolean }) collapsible = false
	@state() collapsed = false

	protected override get template() {
		return html`
			${!this.collapsible ? nothing : this.collapseSwitchTemplate}
			${this.headerTemplate}
			${this.collapsed ? nothing : html`
				<div part='content'>
					<slot>${this.content}</slot>
				</div>
				<div part='footer'>
					<slot name='footer'>${this.footer}</slot>
				</div>
			`}
		`
	}

	protected get collapseSwitchTemplate() {
		return html`
			<button
				class='collapse-switch ${this.collapsed ? 'collapsed' : ''}'
				part='collapse-switch'
				role='switch'
				aria-label='Collapse card'
				aria-checked=${!this.collapsed}
				@click=${() => { this.collapsed = !this.collapsed }}>
				<span class='track'><span class='thumb'></span></span>
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

	protected get content() { return html`` }
	protected get footer() { return html`` }
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-card': CardComponent
	}
}