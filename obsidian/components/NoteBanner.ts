import { Component, component, css, html, property, literal as l } from '@a11d/lit'
import { App } from 'obsidian'
import { plaintorchNodeCoreClient, EntityExistence } from '@pleiades/sdk/plaintorch/node'
import { DerivedRef } from 'components/data'

@component('p7t-note-banner')
export class NoteBanner extends Component {
	@property({ reflect: true, type: Boolean }) invalid = true

	app?: App
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
				margin-block: -2em 1em !important;
			}

			:host([invalid]) {
				display: none !important;
			}
		`
	}

	protected override get template() {
		const note = this.note
		return !note ? html`
			Loading...
		` : this.renderBannerElement(note)
	}

	protected renderBannerElement(note: EntityExistence) {
		const tag = this.getTagForKind(note.entityKind)
		if (tag) {
			return html`<${tag} .puck=${note.puck} .app=${this.app}></${tag}>`
		} else {
			return html`<p7t-entity-banner .puck=${note.puck ?? ''} .xtype=${note.entityKind} .entity=${{ id: note.puck ?? '', title: this.entityTitle }} .app=${this.app}></p7t-entity-banner>`
		}
	}

	/** A kind with no dedicated banner still renders a title, which only the resolved entity carries. */
	private get entityTitle(): string {
		return (this.note?.entity as { title?: string } | undefined)?.title ?? ''
	}

	protected getTagForKind(kind?: string) {
		switch (kind) {
			case 'stellar-directive': return l`p7t-sdirective-banner`
			case 'lunar-directive': return l`p7t-ldirective-banner`

			case 'objective': return l`p7t-objective-banner`
			case 'fate': return l`p7t-fate-banner`
			case 'decree': return l`p7t-decree-banner`

			case 'onrush-sprint': return l`p7t-onrush-banner`
			case 'polaris-cycle': return l`p7t-polaris-banner`
			case 'lore-page': return l`p7t-lore-banner`

			default: return undefined
		}
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