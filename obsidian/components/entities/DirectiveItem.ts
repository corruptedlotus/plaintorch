import { component, css, html, nothing, property } from '@a11d/lit'
import { Directive } from '@pleiades/sdk'
import { IconName } from 'components/PleiadesIcon'
import { InfoItem } from '../design/InfoItem'
import './DirectiveCard'

/**
 * The directive an entity belongs to (PEP100) — its kind glyph beside its title — drawn one unified way in the
 * items, banners, and occurrence rows that used to hand-format it. A lunar and a stellar directive read apart by
 * their glyph. With no directive it shows a placeholder (an objective with none is a "World Quest").
 *
 * The built-in tooltip is a mini-banner (see {@link DirectiveCard}): the crest, the title, and the workflow state —
 * the gist of the directive's own banner, without leaving the surface the chip sits on.
 */
@component('p7t-directive-item')
export class DirectiveItem extends InfoItem {
	@property({ type: Object }) directive?: Directive
	/** Shown when there is no directive — an objective with none is a world quest. */
	@property() placeholder = 'World Quest'

	static override get styles() {
		return css`
			${super.styles}

			.info-bullet {
				font-weight: 400;
				/*font-size: .9em;
				line-height: .9;*/
			}

			.placeholder {
				opacity: .7;
				font-weight: 500;
				/*font-size: .9em;
				line-height: .9;*/
			}
		`
	}

	private get kindIcon(): IconName {
		return this.directive?.isLunar ? 'directive-lunar' : 'directive'
	}

	protected override get bulletIcon(): IconName | undefined {
		return this.directive ? this.kindIcon : undefined
	}

	protected override get bulletText() {
		return this.directive?.title ?? this.placeholder
	}

	/** With no directive the chip is just a dimmed placeholder (a world quest), outside the icon-text layout. */
	protected override get content() {
		if (!this.directive) {
			return html`<span class='placeholder'>${this.placeholder}</span>`
		}

		return super.content
	}

	protected override get tooltip() {
		return this.directive
			? html`<p7t-directive-card .directive=${this.directive}></p7t-directive-card>`
			: nothing
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-directive-item': DirectiveItem
	}
}
