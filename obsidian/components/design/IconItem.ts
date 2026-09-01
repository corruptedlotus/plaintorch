import { component, css, html, property } from '@a11d/lit'
import { IconName } from 'components/PleiadesIcon'
import { InfoItem } from './InfoItem'

/**
 * A manual info chip: pick a glyph and give it a label. `<p7t-icon-item icon='lucide:sparkles' text='Featured'>` — or
 * slot the label as markup, `<p7t-icon-item icon='lucide:sparkles'>Featured</p7t-icon-item>`. It is the base
 * {@link InfoItem}'s icon-text layout with the glyph and label handed in as props, for the one-off bullets a
 * purpose-built chip would be overkill for.
 *
 * It carries the legacy chip's whole surface unchanged — `icon`, `text`, `data`, `small` — so it drops in wherever
 * that element was used; the only addition is that `text` is now the *fallback* for a slotted label rather than the
 * sole source, so both `text='X'` and a slotted `X` render. `data` carries an arbitrary payload a consumer hangs on
 * the chip (a select list keys its rows off it); `small` shrinks the glyph for a dense row.
 */
@component('p7t-icon-item')
export class IconItem<T = unknown> extends InfoItem {
	/** The glyph to draw. */
	@property() icon?: IconName

	/** The label, and the fallback when nothing is slotted — so `text='X'` and a slotted `X` both render. */
	@property() text = ''

	/** An arbitrary payload a consumer hangs on the chip (e.g. the value a select row stands for). */
	@property() data!: T

	static override get styles() {
		return css`
			${super.styles}
		`
	}

	protected override get bulletIcon(): IconName | undefined {
		return this.icon
	}

	protected override get bulletText() {
		return html`<slot>${this.text}</slot>`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-icon-item': IconItem<unknown>
	}
}
