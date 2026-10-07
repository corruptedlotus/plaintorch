import { component, css, html, HTMLTemplateResult, nothing, repeat } from '@a11d/lit'
import { BriefingCard } from '../briefing/BriefingCard'
import { creationActions, createEntity } from '../grid/entityActions'
import { gridRowKey, type GridEntity } from '../grid/entityTree'
import type { MoonlightNode } from './moonlightTree'

/**
 * One top-level lunar directive on the Moonlight page, as a briefing card whose {@link data} is the directive's
 * {@link MoonlightNode}.
 *
 * Beneath its heading the card is split into sections, one per child directive. Each section lays out what that child
 * holds as a horizontally scrolling grid two tiles tall: a directive nested further is a box spanning both rows, an
 * incentive a node-shaped tile taking one. A thinner section closes the card, holding the incentives the card's own
 * directive owns and the affordances to add a child directive or an incentive to it.
 *
 * The card keeps the briefing card's structure — its watermark crest, header, content and footer parts — but not its
 * throne-room dress: it sits flat in a grid of its peers, sized by what it holds. So the radial wash, the margins, the
 * part gap and the fixed-height scrolling content are put back to a plain grid cell's, and the crest is drawn as a
 * small corner watermark rather than a full-width mask. The node is no tracked entity, so the base's entity watch rests;
 * every heading watches its own directive.
 */
@component('p7t-moonlight-card')
export class MoonlightCard extends BriefingCard<MoonlightNode> {
	protected override readonly icon = 'directive-lunar'

	static override get styles() {
		return css`
			${super.styles}

			/* A flat cell of the page's card grid: no margins between cards, no gap between the parts. */
			:host {
				min-width: 0;
				margin: 0;
				gap: 0;
				padding: .9em .9em .5em;
				background: var(--background-primary-alt);
				overflow: clip;
			}

			/* The crest as a small watermark bleeding off the top corner, rather than the briefing card's full-width mask. */
			.mask {
				inset-block: -2.2em auto;
				inset-inline: auto -2.2em;
				width: 10em;
				height: 10em;
				color: color-mix(in srgb, var(--p7t-flare-accent, var(--interactive-accent)) 9%, transparent);
				overflow: visible;

				&::part(icon-frame) {
					margin-top: 0;
					mask-position: center;
				}
			}

			/*
			 * The content grows with the card rather than scrolling inside a fixed height, so the card is as tall as its
			 * sections, and the closing section can sink to the bottom of a card stretched by its row. Its scrollbar
			 * colours are put back too: they are inherited, and Chromium drops the strips' own slim scrollbars wherever
			 * scrollbar-color is set.
			 */
			:host::part(content) {
				inset-inline: auto;
				width: auto;
				padding-inline: 0;
				overflow: visible;
				flex: 1 1 auto;
				display: flex;
				flex-direction: column;
				scrollbar-width: auto;
				scrollbar-color: auto;
			}

			p7t-moonlight-heading[level='card'] {
				flex: 1;
				min-width: 0;
				padding-block-end: .7em;
			}

			.section {
				position: relative;
				display: flex;
				flex-direction: column;
				gap: .45em;
				padding-block: .6em;
				border-top: 1px solid color-mix(in srgb, var(--text-normal) 10%, transparent);
			}

			/*
			 * The strip: a grid two tiles tall that grows sideways, scrolling once it outgrows the card. Tiles flow down
			 * each column before the next one starts; a box spans a whole column, and dense packing lets incentives pair
			 * up in the columns after it.
			 */
			.strip {
				display: grid;
				grid-template-rows: repeat(2, minmax(3.3em, auto));
				grid-auto-flow: column dense;
				grid-auto-columns: var(--p7t-moonlight-tile-width, 14em);
				gap: .45em;
				overflow-x: auto;
				overflow-y: hidden;
				overscroll-behavior-x: contain;
				scroll-snap-type: x proximity;
				scroll-padding-inline: .1em;
				padding: .1em .1em .45em;
				margin-inline: -.1em;

				& > * {
					scroll-snap-align: start;
				}

				&[data-rows='1'] {
					grid-template-rows: minmax(3.3em, auto);
				}

				/*
				 * A slim, quiet scrollbar: a hairline thumb with no track or arrows, brightening only while the strip is
				 * under the pointer. Drawn through the WebKit pseudo-elements because Chromium ignores them the moment
				 * scrollbar-width or scrollbar-color is set, and those alone cannot drop the arrow buttons.
				 */
				&::-webkit-scrollbar {
					height: 4px;
				}

				&::-webkit-scrollbar-track,
				&::-webkit-scrollbar-corner {
					background: transparent;
				}

				&::-webkit-scrollbar-button {
					display: none;
				}

				&::-webkit-scrollbar-thumb {
					border-radius: 4px;
					background-color: color-mix(in srgb, var(--text-normal) 10%, transparent);
				}

				&:hover::-webkit-scrollbar-thumb {
					background-color: color-mix(in srgb, var(--text-normal) 24%, transparent);
				}

				&::-webkit-scrollbar-thumb:hover {
					background-color: color-mix(in srgb, var(--p7t-flare-accent, var(--interactive-accent)) 55%, transparent);
				}
			}

			p7t-moonlight-directive-box {
				grid-row: span 2;
			}

			.vacant {
				padding: .1em .3em;
				color: var(--text-faint, var(--text-muted));
				font-family: var(--font-interface);
				font-size: .85em;
				font-style: italic;
			}

			/* The closing section: the card directive's own incentives and the way to add beneath it. */
			.section.own {
				margin-block-start: auto;
				gap: .3em;
				padding-block: .45em 0;
				border-top-style: dashed;
			}

			.add {
				display: flex;
				flex-wrap: wrap;
				align-items: center;
				gap: .2em;
				color: var(--text-muted);
				font-size: .9em;
			}

			.add > :not(.label) {
				transition: color .2s ease;
			}

			.add > :not(.label):hover {
				color: var(--text-normal);
			}

			/* The inline heading the add row leads with. */
			.add .label {
				margin-inline: .3em .2em;
				color: var(--text-faint, var(--text-muted));
				font-family: var(--font-interface);
				font-size: .75em;
				font-weight: 500;
				letter-spacing: .08em;
				text-transform: uppercase;
			}
		`
	}

	/** Without a node there is nothing to show, not the briefing card's "No Data" panel. */
	protected override get template() {
		return this.data ? super.template : html``
	}

	/** The directive's own heading — its crest, editable title and state, and its full banner a click away. */
	protected override get headerTemplate() {
		return html`<p7t-moonlight-heading level='card' .directive=${this.data!.directive}></p7t-moonlight-heading>`
	}

	protected override get content() {
		const node = this.data!
		const id = node.directive.id
		return html`
			${repeat(node.subdirectives, child => child.directive.id, child => html`
				<section class='section'>
					<p7t-moonlight-heading level='section' addable .directive=${child.directive}></p7t-moonlight-heading>
					${this.stripTemplate(child.subdirectives, child.incentives, 2)}
				</section>
			`)}
			<section class='section own'>
				${node.incentives.length === 0 ? nothing : this.stripTemplate([], node.incentives, 1)}
				<div class='add'>
					<span class='label'>Create</span>
					<p7t-button ghost icon='directive-lunar' @click=${() => createEntity('lunar-directive', id)}>Sub-directive</p7t-button>
					<p7t-expanding-actions
						icon='objective'
						label='Incentive'
						actionLabel='Add an incentive'
						.actions=${creationActions(id, ['objective', 'fate', 'decree'])}>
					</p7t-expanding-actions>
				</div>
			</section>
		`
	}

	/**
	 * A section's tiles: nested directives as boxes two rows tall, then incentives one row each — or, with nothing to
	 * show, a quiet note in their place so an empty section does not hold open two rows of nothing.
	 */
	private stripTemplate(boxes: readonly MoonlightNode[], incentives: readonly GridEntity[], rows: 1 | 2): HTMLTemplateResult {
		if (boxes.length + incentives.length === 0) {
			return html`<div class='vacant'>Nothing beneath yet</div>`
		}

		return html`
			<div class='strip' data-rows=${rows}>
				${repeat(boxes, box => box.directive.id, box => html`
					<p7t-moonlight-directive-box .node=${box}></p7t-moonlight-directive-box>
				`)}
				${repeat(incentives, gridRowKey, incentive => html`
					<p7t-moonlight-incentive-item interactive .entity=${incentive}></p7t-moonlight-incentive-item>
				`)}
			</div>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-moonlight-card': MoonlightCard
	}
}
