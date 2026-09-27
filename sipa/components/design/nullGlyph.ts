import { css, html } from '@a11d/lit'
import { IconName } from '../PleiadesIcon'

/**
 * The single glyph that stands for an absent (null/undefined) value, shared by the editable fields and the
 * read-only chips so a "no value" reads the same everywhere. One source of truth, overridable per use.
 */
export const defaultNullGlyph: IconName = 'lucide:minus'

/**
 * The null indicator: the glyph, or whatever a consumer slots into `null` as a placeholder or fallback. Used by
 * both {@link EditablePart} and {@link InfoItem} through their `nullGlyphTemplate`, so the two never diverge.
 */
export function nullGlyphTemplate(glyph: IconName = defaultNullGlyph) {
	return html`<slot name='null'><p7t-icon class='null-glyph' icon=${glyph}></p7t-icon></slot>`
}

/** Styling for the glyph {@link nullGlyphTemplate} renders; spread into a component's own styles. */
export const nullGlyphStyle = css`
	.null-glyph {
		width: 1em;
		height: 1em;
		opacity: .4;
	}
`
