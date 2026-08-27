import { Component, css, html, nothing, property } from '@a11d/lit'
import { IconName } from 'components/PleiadesIcon'
import { defaultNullGlyph, nullGlyphStyle, nullGlyphTemplate } from './nullGlyph'
import '../PleiadesIcon'
import './Tooltip'

/**
 * Base for the info chips — the small, unified displays of one bit of an entity (its Celestron, its college, its
 * directive, its schedule, its status). Each chip renders one thing, one way, everywhere it appears, replacing the
 * markup that used to be hand-formatted into each item, banner, and grid row.
 *
 * **Layout.** Most chips are the same shape — a glyph beside a label — so the base draws that for them: a subclass
 * supplies its glyph by overriding {@link bulletIcon} and its label by overriding {@link bulletText}, and the default
 * {@link content} lays the two out (icon then text, or {@link iconTrailing | text then icon}). A chip whose text is
 * its own affair — the manual {@link IconItem} — leaves {@link bulletText} as the default `<slot>` and lets the
 * consumer fill it. {@link textHidden} draws the glyph alone (its label becoming the tooltip's job). A chip with a
 * genuinely different shape (a bar, a badge, a mini-banner) overrides {@link content} wholesale instead.
 *
 * The hooks are named `bulletIcon`/`bulletText` rather than `icon`/`text` so a chip is free to expose its own public
 * `icon`/`text` *props* (the legacy {@link IconItem} does) without colliding with the base's read-only accessors.
 *
 * **Tooltip.** Every chip carries a built-in tooltip register: a subclass overrides {@link tooltip} to hand back the
 * extra a hover should reveal — a descriptor or name, a directive's mini-banner, an allocation's progress summary —
 * and the base wraps the visible content in a {@link Tooltip}. A string becomes plain text; anything else is rendered
 * as rich markup; `nothing` leaves the chip tooltip-less.
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

			/* The shared icon-text layout every simple chip renders through. A chip restyles its own icon size/gap. */
			.info-bullet {
				display: inline-flex;
				align-items: center;
				gap: .4ch;
				min-width: 0;
			}

			.info-icon {
				width: 1.2em;
				height: 1.2em;
				flex: 0 0 auto;
			}

			.info-text {
				min-width: 0;
			}

			${nullGlyphStyle}
		`
	}

	/**
	 * The chip's glyph. A simple chip overrides this to name its icon; `undefined` draws none (text only). The type
	 * is widened past {@link IconName} so a chip can hand in a resolved media source (PEP105), the same string
	 * {@link PleiadesIcon} accepts — the timeframe chip does, for its media companion.
	 */
	protected get bulletIcon(): IconName | (string & {}) | undefined {
		return undefined
	}

	/** The chip's label. Defaults to the slotted content (the manual {@link IconItem}); a chip overrides it. */
	protected get bulletText(): unknown {
		return html`<slot></slot>`
	}

	/** Draws the glyph alone, its label becoming the tooltip's job — the icon-only form some chips offer. */
	protected get textHidden(): boolean {
		return false
	}

	/** Draws the glyph after the text rather than before — a value beside its trailing unit glyph, say. */
	protected get iconTrailing(): boolean {
		return false
	}

	/** The extra information a hover reveals: a string (plain text), a template (rich), or `nothing` for no tooltip. */
	protected get tooltip(): unknown {
		return nothing
	}

	/** The unified null indicator, for a nullable chip to render in place of its content when it has no value. */
	protected get nullGlyphTemplate() {
		return nullGlyphTemplate(this.nullGlyph)
	}

	/**
	 * The chip's glyph markup — the shared `part='icon' class='info-icon'` icon every chip draws, or `nothing` when
	 * there is no glyph. Split out so a chip that renders its own icon (e.g. a media companion) still gets exactly the
	 * base's class and part rather than hand-rolling its own; the glyph defaults to {@link bulletIcon}.
	 */
	protected iconTemplate(icon: IconName | (string & {}) | undefined = this.bulletIcon): unknown {
		return icon
			? html`<p7t-icon part='icon' class='info-icon' .icon=${icon}></p7t-icon>`
			: nothing
	}

	/**
	 * The visible chip content. The default is the shared icon-text layout built from {@link iconTemplate} and
	 * {@link bulletText}; a chip with a non-bullet shape overrides this wholesale (and may still call `super.content`
	 * for its plain rows).
	 */
	protected get content(): unknown {
		const iconTemplate = this.iconTemplate()
		if (this.textHidden) {
			return html`<span class='info-bullet'>${iconTemplate}</span>`
		}

		const textTemplate = html`<span class='info-text'>${this.bulletText}</span>`
		return html`
			<span class='info-bullet'>
				${this.iconTrailing ? html`${textTemplate}${iconTemplate}` : html`${iconTemplate}${textTemplate}`}
			</span>
		`
	}

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
