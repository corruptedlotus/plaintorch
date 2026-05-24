import { Component, component, css, html, property, state } from '@a11d/lit'
import { App } from 'obsidian'
import { plaintorchNodeCoreClient, VaultNoteAuthorityResolution } from '@pleiades/sdk/plaintorch/node'

@component('p7t-note-banner')
export class NoteBanner extends Component {
	@state() note?: VaultNoteAuthorityResolution
	@property({ reflect: true, type: Boolean }) invalid = true
	
	app?: App
	file: string = ''

	static override get styles() {
		return css`
			:host {
				display: flex;
				flex-direction: column;
				align-items: stretch;
				margin: 0 0 1rem;
				padding: 0.75rem 0.9rem;
				border: 1px solid var(--background-modifier-border);
				border-radius: 12px;
				background: color-mix(in srgb, var(--interactive-accent, #7c6cff) 12%, var(--background-secondary));
				color: var(--text-normal);
				font-weight: 700;
				margin-bottom: .5rem;
			}

			:host([invalid]) {
				display: none !important;
			}
		`
	}

	protected override render() {
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
			default:
				return html`<p7t-entity-banner .puck=${this.note?.puck ?? ''} .xtype=${this.note?.entityKind} .entity=${{ id: this.note?.puck ?? '', title: this.note?.title ?? '' }} .app=${this.app}></p7t-entity-banner>`
		}
	}

	protected override async initialized() {
		const note = await plaintorchNodeCoreClient.system.resolveNote(this.file)
		this.note = note
		this.invalid = !(note?.isPlaintorchEntity)
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-note-banner': NoteBanner
	}
}