import { Component, component, css, html, nothing, property } from '@a11d/lit'
import type { LorePage } from '@pleiades/sdk'
import { toRomanNumeral } from '@pleiades/sdk/helpers'

/**
 * Where the lore stands today: the chapter, act and phase on the lore's active spine — the chapter faint above, the act
 * large, the phase under it in italics behind its number — optionally beside the lore glyph.
 *
 * Handed the active spine's pages (the core's one active-lore definition, as the briefing serves it), it picks each
 * level from them and leaves out a level the spine lacks. The briefing's hero shows it beside its {@link glyph}; the Lore
 * page shows it {@link centred} under its title.
 */
@component('p7t-active-lore')
export class ActiveLore extends Component {
	/** The pages on the active spine, in any order; their levels decide which line each fills. */
	@property({ attribute: false }) pages?: readonly LorePage[]
	/** Draws the lore glyph before the lines. */
	@property({ type: Boolean, reflect: true }) glyph = false
	/** Centres the lines on each other and in the element, for a stand-alone heading. */
	@property({ type: Boolean, reflect: true }) centred = false
	/** Said faintly in place of the lines while the spine has no chapter, act or phase. Unset, nothing is. */
	@property() emptyLabel?: string

	static override get styles() {
		return css`
			:host {
				display: flex;
				align-items: center;
				gap: 10px;
			}

			:host([centred]) {
				justify-content: center;
				text-align: center;
			}

			p7t-icon {
				width: 64px;
				height: 64px;
			}

			.info {
				display: flex;
				flex-direction: column;
			}

			:host([centred]) .info {
				align-items: center;
			}

			.chapter {
				font-size: 1em;
				opacity: .6;
				font-weight: 500;
				margin-bottom: -.2em;
			}

			.act {
				font-size: 1.4em;
				font-weight: 300;

				& :first-child {
					opacity: .6;
					margin-inline-end: .1ch;
				}
			}

			.phase {
				margin-top: .05em;
				font-family: 'Georgia', serif;
				font-size: .9em;
				color: color-mix(in srgb, var(--text-normal) 80%, transparent);
				font-style: italic;
				font-weight: 300;

				& :first-child {
					font-family: var(--font-text);
					background-color: color-mix(in srgb, var(--text-normal) 15%, transparent);
					color: var(--text-normal);
					padding: 0 .8ch;
					font-style: normal;
					font-weight: 800;
					margin-inline-end: .8ch;
					border-radius: .6em;
				}
			}

			.empty {
				color: var(--text-muted);
				font-weight: 300;
			}
		`
	}

	protected override get template() {
		const chapter = this.pages?.find(page => page.level === 'Cha')
		const act = this.pages?.find(page => page.level === 'Act')
		const phase = this.pages?.find(page => page.level === 'p')
		const empty = !chapter && !act && !phase
		return html`
			${!this.glyph ? nothing : html`<p7t-icon icon='lorepage'></p7t-icon>`}
			<div class='info'>
				${!chapter ? nothing : html`
					<span class='chapter'>
						${chapter.title}
					</span>
				`}
				${!act ? nothing : html`
					<span class='act'>
						<span>${act.overrideIdentifier ?? `Act ${toRomanNumeral(act.act ?? 0)}`}:</span>
						${act.title}
					</span>
				`}
				${!phase ? nothing : html`
					<span class='phase'>
						<span>${phase.overrideIdentifier ?? phase.phase}</span>
						"${phase.title}"
					</span>
				`}
				${!empty || !this.emptyLabel ? nothing : html`<span class='empty'>${this.emptyLabel}</span>`}
			</div>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-active-lore': ActiveLore
	}
}
