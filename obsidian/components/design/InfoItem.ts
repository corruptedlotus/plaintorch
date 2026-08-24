import { Component, css, html, nothing, property } from '@a11d/lit'
import { IconName } from 'components/PleiadesIcon'
import { defaultNullGlyph, nullGlyphStyle, nullGlyphTemplate } from './nullGlyph'
import './Tooltip'

/**
 * Base for the info chips — the small, unified displays of one bit of an entity (its Celestron, its college, its
 * directive, its schedule, its status). Each chip renders one thing, one way, everywhere it appears, replacing the
 * markup that used to be hand-formatted into each item, banner, and grid row.
 *
 * Every chip carries a **built-in tooltip register**: a subclass overrides {@link tooltip} to hand back the extra
 * information a hover should reveal — a descriptor or name, a directive's mini-banner, an allocation's progress
 * summary — and the base wraps the visible content in a {@link Tooltip} that shows it. A string becomes plain text;
 * anything else is rendered as rich markup; `nothing` leaves the chip tooltip-less. The visible content itself comes
 * from {@link content}, and modes (compact/large/icon-only/…) are the subclass's own reflected properties.
 */
export abstract class InfoItem extends Component {
	/**
	 * Whether this chip stands for a nullable value. A nullable chip draws the {@link nullGlyph} (through
	 * {@link nullGlyphTemplate}) for its empty state, the same glyph the editable fields use — so "no value" reads
	 * one way everywhere. Off by default; a chip opts in and renders {@link nullGlyphTemplate} where it is empty.
	 */
	@property({ type: Boolean }) nullable = false

	/** The glyph drawn for an absent value; overridable, or replaced wholesale via the `null` slot. */
	@property() nullGlyph: IconName = defaultNullGlyph

	static override get styles() {
		return css`
			:host {
				display: inline-flex;
				align-items: center;
			}

			p7t-tooltip {
				display: inline-flex;
				align-items: center;
			}

			${nullGlyphStyle}
		`
	}

	/** The extra information a hover reveals: a string (plain text), a template (rich), or `nothing` for no tooltip. */
	protected get tooltip(): unknown {
		return nothing
	}

	/** The unified null indicator, for a nullable chip to render in place of its content when it has no value. */
	protected get nullGlyphTemplate() {
		return nullGlyphTemplate(this.nullGlyph)
	}

	/** The visible chip content. */
	protected abstract get content(): unknown

	protected override get template() {
		const tip = this.tooltip
		const text = typeof tip === 'string' ? tip : ''
		const rich = tip !== nothing && tip !== undefined && tip !== null && typeof tip !== 'string'
		const hasTooltip = text.length > 0 || rich
		return html`
			<p7t-tooltip ?disabled=${!hasTooltip} .text=${text}>
				${this.content}
				${rich ? html`<div slot='tooltip'>${tip}</div>` : nothing}
			</p7t-tooltip>
		`
	}
}
