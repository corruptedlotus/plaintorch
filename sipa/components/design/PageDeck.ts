import { Component, component, css, event, html, property, repeat, state, type HTMLTemplateResult, type PropertyValues } from '@a11d/lit'
import { catchUpTicks } from '../system/globalTick'

/** One page of a {@link PageDeck}. */
export interface DeckPage {
	/** Identifies the page: the deck shows the page whose key is its {@link PageDeck.active | active} key. */
	readonly key: string
	/**
	 * Draws the page's content. Not called until the page is first chosen; from then on it is called on each render of
	 * the deck while the page is on show, and not at all while it is dormant.
	 */
	readonly template: () => HTMLTemplateResult
}

/** A page change at the moment the deck carries it out: the page that has just left, and the page now on show. */
export interface PageSwap {
	readonly from: string
	readonly to: string
}

/** Resolves once an animation has finished or been cancelled; at once for none. */
function settled(animation: Animation | undefined): Promise<unknown> {
	return animation?.finished.catch(() => undefined) ?? Promise.resolve()
}

/**
 * A deck of persistent pages, one on show at a time: the body of a tabbed view.
 *
 * A page is drawn the first time it is chosen and then kept. Switching away leaves it in the DOM with all of its state
 * (subscriptions, expanded rows, pan and zoom, element identity), so coming back to it is instant and nothing is drawn
 * again. A page that is not on show is **dormant**:
 * - it is `content-visibility: hidden`, so the browser skips its style, layout and paint and stops servicing its CSS
 *   animations — a dormant page costs no GPU time;
 * - it is `inert`, out of the focus order and the accessibility tree;
 * - its template is frozen: the deck does not re-run it, so the host's re-renders do not reach the page until it wakes;
 * - its tick-driven views (`subscribeElementTick`) skip their ticks, and the deck catches them up the moment it wakes.
 * The page's own reactive state still moves (a subscription can still re-render it); it is just not drawn.
 *
 * A switch is a *fade-through* along the tab order. The page on show fades out, sliding away from the side the new page
 * comes from, and only then does the new page slide in. The pages never overlap, so the deck is always as tall as the
 * one page it shows, and a host can rearrange what surrounds the deck (its scroll position, its chrome) at the
 * {@link pageswap} moment in between, while no page is visible. A switch can be interrupted: choosing another page
 * while one is coming in sends that one out from where it had got to, and choosing again the page that is leaving turns
 * it round.
 *
 * Every page sits in the deck's one grid cell. A dormant page's size containment keeps it from adding to the deck's
 * height, and the page on show stretches to the cell, so content sized `height: 100%` fills a deck that is given a
 * height.
 *
 * The deck does not scroll. The scroll position belongs to whatever scrolls around it and is shared by every page; a
 * host that wants each page to keep its own saves it at {@link pageleave} and restores it at {@link pageswap}.
 */
@component('p7t-page-deck')
export class PageDeck extends Component {
	/** How long the page on show takes to leave, in milliseconds. */
	static readonly leaveDuration = 150
	/** How long the new page takes to come in once the previous one has left, in milliseconds. */
	static readonly enterDuration = 250

	/** The pages, in tab order. The order decides which way a page slides. */
	@property({ attribute: false }) pages: readonly DeckPage[] = []

	/** The key of the page to show. A key that names no page is ignored. */
	@property() active?: string

	/** Fired as a page starts to leave, with its key, before anything has moved. */
	@event() pageleave!: EventDispatcher<string>

	/**
	 * Fired once the leaving page has gone dormant and the new page is on show but still transparent, just before it
	 * starts to come in. A handler that rearranges the view around the deck here does so while no page is visible.
	 */
	@event() pageswap!: EventDispatcher<PageSwap>

	/** The keys of the pages drawn so far, in the order they were first chosen. A page is only ever appended, never moved. */
	@state() private mounted: readonly string[] = []

	/** The page on show, resting or coming in. Unset only while the previous page is still leaving. */
	@state() private shown?: string

	/** The page fading out: still drawn, no longer interactive. */
	@state() private leaving?: string

	/** Each page's template as last drawn, which a dormant page keeps. */
	private readonly frozen = new Map<string, HTMLTemplateResult>()

	/** The animation of the switch in flight: the leaving page's or the entering page's. */
	private animation?: Animation

	/** Bumped by each switch, so a switch that has been interrupted notices and stops. */
	private generation = 0

	static override get styles() {
		return css`
			:host {
				display: grid;
				/* Every page shares the one cell: the page on show sizes it, a dormant one adds nothing. */
				grid-template-columns: minmax(0, 1fr);
				/* The slide runs inside the deck's edges instead of widening whatever scrolls around it. */
				overflow-x: clip;
			}

			.page {
				grid-area: 1 / 1;
				min-width: 0;
				display: flex;
				flex-direction: column;
			}

			.page[dormant] {
				content-visibility: hidden;
			}
		`
	}

	protected override willUpdate(changed: PropertyValues<this>) {
		super.willUpdate(changed)
		// The first page is simply there, drawn in the same render as the deck.
		if (this.shown === undefined && this.leaving === undefined && this.active !== undefined && this.isPage(this.active)) {
			this.mount(this.active)
			this.shown = this.active
		}
	}

	protected override updated(changed: PropertyValues<this>) {
		super.updated(changed)
		if (changed.has('active') && this.active !== undefined) {
			void this.show(this.active)
		}
	}

	protected override get template() {
		return html`
			${repeat(this.mounted.filter(key => this.isPage(key)), key => key, key => {
				const dormant = key !== this.shown && key !== this.leaving
				return html`
					<div class='page' data-key=${key} ?dormant=${dormant} ?inert=${key !== this.shown}>
						${this.content(key, dormant)}
					</div>
				`
			})}
		`
	}

	/** A page's content: drawn afresh while it is on show or leaving, its last drawing while it is dormant. */
	private content(key: string, dormant: boolean) {
		let template = this.frozen.get(key)
		if (!dormant || template === undefined) {
			template = this.pages.find(page => page.key === key)!.template()
			this.frozen.set(key, template)
		}

		return template
	}

	/** Carries out a switch to the page with this key: out with the page on show, then in with this one. */
	private async show(key: string) {
		if (!this.isPage(key) || key === this.shown) {
			return
		}

		const generation = ++this.generation

		// The page on its way out is chosen again: it turns round and comes back.
		if (key === this.leaving) {
			const animation = this.animation
			this.leaving = undefined
			this.shown = key
			animation?.reverse()
			await settled(animation)
			if (generation === this.generation) {
				animation?.cancel()
				this.animation = undefined
			}

			return
		}

		// The page on show starts to leave. If a page is leaving already, it carries on and this one simply takes the
		// place of the page it was leaving for.
		if (this.shown !== undefined) {
			const outgoing = this.shown
			this.pageleave.dispatch(outgoing)
			this.animation = this.leave(outgoing, key)
			this.leaving = outgoing
			this.shown = undefined
		}

		// A page shown for the first time is drawn at once, still dormant, so that its first render runs while the
		// leaving page fades (a fade the compositor carries on with however busy the render keeps the page) rather than
		// in the gap after it.
		this.mount(key)

		const leave = this.animation
		await settled(leave)
		if (generation !== this.generation) {
			return
		}

		// The midpoint, with nothing visible: the leaving page goes dormant, the new one goes on show, and the host gets its
		// moment before the new page comes in.
		const departed = this.leaving!
		this.leaving = undefined
		this.shown = key
		await this.updateComplete
		if (generation !== this.generation) {
			return
		}

		// The departed page is dormant now, so its held-over fade can go.
		leave?.cancel()
		this.pageswap.dispatch({ from: departed, to: key })
		catchUpTicks()

		const enter = this.enter(key, this.direction(departed, key))
		this.animation = enter
		await settled(enter)
		if (generation === this.generation) {
			this.animation = undefined
		}
	}

	/**
	 * Fades the page out, sliding away from the side the page it leaves for sits on. A page caught while it was still
	 * coming in leaves from where it had got to, and takes proportionally less time about it.
	 */
	private leave(key: string, toward: string): Animation | undefined {
		const element = this.pageElement(key)
		if (!element) {
			return undefined
		}

		const current = getComputedStyle(element)
		const from = { opacity: current.opacity, translate: current.translate }
		this.animation?.cancel()
		return element.animate([from, { opacity: 0, translate: this.offset(-this.direction(key, toward)) }], {
			duration: PageDeck.leaveDuration * Number(from.opacity),
			easing: 'cubic-bezier(.4, 0, 1, 1)',
			// Held until the page is dormant, or it would flash back in for a frame.
			fill: 'forwards'
		})
	}

	/** Fades the page in, sliding in from its own side: from the right when `direction` is positive. */
	private enter(key: string, direction: number): Animation | undefined {
		return this.pageElement(key)?.animate([{ opacity: 0, translate: this.offset(direction) }, { opacity: 1, translate: '0' }], {
			duration: PageDeck.enterDuration,
			easing: 'cubic-bezier(0, 0, .2, 1)'
		})
	}

	/** How far a page slides, toward the right for a positive direction. No slide at all under reduced motion. */
	private offset(direction: number) {
		return matchMedia('(prefers-reduced-motion: reduce)').matches ? '0' : `${direction * 1.5}em`
	}

	/** Which way the tab order runs from one page to another: 1 forward, -1 back. */
	private direction(from: string, to: string) {
		const index = (key: string) => this.pages.findIndex(page => page.key === key)
		return Math.sign(index(to) - index(from)) || 1
	}

	private isPage(key: string) {
		return this.pages.some(page => page.key === key)
	}

	private mount(key: string) {
		if (!this.mounted.includes(key)) {
			this.mounted = [...this.mounted, key]
		}
	}

	private pageElement(key: string) {
		return this.shadowRoot?.querySelector<HTMLElement>(`.page[data-key="${CSS.escape(key)}"]`) ?? undefined
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-page-deck': PageDeck
	}
}
