import { component, css, html, nothing, property, TemplateResult } from '@a11d/lit'
import type { MediaReference } from '@pleiades/sdk'
import { EditablePart } from '../editing/EditableDataLink'
import { SelectMediaModal } from './SelectMediaModal'
import type { MediaEntityRef } from './SelectMediaModal'
import './MediaView'

/**
 * A display-and-edit field for a media key (PEP105), the media counterpart to the other editables. It shows the
 * field through {@link MediaView} — a custom image, a glyph, or the caller's default when empty — and, on click,
 * opens {@link SelectMediaModal} to pick a new value: an entity or vault asset, an icon, or removal.
 *
 * Its value is the media key the field stores (a glyph/lucide name, a `media:`/`vault:` file, or the empty string
 * for "cleared"); removal resolves to the empty string so a consumer clears the field the same way it sets it. The
 * committed value is emitted through the base's `change` event; the owner persists it.
 */
@component('p7t-editable-media')
export class EditableMedia extends EditablePart<string> {
	/** The resolved media companion from the core, used to render the current custom image. */
	@property({ type: Object }) media?: MediaReference

	/** The glyph shown when the field is empty (its designated default). */
	@property() default = 'lucide:image'

	/** Constrains the preview to a contained square, for icon (as opposed to banner) use. */
	@property({ type: Boolean, reflect: true }) icon = false

	/** The entity this field belongs to, enabling entity-level (`media:`) media; omit for a field with no self folder. */
	@property({ type: Object }) entity?: MediaEntityRef

	@property({ type: Object }) promptTemplate?: TemplateResult

	static override get styles() {
		return css`
			${super.styles}

			:host {
				cursor: pointer;
				display: grid;
				grid-template-rows: 1fr;
				grid-template-columns: 1fr;
			}

			/* Fill the frame the host is sized to, so one control serves both a small icon and a full-width banner. */
			p7t-media {
				width: 100%;
				height: 100%;
				grid-area: 1 / 1;
			}

			.prompt {
				grid-area: 1 / 1;
				opacity: .3;
				transition: opacity .4s;
				font-size: .75em;

				&:hover {
					opacity: .6;
				}
			}
		`
	}

	override doEdit = async (_current: string | undefined): Promise<string | undefined> => {
		const chosen = await SelectMediaModal.prompt(this.entity)
		// The modal resolves to a key, or to null for an explicit removal; the empty string is how this field
		// carries "cleared". A dismissal rejects instead, which the base treats as no change.
		return chosen === null ? '' : chosen
	}

	protected override get template() {
		return html`
			<p7t-media
				part='media'
				?icon=${this.icon}
				.media=${this.media}
				.mediaKey=${this.value}
				.default=${this.default}>
			</p7t-media>
			${!this.promptTemplate ? nothing : html`<div part='prompt' class='prompt'>${this.promptTemplate}</div>`}
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-editable-media': EditableMedia
	}
}
