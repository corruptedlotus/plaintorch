import { Component, component, css, html, repeat, unsafeCSS } from '@a11d/lit'
import type { EntitySubscription } from '@pleiades/sdk'
import { core, DerivedRef } from '..'
import { 'project-everglow-banner-webp' as EverglowBanner } from '../../assets/design'
import { createEntity } from '../grid/entityActions'
import { buildLunarReflection, buildMoonlightTree } from './moonlightTree'

/**
 * The briefing's Moonlight page: Project Moonlight's lunar directives, one card per top-level directive, and the
 * decrees taking part in Lunar Reflection.
 *
 * The page opens on its title, big and centred over the Project Everglow banner, which fades out downward into the
 * page. The cards follow in a grid that fits as many to a row as the width allows — at least one, with no upper bound —
 * with a last tile after them that adds a new top-level directive; the Lunar Reflection list closes the page. It draws
 * from the same directive and incentive listings as the backlog grid, which leaves the lunar hierarchy to this page.
 */
@component('p7t-moonlight-view')
export class MoonlightView extends Component {
	private readonly directives = new DerivedRef(this, core.repos.directiveList)
	private readonly objectives = new DerivedRef(this, core.repos.objectiveList)
	private readonly fates = new DerivedRef(this, core.repos.fateList)
	private readonly decrees = new DerivedRef(this, core.repos.decreeList)

	private storeSubscription?: EntitySubscription

	static override get styles() {
		return css`
			:host {
				display: flex;
				flex-direction: column;
				gap: .9em;
				container-type: inline-size;
				font-family: var(--font-interface);
			}

			/* Tall enough that the banner's centrepiece sits clear above the title rather than behind it. */
			.masthead {
				position: relative;
				isolation: isolate;
				display: flex;
				flex-direction: column;
				justify-content: flex-end;
				align-items: center;
				min-height: 18em;
				padding-block-end: .6em;
			}

			/*
			 * The banner bleeds out to the briefing's page frame — past the page's own padding, and into its rounded top
			 * corners — and fades out downward, so the page reads as rising out of it.
			 */
			.banner {
				position: absolute;
				z-index: -1;
				inset: -.4em -.5em 0;
				border-radius: 16px 16px 0 0;
				background: url('${unsafeCSS(EverglowBanner)}') center 55% / cover no-repeat;
				mask-image: linear-gradient(to bottom, black 30%, transparent);
				pointer-events: none;
			}

			h1 {
				display: flex;
				align-items: center;
				gap: .35em;
				margin: 0;
				/* Big on a wide page, shrinking with a narrow pane rather than overflowing it. */
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

			/*
			 * As many cards to a row as fit at their preferred width — about three on a full-screen window — and never
			 * fewer than one: on a narrow pane a card takes the whole row rather than overflowing it.
			 */
			.cards {
				display: grid;
				grid-template-columns: repeat(auto-fill, minmax(min(100%, var(--p7t-moonlight-card-width, 34em)), 1fr));
				gap: .8em;
				align-items: stretch;
			}

			.notice {
				padding: 1em;
				opacity: .5;
				font-weight: 300;
			}

			.new-card {
				display: flex;
				flex-direction: column;
				align-items: center;
				justify-content: center;
				gap: .5em;
				min-height: 9em;
				box-sizing: border-box;
				border-radius: 16px;
				border: 1px dashed color-mix(in srgb, var(--text-normal) 22%, transparent);
				background: transparent;
				color: var(--text-muted);
				font-family: inherit;
				font-size: 1em;
				font-weight: 300;
				cursor: pointer;
				transition: color .3s ease, border-color .3s ease, background-color .3s ease;

				& p7t-icon {
					width: 2em;
					height: 2em;
				}

				&:hover,
				&:focus-visible {
					outline: none;
					color: color-mix(in srgb, var(--p7t-flare-accent, var(--interactive-accent)) 70%, var(--text-normal));
					border-color: color-mix(in srgb, var(--p7t-flare-accent, var(--interactive-accent)) 55%, transparent);
					background-color: color-mix(in srgb, var(--p7t-flare-accent, var(--interactive-accent)) 6%, transparent);
				}
			}

			.reflection {
				display: flex;
				flex-direction: column;
				gap: .5em;
				margin-block: .6em 1em;
			}

			h2 {
				display: flex;
				align-items: center;
				gap: .45em;
				margin: 0;
				padding-inline: .4em;
				font-size: 1.4em;
				font-weight: 250;
				letter-spacing: .02em;

				& p7t-icon {
					width: 1.1em;
					height: 1.1em;
					color: color-mix(in srgb, var(--p7t-flare-accent, var(--interactive-accent)) 65%, var(--text-normal));
				}

				& .count {
					color: var(--text-faint, var(--text-muted));
					font-size: .7em;
					font-weight: 400;
				}
			}

			.panel {
				padding: .7em .4em;
				border-radius: 16px;
				background-color: var(--background-primary-alt);
			}
		`
	}

	protected override connected() {
		// The page's shape comes from parent references and flags on the entities themselves, so it can change without
		// any listing changing — reparenting leaves every membership intact. This observes the kinds it is built from.
		this.storeSubscription = core.store.subscribeTypes(['LunarDirective', 'Objective', 'Fate', 'Decree'], () => this.requestUpdate())
	}

	protected override disconnected() {
		this.storeSubscription?.()
		this.storeSubscription = undefined
	}

	protected override get template() {
		return html`
			<header class='masthead'>
				<div class='banner'></div>
				<h1>
					<p7t-icon icon='project-moonlight'></p7t-icon>
					Project Moonlight
				</h1>
			</header>
			${!this.directives.value ? html`<div class='notice'>Loading…</div>` : html`
				${this.cardsTemplate}
				${this.reflectionTemplate}
			`}
		`
	}

	private get cardsTemplate() {
		const roots = buildMoonlightTree({
			directives: this.directives.value,
			objectives: this.objectives.value,
			fates: this.fates.value,
			decrees: this.decrees.value
		})

		return html`
			<div class='cards'>
				${repeat(roots, root => root.directive.id, root => html`
					<p7t-moonlight-card .node=${root}></p7t-moonlight-card>
				`)}
				<button class='new-card' @click=${() => createEntity('lunar-directive')}>
					<p7t-icon icon='lucide:plus'></p7t-icon>
					<span>New Lunar Directive</span>
				</button>
			</div>
		`
	}

	private get reflectionTemplate() {
		const entries = buildLunarReflection({ directives: this.directives.value, decrees: this.decrees.value })
		return html`
			<section class='reflection'>
				<h2>
					<p7t-icon icon='reflective'></p7t-icon>
					Lunar Reflection
					<span class='count'>${entries.length}</span>
				</h2>
				<div class='panel'>
					${entries.length > 0
						? html`<p7t-lunar-reflection-list .entries=${entries}></p7t-lunar-reflection-list>`
						: html`<div class='notice'>No decree takes part in Lunar Reflection yet.</div>`}
				</div>
			</section>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-moonlight-view': MoonlightView
	}
}
