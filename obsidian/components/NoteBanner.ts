import { Component, component, css, html, property, state, literal as l } from '@a11d/lit'
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
		const tag = this.getTagForKind(this.note?.entityKind)
		if (tag) {
			return html`<${tag} .puck=${this.note.puck} .app=${this.app}></${tag}>`
		} else {
			return html`<p7t-entity-banner .puck=${this.note?.puck ?? ''} .xtype=${this.note?.entityKind} .entity=${{ id: this.note?.puck ?? '', title: this.note?.title ?? '' }} .app=${this.app}></p7t-entity-banner>`
		}
	}

	protected getTagForKind(kind?: string) {
		switch (kind) {
			case 'directive': return l`p7t-directive-banner`
			case 'lunar-directive': return l`p7t-lunar-directive-banner`

			case 'objective': return l`p7t-objective-banner`
			case 'fate': return l`p7t-fate-banner`
			case 'decree': return l`p7t-decree-banner`

			case 'onrush-sprint': return l`p7t-onrush-banner`
			case 'polaris-cycle': return l`p7t-polaris-banner`
			case 'lore-page': return l`p7t-lore-banner`

			default: return undefined
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