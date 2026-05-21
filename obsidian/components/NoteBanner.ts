import { Component, component, css, html, state } from '@a11d/lit'
import { App } from 'obsidian'
import { plaintorchNodeCoreClient, VaultNoteAuthorityResolution } from '@pleiades/sdk/plaintorch/node'

@component('p7t-note-banner')
export class NoteBanner extends Component {
	@state() note?: VaultNoteAuthorityResolution
	
	app?: App
	file: string = ''

	static override get styles() {
		return css`
			:host {
				display: flex;
				flex-direction: column;
				align-items: stretch;
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
				return html`<p7t-entity-banner .xtype=${this.note?.entityKind} .puck=${this.note?.puck ?? 'NULL'}></p7t-entity-banner>`
		}
	}

	protected override async initialized() {
		const note = await plaintorchNodeCoreClient.system.resolveNote(this.file)
		this.note = note
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-note-banner': NoteBanner
	}
}