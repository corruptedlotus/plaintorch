import { Component, component, css, eventListener, html, query, state } from "@a11d/lit"
import { catchUpTicks, core, DerivedRef, PageDeck, PreferenceModal, type DeckPage, type IconName, type PageSwap } from ".."

/** A briefing tab: a page of the deck, with the icon and label its navigation item shows. */
interface BriefingTab extends DeckPage {
	readonly icon: IconName
	readonly label: string
}

/** The throne room, the one page the hero stands over. */
const throne = 'throne'

/** How long the hero's space takes to close or open when it animates, in milliseconds. */
const heroDuration = 320

/**
 * The briefing: the hero, the tab bar, and the pages of the tabs.
 *
 * The pages live in a {@link PageDeck}: each keeps its state while another is on show, a page that is not on show
 * costs nothing to draw, and switching fades one page out and the next in. The tab bar sticks to the top of the pane
 * as a page scrolls beneath it.
 *
 * The hero belongs to the throne room. On any other page it ducks away (it slides up under the top edge as its space
 * closes, fading as it goes) and goes dormant; it rises back with the throne room.
 *
 * Each page keeps its own scroll position, saved as the page leaves and restored at the deck's swap, while no page is
 * visible. The hero's motion is arranged around that: it animates only where it can be seen to without shoving a page
 * around, at the top of the pane, and otherwise settles at once, out of sight.
 */
@component('p7t-briefing')
export class Briefing extends Component {
	@state() page = throne

	/** Whether the hero is faded out: on its way out, or gone. */
	@state() private heroFaded = false
	/** Whether the hero's space is closed, which leaves it dormant. */
	@state() private heroCollapsed = false
	/** Whether the hero's space opens or closes at once on its next change instead of animating. */
	@state() private heroInstant = false
	/** How long the hero's space takes to open or close when it animates, in milliseconds. */
	@state() private heroDuration = heroDuration

	/** The page the deck last brought on show. It trails {@link page} through a switch. */
	private displayed = throne
	/** Where each page was scrolled to when it was last left. */
	private readonly scrollPositions = new Map<string, number>()

	@query('.hero') private readonly heroElement!: HTMLElement

	private readonly briefingRef = new DerivedRef(this, core.repos.briefing)

	get data() {
		return this.briefingRef.value
	}

	/** The tabs, in the order the tab bar shows them. */
	private get tabs(): readonly BriefingTab[] {
		return [
			{ key: throne, icon: 'everglow', label: 'Throne Room', template: () => html`<p7t-throne-view .data=${this.data}></p7t-throne-view>` },
			{ key: 'planning', icon: 'onrush', label: 'Planning', template: () => html`<p7t-dependency-canvas></p7t-dependency-canvas>` },
			{ key: 'backlog', icon: 'directive', label: 'Backlog', template: () => html`<p7t-entity-grid></p7t-entity-grid>` },
			{ key: 'moonlight', icon: 'directive-lunar', label: 'Moonlight', template: () => html`<p7t-moonlight-view></p7t-moonlight-view>` },
			{ key: 'lore', icon: 'lorepage', label: 'Lore', template: () => html`<p7t-lore-grid></p7t-lore-grid>` }
		]
	}

	/** What the briefing scrolls in: the nearest ancestor that scrolls vertically, across shadow roots. */
	private get scroller(): Element {
		for (let node: Node | null = this.parentNode; node; node = node instanceof ShadowRoot ? node.host : node.parentNode) {
			if (node instanceof Element && /auto|scroll|overlay/.test(getComputedStyle(node).overflowY)) {
				return node
			}
		}

		return document.scrollingElement ?? document.documentElement
	}

	/** Whether the hero stands wholly in view at the top of the pane. */
	private get heroAtTop() {
		return this.heroElement.getBoundingClientRect().top >= this.scroller.getBoundingClientRect().top
	}

	@eventListener('requestKeyNavigation')
	protected onRequestKeyNavigation(e: CustomEvent<string>) {
		e.stopPropagation()
		if (e.detail && e.detail !== this.page) {
			this.navigate(e.detail)
		}
	}

	private navigate(page: string) {
		this.page = page

		// Chosen back before the deck's swap: the page that was leaving turns round, and the hero with it.
		if (page === this.displayed) {
			this.heroInstant = false
			this.heroDuration = heroDuration
			this.heroFaded = this.heroCollapsed = page !== throne
			return
		}

		if (this.displayed === throne) {
			this.heroFaded = true
			// At the top of the pane the hero's space closes as the throne room fades, carrying the tab bar up with it. It
			// closes faster when the page coming in resumes part-way down, so that it is shut by the swap. Any lower, the
			// closing would drag the leaving page up the pane, so the space waits for the swap and closes out of sight.
			if (this.heroAtTop) {
				this.heroInstant = false
				this.heroDuration = (this.scrollPositions.get(page) ?? 0) > 0 ? PageDeck.leaveDuration : heroDuration
				this.heroCollapsed = true
			}
		}
	}

	private onPageLeave(page: string) {
		this.scrollPositions.set(page, this.scroller.scrollTop)
	}

	private async onPageSwap(swap: PageSwap) {
		this.displayed = swap.to
		const position = this.scrollPositions.get(swap.to) ?? 0
		const raised = swap.to === throne
		// A page that resumes part-way down has the hero out of sight above it, so the hero's space settles at once rather
		// than shifting the page while it opens or closes. So does a space that was left open for the swap.
		this.heroInstant = position > 0 || (!raised && !this.heroCollapsed)
		this.heroDuration = heroDuration
		this.heroFaded = !raised
		this.heroCollapsed = !raised
		await this.updateComplete
		this.scroller.scrollTop = position
		catchUpTicks()
	}

	/** Opens the settings modal. The settings sit at the end of the tab row but act as a button, not a nav tab. */
	private openSettings() {
		new PreferenceModal().open()
	}

	static override get styles() {
		return css`
			:host {
				/* The pane's height at least, so a page can fill what is left beneath the tab bar, and free to grow past it:
				   the tab bar stays stuck only as far down as the host reaches. */
				min-height: 100%;
				display: grid;
				align-items: stretch;
				grid-template-rows: auto auto 1fr;
			}

			/*
			 * Three layers: the space (height), the slide (fade, shrink and slide), and the hero itself, which goes dormant
			 * once its space has closed. Only the innermost goes dormant because an element inside a skipped subtree does
			 * not transition reliably as the subtree is revealed, so every transition runs on a layer that is never skipped.
			 */
			.hero {
				margin-bottom: -.4em;
				interpolate-size: allow-keywords;
				transition:
					height var(--hero-duration) cubic-bezier(.4, 0, .2, 1),
					margin-bottom var(--hero-duration) cubic-bezier(.4, 0, .2, 1);
			}

			.hero[collapsed] {
				height: 0;
				margin-bottom: 0;
			}

			/* The translate comes last, so an instant change that keeps only the first two keeps their own timings. */
			.slide {
				transform-origin: top center;
				transition:
					opacity ${PageDeck.enterDuration}ms cubic-bezier(0, 0, .2, 1),
					scale ${PageDeck.enterDuration}ms cubic-bezier(0, 0, .2, 1),
					translate var(--hero-duration) cubic-bezier(.4, 0, .2, 1);
			}

			.hero[faded] > .slide {
				opacity: 0;
				scale: .96;
				transition-duration: ${PageDeck.leaveDuration}ms, ${PageDeck.leaveDuration}ms, var(--hero-duration);
				transition-timing-function: cubic-bezier(.4, 0, 1, 1), cubic-bezier(.4, 0, 1, 1), cubic-bezier(.4, 0, .2, 1);
			}

			/* Slides up by its own height as its space closes, so its lower edge rides on the tab bar all the way. */
			.hero[collapsed] > .slide {
				translate: 0 -100%;
			}

			p7t-briefing-hero {
				margin-bottom: .1em;
				transition: content-visibility var(--hero-duration) allow-discrete;
			}

			.hero[collapsed] p7t-briefing-hero {
				content-visibility: hidden;
			}

			.hero[instant], .hero[instant] p7t-briefing-hero {
				transition: none;
			}

			.hero[instant] > .slide {
				transition-property: opacity, scale;
			}

			/* Under reduced motion the hero only fades; its space opens and closes at once. */
			@media (prefers-reduced-motion: reduce) {
				.hero, .hero p7t-briefing-hero {
					transition: none;
				}

				.hero > .slide, .hero[instant] > .slide {
					transition-property: opacity;
				}
			}

			.navbar {
				position: sticky;
				top: 0;
				z-index: 3;
				/* Lets the tab row tell when it is stuck, to put a backdrop behind itself. */
				container-type: scroll-state;
			}

			.tabs {
				position: relative;
				display: flex;
				justify-content: center;
				align-items: center;
				/* As wide as the pages' panel, which runs half an em past the bar on either side. */
				margin-inline: -.5em;
				padding-inline: 1em;
				transition: background-color .2s ease, box-shadow .2s ease;
			}

			@container scroll-state(stuck: top) {
				.tabs {
					/* Covers the page scrolling beneath, and the strip of the pane's own padding above. */
					background-color: var(--background-primary);
					box-shadow:
						0 -1em 0 var(--background-primary),
						0 .8em 1em -1em rgb(0 0 0 / .6);
				}
			}

			/* A runtime that cannot tell when the row is stuck keeps its backdrop on, so no page shows through it. */
			@supports not (container-type: scroll-state) {
				.tabs {
					background-color: var(--background-primary);
				}
			}

			.pages {
				background-color: color-mix(in srgb, black 20%, transparent);
				border-radius: 16px;
				padding-inline: .5em;
				padding-block: .4em 0;
				margin-block: 0 -.1em;
				margin-inline: -.5em;
			}

			.settings {
				position: absolute;
				right: .9em;
				top: 50%;
				transform: translateY(-50%);
				color: var(--text-muted);
				font-size: .9em;
			}

			.settings p7t-icon {
				width: 20px;
				height: 20px;
			}
		`
	}

	override get template() {
		const tabs = this.tabs
		return html`
			<div class='hero'
				?faded=${this.heroFaded}
				?collapsed=${this.heroCollapsed}
				?instant=${this.heroInstant}
				?inert=${this.heroFaded}
				style=${`--hero-duration: ${this.heroDuration}ms`}>
				<div class='slide'>
					<p7t-briefing-hero .briefing=${this.data}></p7t-briefing-hero>
				</div>
			</div>
			<nav class='navbar'>
				<div class='tabs'>
					${tabs.map(tab => html`
						<p7t-navitem key=${tab.key} icon=${tab.icon} ?active=${this.page === tab.key}>${tab.label}</p7t-navitem>
					`)}
					<p7t-button ghost emphasis label='Settings' class='settings' icon='lucide:settings' aria-label='Settings' @click=${() => this.openSettings()}></p7t-button>
				</div>
			</nav>
			<p7t-page-deck class='pages'
				.pages=${tabs}
				active=${this.page}
				@pageleave=${(e: CustomEvent<string>) => this.onPageLeave(e.detail)}
				@pageswap=${(e: CustomEvent<PageSwap>) => void this.onPageSwap(e.detail)}>
			</p7t-page-deck>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-briefing': Briefing
	}
}
