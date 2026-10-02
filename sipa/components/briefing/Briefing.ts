import { Component, component, css, eventListener, html, query, state } from "@a11d/lit"
import { catchUpTicks, core, DerivedRef, PageDeck, PreferenceModal, type DeckPage, type IconName, type PageSwap } from ".."

/** A briefing tab: a page of the deck, with the icon and label its navigation item shows. */
interface BriefingTab extends DeckPage {
	readonly icon: IconName
	readonly label: string
}

/** The throne room, the one page the hero stands over in full. */
const throne = 'throne'

/** How long the hero takes to duck or rise when it animates, in milliseconds. */
const heroDuration = 320

/**
 * The briefing: the hero, the tab bar, and the pages of the tabs.
 *
 * The pages live in a {@link PageDeck}: each keeps its state while another is on show, a page that is not on show
 * costs nothing to draw, and switching fades one page out and the next in. The tab bar sticks to the top of the pane
 * as a page scrolls beneath it.
 *
 * The hero stands in full over the throne room. On any other page it ducks to a short strip of the date and the banked
 * starfire (its lore, executive orders and heading fold away as they fade), and it rises back with the throne room.
 *
 * Each page keeps its own scroll position, saved as the page leaves and restored at the deck's swap, while no page is
 * visible. The hero's motion is arranged around that: it animates only where it can be seen to without shoving a page
 * around, at the top of the pane, and otherwise settles at once, out of sight.
 *
 * A page that sticks chrome of its own to the top of the pane (a search bar, a grid's heading row) sticks it beneath
 * the tab bar: the briefing measures the bar and hands its height down as `--p7t-page-sticky-top`.
 */
@component('p7t-briefing')
export class Briefing extends Component {
	@state() page = throne

	/** Whether the hero is ducked to its strip: on its way down, or down. */
	@state() private heroDucked = false
	/** Whether the hero ducks or rises at once on its next change instead of animating. */
	@state() private heroInstant = false
	/** How long the hero takes to duck or rise when it animates, in milliseconds. */
	@state() private heroDuration = heroDuration

	/** The page the deck last brought on show. It trails {@link page} through a switch. */
	private displayed = throne
	/** Where each page was scrolled to when it was last left. */
	private readonly scrollPositions = new Map<string, number>()

	@query('.hero') private readonly heroElement!: HTMLElement
	@query('.navbar') private readonly navbarElement?: HTMLElement

	/** Hands the tab bar's height down to the pages, as where their own sticky chrome stops (`--p7t-page-sticky-top`). */
	private readonly navbarObserver = new ResizeObserver(([entry]) => {
		const height = entry?.borderBoxSize[0]?.blockSize ?? entry?.target.getBoundingClientRect().height ?? 0
		this.style.setProperty('--p7t-page-sticky-top', `${height}px`)
	})

	private readonly briefingRef = new DerivedRef(this, core.repos.briefing)

	get data() {
		return this.briefingRef.value
	}

	protected override connected() {
		void this.updateComplete.then(() => {
			if (this.isConnected && this.navbarElement) {
				this.navbarObserver.observe(this.navbarElement)
			}
		})
	}

	protected override disconnected() {
		this.navbarObserver.disconnect()
	}

	/** The tabs, in the order the tab bar shows them. */
	private get tabs(): readonly BriefingTab[] {
		return [
			{ key: throne, icon: 'everglow', label: 'Throne Room', template: () => html`<p7t-throne-view .data=${this.data}></p7t-throne-view>` },
			{ key: 'forecast', icon: 'polaris', label: 'Forecast', template: () => html`<p7t-forecast-view></p7t-forecast-view>` },
			{ key: 'planning', icon: 'onrush', label: 'Planning', template: () => html`<p7t-dependency-canvas></p7t-dependency-canvas>` },
			{ key: 'backlog', icon: 'directive', label: 'Backlog', template: () => html`<p7t-backlog-view></p7t-backlog-view>` },
			{ key: 'moonlight', icon: 'directive-lunar', label: 'Moonlight', template: () => html`<p7t-moonlight-view></p7t-moonlight-view>` },
			{ key: 'lore', icon: 'lorepage', label: 'Lore', template: () => html`<p7t-lore-view></p7t-lore-view>` }
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
			this.heroDucked = page !== throne
			return
		}

		// At the top of the pane the hero ducks as the throne room fades, carrying the tab bar up with it. It ducks faster
		// when the page coming in resumes part-way down, so that it is down by the swap. Any lower, the ducking would drag
		// the leaving page up the pane, so the hero waits for the swap and ducks out of sight.
		if (this.displayed === throne && this.heroAtTop) {
			this.heroInstant = false
			this.heroDuration = (this.scrollPositions.get(page) ?? 0) > 0 ? PageDeck.leaveDuration : heroDuration
			this.heroDucked = true
		}
	}

	private onPageLeave(page: string) {
		this.scrollPositions.set(page, this.scroller.scrollTop)
	}

	private async onPageSwap(swap: PageSwap) {
		this.displayed = swap.to
		const position = this.scrollPositions.get(swap.to) ?? 0
		const raised = swap.to === throne
		// A page that resumes part-way down has the hero out of sight above it, so the hero settles at once rather than
		// shifting the page while it ducks or rises. So does a hero left standing for the swap.
		this.heroInstant = position > 0 || (!raised && !this.heroDucked)
		this.heroDuration = heroDuration
		this.heroDucked = !raised
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
			 * The hero folds its own parts as it ducks (see BriefingHero); the briefing times the fold, through
			 * --p7t-hero-duck-duration, and lets the tab bar ride up into the full hero's foot but not into the strip.
			 */
			.hero {
				margin-bottom: -.3em;
				transition: margin-bottom var(--p7t-hero-duck-duration) cubic-bezier(.4, 0, .2, 1);
			}

			.hero[ducked] {
				margin-bottom: .3em;
			}

			@media (prefers-reduced-motion: reduce) {
				.hero {
					transition: none;
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
			<p7t-briefing-hero class='hero'
				.briefing=${this.data}
				?ducked=${this.heroDucked}
				style=${`--p7t-hero-duck-duration: ${this.heroInstant ? 0 : this.heroDuration}ms`}>
			</p7t-briefing-hero>
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
