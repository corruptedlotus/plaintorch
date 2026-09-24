import { Component, component, css, html, property, literal as l } from '@a11d/lit'
import { plaintorchNodeCoreClient, EntityExistence } from '@pleiades/sdk/plaintorch/node'
import { DerivedRef } from './data'

@component('p7t-note-banner')
export class NoteBanner extends Component {
	@property({ reflect: true, type: Boolean }) invalid = true

	file: string = ''

	private readonly noteRef = new DerivedRef(
		this,
		plaintorchNodeCoreClient.repos.noteResolution,
		() => this.file
	)

	protected get note() {
		return this.noteRef.value
	}

	static override get styles() {
		return css`
			:host {
				display: grid;
				overflow-anchor: auto;
				white-space: initial;
				margin-block: -1em 1em !important;
				contain: none !important;
				padding-bottom: .5em;
			}

			:host([invalid]) {
				display: none !important;
			}
		`
	}

	protected override get template() {
		return !this.note ? html`
			Loading...
		` : this.renderBannerElement()
	}

	protected renderBannerElement() {
		switch (this.note?.entityKind) {
			case 'stellar-directive': return html`<p7t-sdirective-banner puck=${this.note?.puck}></p7t-sdirective-banner>`
			case 'lunar-directive': return html`<p7t-ldirective-banner puck=${this.note?.puck}></p7t-ldirective-banner>`

			case 'objective': return html`<p7t-objective-banner puck=${this.note?.puck}></p7t-objective-banner>`
			case 'fate': return html`<p7t-fate-banner puck=${this.note?.puck}></p7t-fate-banner>`
			case 'decree': return html`<p7t-decree-banner puck=${this.note?.puck}></p7t-decree-banner>`

			case 'onrush-sprint': return html`<p7t-onrush-banner puck=${this.note?.puck}></p7t-onrush-banner>`
			case 'executive-order': return html`<p7t-executive-order-banner puck=${this.note?.puck}></p7t-executive-order-banner>`
			case 'polaris-cycle': return html`<p7t-polaris-banner puck=${this.note?.puck}></p7t-polaris-banner>`
			case 'lore-page': return html`<p7t-lore-banner puck=${this.note?.puck}></p7t-lore-banner>`

			default: return html`<p7t-entity-banner .puck=${this.note?.puck ?? ''} .xtype=${this.note?.entityKind} .entity=${{ id: this.note?.puck ?? '', title: this.entityTitle }}></p7t-entity-banner>`
		}
	}

	private get entityTitle(): string {
		return (this.note?.entity as { title?: string } | undefined)?.title ?? ''
	}

	protected override updated() {
		this.invalid = !this.note?.exists
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-note-banner': NoteBanner
	}
}