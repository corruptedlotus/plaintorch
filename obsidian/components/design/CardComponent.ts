import { Component, component, css, html, property } from "@a11d/lit";

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
				/*background: linear-gradient(
					40deg,
					color-mix(in srgb, var(--p7t-flare-accent, var(--interactive-accent)) 50%, transparent) -35%,
					color-mix(in srgb, black 80%, transparent) 100%
				);*/
				background:
					/*url(''),*/
					radial-gradient(
						color-mix(in srgb, black 90%, transparent) 38%,
						color-mix(in srgb, var(--p7t-flare-accent, var(--interactive-accent)) 40%, black) 70%,
						transparent 94%);
				background-size: /*contain,*/ 95vw 95vw;
				background-blend-mode: soft-light;
				background-position: /*bottom,*/ 40% 84%;
				
				color: var(--text-normal);
				font-weight: 700;
				margin-bottom: .5rem;
				position: relative;
				box-sizing: border-box;
				gap: .5rem;
			}

			:host::part(header) {
				display: flex;
				padding-inline: .5em;
				flex-direction: column;
			}
	
			:host::part(pre-heading) {
				font-size: 1.1em;
				font-weight: 450;
			}

			:host::part(heading) {
				font-size: 1.65em;
				font-weight: 250;
			}

			:host::part(sub-heading) {
				font-size: 1em;
				font-weight: 500;
				opacity: .6;
			}

			:host::part(content) {
				flex: 1;
			}
		`
	}

	@property() preHeading = ''
	@property() heading = ''
	@property() subHeading = ''

	protected override get template() {
		return html`
			${this.headerTemplate}
			<div part='content'>
				<slot>${this.content}</slot>
			</div>
			<div part='footer'>
				<slot name='footer'>${this.footer}</slot>
			</div>
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