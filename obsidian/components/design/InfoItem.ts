import { Component, css, html, HTMLTemplateResult, nothing, property } from '@a11d/lit'
import { IconName } from 'components/PleiadesIcon'
import { defaultNullGlyph, nullGlyphStyle, nullGlyphTemplate } from './nullGlyph'
import '../PleiadesIcon'
import { tooltip } from './Tooltip'

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
 * and the base tethers a {@link tooltip} to the visible content. A string becomes plain text; anything else is
 * rendered as rich markup; `nothing` leaves the chip tooltip-less.
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

	/** Draws the glyph at the compact size — the dense form a tight row or inline mention wants. */
	@property({ type: Boolean, reflect: true }) small = false

	@property({ type: Boolean, reflect: true }) thumbnail = false

	static override get styles() {
		return css`
			/*
			 * The whole icon-text layout lives here so a chip never re-declares it: the flex structure, the one shared
			 * gap, the one glyph size, and the compact small form are all fixed base rules — every chip reads the same.
			 * A chip is left to add only its own niche bits (a badge, a placeholder, a rich tooltip) and to supply its
			 * glyph and label through the bulletIcon/bulletText getters. An enclosing font-size (e.g. a headline
			 * variant) scales the whole bullet, glyph included, since the sizes are in em.
			 */
			:host {
				display: inline-flex;
				align-items: center;
				min-width: 0;
				max-width: 100%;
			}

			.info-bullet {
				display: inline-flex;
				align-items: center;
				gap: .4ch;
				min-width: 0;
				line-height: .9;
			}

			.info-icon {
				width: 1.8em;
				height: 1.8em;
				flex: 0 0 auto;
			}

			/* The compact form: one step down for a tight row or an inline mention. */
			:host([small]) .info-icon {
				width: 1.2em;
				height: 1.2em;
			}

			.info-text {
				min-width: 0;
				vertical-align: middle;
			}

			:host([thumbnail]) .info-bullet {
				flex-direction: column;
				gap: .5em;

				& .info-icon {
					scale: 1.2;
				}

				& .info-text {
					text-align: center;
					overflow: hidden;
					text-overflow: ellipsis;
					font-size: .85em;
					line-height: .85;
					max-width: 100%;
					padding-block: .15em .05em;
				}
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
	protected get bulletText(): string | HTMLTemplateResult {
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
	protected get content() {
		const tip = this.tooltip
		const hasTooltip = typeof tip === 'string' ? tip.length > 0 : (tip !== nothing && tip !== undefined && tip !== null)

		const iconTemplate = this.iconTemplate()
		if (this.textHidden) {
			return html`<span ${hasTooltip ? tooltip(() => tip as string | HTMLTemplateResult) : nothing} class='info-bullet'>${iconTemplate}</span>`
		}

		const textTemplate = html`<span class='info-text'>${this.bulletText}</span>`

		return html`
			<span class='info-bullet' ${hasTooltip ? tooltip(() => tip as string | HTMLTemplateResult) : nothing}>
				${this.iconTrailing ? html`${textTemplate}${iconTemplate}` : html`${iconTemplate}${textTemplate}`}
			</span>
		`
	}

	protected override get template() {
		return this.content
	}
}
