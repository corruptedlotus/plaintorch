import { Component, css, html, query, type SVGTemplateResult } from '@a11d/lit'
import { subscribeElementTick } from '../system/globalTick'

const svgNamespace = 'http://www.w3.org/2000/svg'
/** The longest frame step, in milliseconds, so a stalled frame never throws a particle across its whole path. */
const maximumStep = 50

const reducedMotion = matchMedia('(prefers-reduced-motion: reduce)')

/**
 * An ambient particle animation, drawn on an SVG stage that fills the element: the frame loop, the stage's geometry and
 * the spawn clock shared by the briefing's backdrops ({@link RisingStars}, {@link RisingEmbers}).
 *
 * Purely decorative — hidden from assistive technology and blind to the pointer. It has no size of its own (it is
 * fully contained), so it fills whatever box it is given: a backdrop laid over its container with
 * `position: absolute; inset: 0`, say. The host clips everything drawn to that box, rounded corners included, which
 * lets a consumer soften the drawing through the `stage` part (a blur, say) without the softening spilling past the
 * edges.
 *
 * It only animates while it can be seen: scrolled out of view, or inside a `display: none` or
 * `content-visibility: hidden` subtree (a dormant page of a `p7t-page-deck`), it sleeps with its particles where they
 * were, and picks up from there once shown again.
 *
 * A derivation draws the layers its particles live in ({@link layers}), moves them on each frame ({@link advance}) and
 * drops them all as the element leaves the document ({@link clear}).
 */
export abstract class ParticleStageBase extends Component {
	static override get styles() {
		return css`
			:host {
				display: block;
				position: relative;
				overflow: clip;
				pointer-events: none;
				contain: strict;
			}

			svg {
				position: absolute;
				inset: 0;
				width: 100%;
				height: 100%;
				overflow: visible;
			}
		`
	}

	@query('svg') private readonly stage?: SVGSVGElement

	/** The stage's width in px, as last measured. */
	protected width = 0
	/** The stage's height in px, as last measured. */
	protected height = 0
	/** Scales speeds, strokes and sizes with the stage, from 0.7 to 1.4, so a small stage gets a gentler show. */
	protected scale = 1
	/** Whether the stage runs right to left, which puts its inline-start edge on the right. */
	protected rtl = false

	/** Spawns owed but not yet made; a whole one is made each time it reaches 1. */
	private owed = 0
	/** The timestamp of the last frame, or 0 when the next frame starts the clock afresh. */
	private lastFrame = 0
	private frame?: number
	/** Whether any of the element is inside the viewport. */
	private onScreen = false
	/** While asleep in a hidden subtree: the subscription that wakes the animation once the element shows again. */
	private wake?: () => void

	private readonly resizeObserver = new ResizeObserver(() => this.measure())
	private readonly intersectionObserver = new IntersectionObserver(([entry]) => {
		this.onScreen = !!entry?.isIntersecting
		this.resume()
	})

	protected override get template() {
		return html`<svg part='stage' aria-hidden='true'>${this.layers}</svg>`
	}

	/** What the stage holds: the layers the particles are drawn into. */
	protected abstract get layers(): SVGTemplateResult

	/** Moves the animation on by one frame, `delta` milliseconds after the last. */
	protected abstract advance(delta: number): void

	/** Removes every particle, as the element leaves the document. */
	protected abstract clear(): void

	/** Whether the user prefers reduced motion. */
	protected get reducedMotion() {
		return reducedMotion.matches
	}

	/**
	 * How many particles to make this frame, at `perSecond` over `delta` milliseconds: jittered, so they come in
	 * irregular clumps, and a third as many with reduced motion preferred. The fraction left over carries to the next
	 * frame.
	 */
	protected spawns(perSecond: number, delta: number) {
		this.owed += delta / 1000 * perSecond * (this.reducedMotion ? .3 : 1) * random(.4, 1.6)
		const count = Math.floor(this.owed)
		this.owed -= count
		return count
	}

	protected override connected() {
		void this.updateComplete.then(() => {
			if (this.isConnected) {
				this.measure()
				this.resizeObserver.observe(this)
				this.intersectionObserver.observe(this)
			}
		})
	}

	protected override disconnected() {
		this.resizeObserver.disconnect()
		this.intersectionObserver.disconnect()
		this.halt()
		this.clear()
		this.owed = 0
	}

	private measure() {
		const bounds = this.getBoundingClientRect()
		this.width = Math.max(1, bounds.width)
		this.height = Math.max(1, bounds.height)
		this.scale = Math.min(1.4, Math.max(.7, Math.min(this.width, this.height) / 600))
		this.rtl = getComputedStyle(this).direction === 'rtl'
		this.stage?.setAttribute('viewBox', `0 0 ${this.width} ${this.height}`)
	}

	/** Starts the frame loop, unless it already runs or the element is off screen. */
	private resume() {
		if (this.frame === undefined && this.onScreen && this.isConnected) {
			this.lastFrame = 0
			this.frame = requestAnimationFrame(now => this.step(now))
		}
	}

	private halt() {
		if (this.frame !== undefined) {
			cancelAnimationFrame(this.frame)
			this.frame = undefined
		}

		this.wake?.()
		this.wake = undefined
	}

	private step(now: number) {
		this.frame = undefined
		// Off screen, the intersection observer resumes it. Hidden (a dormant page), no observer reports the reveal; the
		// app's shared tick does, as it only ticks an element that shows.
		if (!this.onScreen) {
			return
		}

		if (!this.checkVisibility()) {
			this.wake ??= subscribeElementTick(this, () => {
				this.wake?.()
				this.wake = undefined
				this.resume()
			})
			return
		}

		const delta = this.lastFrame ? Math.min(maximumStep, now - this.lastFrame) : 16
		this.lastFrame = now
		this.advance(delta)
		this.frame = requestAnimationFrame(now => this.step(now))
	}
}

/** Creates an SVG element with the given attributes, appended to `parent` when one is given. */
export function svgElement<K extends keyof SVGElementTagNameMap>(name: K, attributes: Record<string, string | number>, parent?: Element) {
	const element = document.createElementNS(svgNamespace, name)
	for (const [key, value] of Object.entries(attributes)) {
		element.setAttribute(key, String(value))
	}

	parent?.append(element)
	return element
}

/** A random number from `from` up to `to`. */
export function random(from: number, to: number) {
	return from + Math.random() * (to - from)
}

/** A setting held within `minimum` and `maximum`, or the fallback when it is not a finite number. */
export function setting(value: number, fallback: number, minimum: number, maximum: number) {
	return Math.min(maximum, Math.max(minimum, Number.isFinite(value) ? value : fallback))
}

/** An SVG translation to the point, rounded to hundredths. */
export function translate(x: number, y: number) {
	return `translate(${x.toFixed(2)},${y.toFixed(2)})`
}
