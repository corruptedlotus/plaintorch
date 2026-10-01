import { Component, component, css, html, nothing, property } from '@a11d/lit'
import type { IconName } from '../PleiadesIcon'

/**
 * The hero a briefing page opens on: its title, big and centred, with the page's glyph beside it — over a banner that
 * fades out downward, where the page has one.
 *
 * The banner bleeds out to the briefing's page frame (past the page's own padding and into its rounded top corners) and
 * lightens what lies beneath it, so its dark sky gives way to the frame's own surface and only its light shows. That
 * blend reaches the frame only while nothing between the two forms a group of its own, which is why the hero is not
 * isolated. A page without a banner takes the {@link compact} hero: the same title, in a shorter space.
 *
 * The title scales with the hero's width, so a narrow pane shrinks it rather than letting it overflow.
 */
@component('p7t-page-hero')
export class PageHero extends Component {
	/** The page's title. */
	@property() heading = ''
	/** The page's glyph, drawn before the title. */
	@property() icon?: IconName
	/** The banner image's URL. Without one the title stands on the page's own surface. */
	@property() banner?: string
	/** A shorter hero, for a page whose title stands without a banner. */
	@property({ type: Boolean, reflect: true }) compact = false

	static override get styles() {
		return css`
			/* Tall enough that a banner's centrepiece sits clear above the title rather than behind it. */
			:host {
				position: relative;
				display: flex;
				flex-direction: column;
				justify-content: flex-end;
				align-items: center;
				min-height: 18em;
				padding-block-end: .6em;
				container-type: inline-size;
			}

			:host([compact]) {
				min-height: 6em;
			}

			.banner {
				position: absolute;
				inset: -.4em -.5em 0;
				border-radius: 16px 16px 0 0;
				background: var(--p7t-page-hero-banner) var(--p7t-page-hero-banner-position, center 55%) / cover no-repeat;
				mask-image: linear-gradient(to bottom, black 30%, transparent);
				mix-blend-mode: lighten;
				pointer-events: none;
			}

			h1 {
				position: relative;
				display: flex;
				align-items: center;
				justify-content: center;
				flex-wrap: wrap;
				gap: .35em;
				margin: 0;
				font-size: clamp(1.7em, 7cqi, 2.9em);
				font-weight: 200;
				letter-spacing: .05em;
				line-height: 1.1;
				text-align: center;
				text-shadow: 0 2px 24px rgb(0 0 0 / .7);

				& p7t-icon {
					flex: none;
					width: 1em;
					height: 1em;
					color: color-mix(in srgb, var(--p7t-flare-accent, var(--interactive-accent)) 75%, var(--text-normal));
				}
			}
		`
	}

	protected override get template() {
		return html`
			${!this.banner ? nothing : html`<div class='banner' style=${`--p7t-page-hero-banner: url("${this.banner}")`}></div>`}
			<h1>
				${!this.icon ? nothing : html`<p7t-icon icon=${this.icon}></p7t-icon>`}
				${this.heading}
			</h1>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-page-hero': PageHero
	}
}
