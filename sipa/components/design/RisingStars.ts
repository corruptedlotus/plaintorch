import { Component, component, css, html, property, query } from '@a11d/lit'
import { subscribeElementTick } from '../system/globalTick'

const svgNamespace = 'http://www.w3.org/2000/svg'
/** How long a star takes to fade in once its bubble bursts, in milliseconds. */
const starFadeIn = 100
/** How long a star takes to fade out, from the moment its ring is gone, in milliseconds. */
const starFadeOut = 450
/** The most bubbles and bursts in flight at once; a spawn beyond it is dropped. */
const maximumParticles = 300
/** The longest frame step, in milliseconds, so a stalled frame never throws a particle across its whole path. */
const maximumStep = 50

const reducedMotion = matchMedia('(prefers-reduced-motion: reduce)')

/** One bubble on its way up, or the ring and star it burst into. */
interface Particle {
	readonly group: SVGGElement
	readonly circle: SVGCircleElement
	star?: SVGPathElement
	/** Rising, or burst. */
	burst: boolean
	/** Milliseconds spent in the current phase. */
	elapsed: number
	readonly x: number
	readonly startY: number
	readonly endY: number
	/** The bubble's starting radius. */
	readonly radius: number
	/** How long the rise takes, in milliseconds. */
	readonly duration: number
	/** The ring's final radius. */
	ringRadius: number
	ringStroke: number
	/** How long the ring takes to expand and fade, in milliseconds. */
	ringDuration: number
	/** How far the star turns over its life, in degrees. */
	tilt: number
}

/**
 * Rising stars: small circles rise from the bottom edge, shrinking as they climb and slowing near the top, then each
 * bursts into a hollow ring that races outward as it fades and a four-pointed star that tilts as it pops. Most bursts
 * stay small; now and then one blooms large. Ported from the "Rising Stars" design artefact; the attributes are its
 * settings, under the same names.
 *
 * Purely decorative — hidden from assistive technology and blind to the pointer — and drawn in the flare accent
 * (`--p7t-flare-accent`, falling back to the interactive accent), so it takes the colour of whatever it is placed in.
 * It has no size of its own (it is fully contained), so it fills whatever box it is given — a backdrop laid over its
 * container with `position: absolute; inset: 0`, say. The host clips everything drawn to that box, rounded corners
 * included, which lets a consumer soften the drawing through the `stage` part (a blur, say) without the softening
 * spilling past the edges.
 *
 * It only animates while it can be seen: scrolled out of view, or inside a `display: none` or
 * `content-visibility: hidden` subtree (a dormant page of a `p7t-page-deck`), it sleeps with its particles where they
 * were, and picks up from there once shown again. With reduced motion preferred, a third as many bubbles rise.
 */
@component('p7t-rising-stars')
export class RisingStars extends Component {
	/** Bubbles per second for every 100 px of spawn width. */
	@property({ type: Number }) density = 1.2
	/** The bubbles' starting diameter in px. Each one varies randomly from 50% to 150% of it. */
	@property({ type: Number, attribute: 'bubble-size' }) bubbleSize = 12
	/** The width of the spawn area, in % of the element's width, measured from the inline-start edge. */
	@property({ type: Number, attribute: 'max-width' }) maxWidth = 100
	/** The highest a bubble rises from the bottom, in % of the element's height. Each stops between 40% and 100% of it. */
	@property({ type: Number, attribute: 'max-height' }) maxHeight = 80
	/** The diameter of the largest ring, relative to the element (the geometric mean of its sides). Most pops stay small. */
	@property({ type: Number, attribute: 'pop-max' }) popMax = .5

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

			.dot, .star {
				fill: var(--p7t-flare-accent, var(--interactive-accent));
			}

			.ring {
				fill: none;
				stroke: var(--p7t-flare-accent, var(--interactive-accent));
			}
		`
	}

	@query('.layer') private readonly layer?: SVGGElement
	@query('svg') private readonly stage?: SVGSVGElement

	private particles = new Array<Particle>()
	/** Spawns owed but not yet made; a whole one spawns each time it reaches 1. */
	private owed = 0
	/** The timestamp of the last frame, or 0 when the next frame starts the clock afresh. */
	private lastFrame = 0
	private frame?: number

	private width = 0
	private height = 0
	/** Scales speeds, strokes and star sizes with the element, so a small stage gets a gentler show. */
	private scale = 1
	private rtl = false
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
		return html`<svg part='stage' aria-hidden='true'><g class='layer'></g></svg>`
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
		for (const particle of this.particles) {
			particle.group.remove()
		}

		this.particles = []
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

		const spawnWidth = this.width * percentage(this.maxWidth, 100)
		const rate = nonNegative(this.density, 1.2) * spawnWidth / 100 * (reducedMotion.matches ? .3 : 1)
		this.owed += delta / 1000 * rate * random(.4, 1.6)
		while (this.owed >= 1) {
			this.spawn(spawnWidth)
			this.owed--
		}

		this.particles = this.particles.filter(particle => this.advance(particle, delta))
		this.frame = requestAnimationFrame(now => this.step(now))
	}

	private spawn(spawnWidth: number) {
		const riseHeight = this.height * percentage(this.maxHeight, 80)
		if (!this.layer || this.particles.length >= maximumParticles || riseHeight <= 0 || spawnWidth <= 0) {
			return
		}

		const radius = Math.max(1, nonNegative(this.bubbleSize, 12)) * random(.5, 1.5) / 2
		const band = Math.max(2 * radius, spawnWidth)
		const offset = random(radius, band - radius)
		const x = this.rtl ? this.width - offset : offset
		const startY = this.height + radius
		const endY = this.height - riseHeight * random(.4, 1)
		const speed = random(110, 210) * this.scale
		const group = svg('g', { transform: translate(x, startY) })
		const circle = svg('circle', { class: 'dot', cx: 0, cy: 0, r: radius })
		group.append(circle)
		this.layer.append(group)
		this.particles.push({
			group, circle, burst: false, elapsed: 0,
			x, startY, endY, radius,
			duration: Math.max(900, (startY - endY) / speed * 1000),
			ringRadius: 0, ringStroke: 0, ringDuration: 0, tilt: 0,
		})
	}

	private burst(particle: Particle) {
		particle.burst = true
		particle.elapsed = 0
		// u⁴ skews the pop size toward small, with the occasional huge one.
		const u = Math.pow(Math.random(), 4)
		const smallest = 12 * this.scale + particle.radius * 2
		const largest = Math.max(smallest, nonNegative(this.popMax, .5) * Math.sqrt(this.width * this.height) / 2)
		particle.ringRadius = smallest + (largest - smallest) * u
		particle.ringDuration = 380 + 420 * u
		particle.ringStroke = (2.5 + 4 * u) * this.scale
		particle.tilt = random(52, 68)
		particle.circle.setAttribute('class', 'ring')
		particle.circle.setAttribute('r', '0')
		particle.circle.setAttribute('stroke-width', particle.ringStroke.toFixed(2))
		particle.star = svg('path', { class: 'star', d: sparkle(particle.radius * 1.4 + (6 + 10 * u) * this.scale), opacity: 0 })
		particle.group.append(particle.star)
	}

	/** Moves a particle on by a frame; false once it is spent and removed. */
	private advance(particle: Particle, delta: number) {
		particle.elapsed += delta
		if (!particle.burst) {
			const progress = Math.min(1, particle.elapsed / particle.duration)
			const eased = 1 - Math.pow(1 - progress, 1.6)
			particle.group.setAttribute('transform', translate(particle.x, particle.startY + (particle.endY - particle.startY) * eased))
			particle.circle.setAttribute('r', Math.max(0, particle.radius * (1 - progress)).toFixed(2))
			if (progress >= 1) {
				this.burst(particle)
			}

			return true
		}

		const elapsed = particle.elapsed
		const life = particle.ringDuration + starFadeOut
		// The ring expands fast and thins as it fades.
		const ring = Math.min(1, elapsed / particle.ringDuration)
		particle.circle.setAttribute('r', ((1 - Math.pow(1 - ring, 3)) * particle.ringRadius).toFixed(2))
		particle.circle.setAttribute('stroke-width', (particle.ringStroke * (1 - ring * .75)).toFixed(2))
		particle.circle.setAttribute('opacity', (1 - ring).toFixed(3))
		// The star fades in, holds while the ring lasts, then fades out as it shrinks a little; it tilts throughout.
		let opacity: number
		let size: number
		if (elapsed < starFadeIn) {
			opacity = elapsed / starFadeIn
			size = .5 + .5 * (1 - Math.pow(1 - opacity, 2))
		} else if (elapsed < particle.ringDuration) {
			opacity = 1
			size = 1
		} else {
			const fade = Math.min(1, (elapsed - particle.ringDuration) / starFadeOut)
			opacity = 1 - fade
			size = 1 - .25 * fade
		}

		const angle = particle.tilt * (1 - Math.pow(1 - Math.min(1, elapsed / life), 3))
		particle.star!.setAttribute('opacity', opacity.toFixed(3))
		particle.star!.setAttribute('transform', `rotate(${angle.toFixed(2)}) scale(${size.toFixed(3)})`)
		if (elapsed >= life) {
			particle.group.remove()
			return false
		}

		return true
	}
}

function svg<K extends keyof SVGElementTagNameMap>(name: K, attributes: Record<string, string | number>) {
	const element = document.createElementNS(svgNamespace, name)
	for (const [key, value] of Object.entries(attributes)) {
		element.setAttribute(key, String(value))
	}

	return element
}

function translate(x: number, y: number) {
	return `translate(${x.toFixed(2)},${y.toFixed(2)})`
}

function random(from: number, to: number) {
	return from + Math.random() * (to - from)
}

/** The value, or the fallback when it is not a finite number at or above zero. */
function nonNegative(value: number, fallback: number) {
	return Number.isFinite(value) && value >= 0 ? value : fallback
}

/** A percentage as a fraction, capped at 100%. */
function percentage(value: number, fallback: number) {
	return Math.min(100, nonNegative(value, fallback)) / 100
}

/** A four-pointed star with concave sides, centred on the origin, reaching `radius` at each point. */
function sparkle(radius: number) {
	const r = radius.toFixed(2)
	const c = (radius * .14).toFixed(2)
	return `M0,-${r} Q${c},-${c} ${r},0 Q${c},${c} 0,${r} Q-${c},${c} -${r},0 Q-${c},-${c} 0,-${r}Z`
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-rising-stars': RisingStars
	}
}
