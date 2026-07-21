import { Component, component, css, html, property, state } from '@a11d/lit'
import { App } from 'obsidian'
import { plaintorchNodeCoreClient, EntityExistence } from '@pleiades/sdk/plaintorch/node'

@component('p7t-note-banner')
export class NoteBanner extends Component {
	@state() note?: EntityExistence
	@property({ reflect: true, type: Boolean }) invalid = true
	
	app?: App
	file: string = ''

	static override get styles() {
		return css`
			:host {
				display: grid;
				overflow-anchor: auto;
				white-space: initial;
				margin-block: -2em 1em !important;
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
			case 'directive':
				return html`<p7t-directive-banner .puck=${this.note.puck} .app=${this.app}></p7t-directive-banner>`
			case 'objective':
				return html`<p7t-objective-banner .puck=${this.note.puck} .app=${this.app}></p7t-objective-banner>`
			case 'onrush-sprint':
				return html`<p7t-onrush-banner .puck=${this.note.puck} .app=${this.app}></p7t-onrush-banner>`
			case 'polaris-cycle':
				return html`<p7t-polaris-banner .puck=${this.note.puck} .app=${this.app}></p7t-polaris-banner>`
			case 'lore-page':
				return html`<p7t-lore-banner .puck=${this.note.puck} .app=${this.app}></p7t-lore-banner>`
			default:
				return html`<p7t-entity-banner .puck=${this.note?.puck ?? ''} .xtype=${this.note?.entityKind} .entity=${{ id: this.note?.puck ?? '', title: this.entityTitle }} .app=${this.app}></p7t-entity-banner>`
		}
	}

	private get entityTitle(): string {
		return (this.note?.entity as { title?: string } | undefined)?.title ?? ''
	}

	protected override async initialized() {
		const note = await plaintorchNodeCoreClient.system.resolveNote(this.file)
		this.note = note
		this.invalid = !(note?.exists)
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-note-banner': NoteBanner
	}
}