import { component, css, property, query, svg } from '@a11d/lit'
import { ParticleStageBase, random, setting, svgElement, translate } from './ParticleStageBase'

/** How long a star takes to fade in once its bubble bursts, in milliseconds. */
const starFadeIn = 100
/** How long a star takes to fade out, from the moment its ring is gone, in milliseconds. */
const starFadeOut = 450
/** The most bubbles and bursts in flight at once; a spawn beyond it is dropped. */
const maximumParticles = 300

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
 * Rising stars: small circles rise from the bottom edge, shrinking as they climb and slowing near the top. Some burst
 * there into a hollow ring that races outward as it fades and a four-pointed star (the briefing hero's) that tilts as
 * it pops; the rest simply shrink away. Most bursts stay small; now and then one blooms large. Ported from the "Rising
 * Stars" design artefact; the attributes are its settings, under the same names, plus the chance that a bubble bursts
 * at all.
 *
 * Drawn in the flare accent (`--p7t-flare-accent`, falling back to the interactive accent), so it takes the colour of
 * whatever it is placed in. The stage, its clipping and its sleeping are {@link ParticleStageBase}'s.
 */
@component('p7t-rising-stars')
export class RisingStars extends ParticleStageBase {
	/** Bubbles per second for every 100 px of spawn width. */
	@property({ type: Number }) density = 1.2
	/** The bubbles' starting diameter in px. Each one varies randomly from 50% to 150% of it. */
	@property({ type: Number, attribute: 'bubble-size' }) bubbleSize = 12
	/** The width of the spawn area, in % of the element's width, measured from the inline-start edge. */
	@property({ type: Number, attribute: 'max-width' }) maxWidth = 100
	/** The highest a bubble rises from the bottom, in % of the element's height. Each stops between 40% and 100% of it. */
	@property({ type: Number, attribute: 'max-height' }) maxHeight = 80
	/** The share of bubbles, from 0 to 1, that burst into stars at the top of their rise. The rest shrink away there. */
	@property({ type: Number, attribute: 'pop-chance' }) popChance = .5
	/** The diameter of the largest ring, relative to the element (the geometric mean of its sides). Most pops stay small. */
	@property({ type: Number, attribute: 'pop-max' }) popMax = .5

	static override get styles() {
		return css`
			${super.styles}

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

	private particles = new Array<Particle>()

	protected override get layers() {
		return svg`<g class='layer'></g>`
	}

	protected override advance(delta: number) {
		const spawnWidth = this.width * setting(this.maxWidth, 100, 0, 100) / 100
		const count = this.spawns(setting(this.density, 1.2, 0, Infinity) * spawnWidth / 100, delta)
		for (let index = 0; index < count; index++) {
			this.spawn(spawnWidth)
		}

		this.particles = this.particles.filter(particle => this.advanceParticle(particle, delta))
	}

	protected override clear() {
		for (const particle of this.particles) {
			particle.group.remove()
		}

		this.particles = []
	}

	private spawn(spawnWidth: number) {
		const riseHeight = this.height * setting(this.maxHeight, 80, 0, 100) / 100
		if (!this.layer || this.particles.length >= maximumParticles || riseHeight <= 0 || spawnWidth <= 0) {
			return
		}

		const radius = setting(this.bubbleSize, 12, 1, Infinity) * random(.5, 1.5) / 2
		const band = Math.max(2 * radius, spawnWidth)
		const offset = random(radius, band - radius)
		const x = this.rtl ? this.width - offset : offset
		const startY = this.height + radius
		const endY = this.height - riseHeight * random(.4, 1)
		const speed = random(110, 210) * this.scale
		const group = svgElement('g', { transform: translate(x, startY) }, this.layer)
		const circle = svgElement('circle', { class: 'dot', cx: 0, cy: 0, r: radius }, group)
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
		const largest = Math.max(smallest, setting(this.popMax, .5, 0, Infinity) * Math.sqrt(this.width * this.height) / 2)
		particle.ringRadius = smallest + (largest - smallest) * u
		particle.ringDuration = 380 + 420 * u
		particle.ringStroke = (2.5 + 4 * u) * this.scale
		particle.tilt = random(52, 68)
		particle.circle.setAttribute('class', 'ring')
		particle.circle.setAttribute('r', '0')
		particle.circle.setAttribute('stroke-width', particle.ringStroke.toFixed(2))
		particle.star = svgElement('path', { class: 'star', d: star(particle.radius * 1.4 + (6 + 10 * u) * this.scale), opacity: 0 }, particle.group)
	}

	/** Moves a particle on by a frame; false once it is spent and removed. */
	private advanceParticle(particle: Particle, delta: number) {
		particle.elapsed += delta
		if (!particle.burst) {
			const progress = Math.min(1, particle.elapsed / particle.duration)
			const eased = 1 - Math.pow(1 - progress, 1.6)
			particle.group.setAttribute('transform', translate(particle.x, particle.startY + (particle.endY - particle.startY) * eased))
			particle.circle.setAttribute('r', Math.max(0, particle.radius * (1 - progress)).toFixed(2))
			if (progress < 1) {
				return true
			}

			// At the top the bubble has shrunk to nothing: it bursts, or it is simply gone.
			if (Math.random() < setting(this.popChance, .5, 0, 1)) {
				this.burst(particle)
				return true
			}

			particle.group.remove()
			return false
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

/**
 * The briefing hero's four-pointed star, centred on the origin and reaching `radius` at each point: straight arms that
 * taper to slightly rounded points, with rounded valleys between them. Its proportions are the hero's outline, measured
 * against its points' radius.
 */
function star(radius: number) {
	const tip = (.0396 * radius).toFixed(2)
	const valley = (.3086 * radius).toFixed(2)
	// The right-hand arm's quarter of the outline, from where its upper edge leaves the valley above it: out to where its
	// point rounds, round it, back along its lower edge, and round the valley below it to where the next arm's edge begins.
	// The other arms are the same quarter turned.
	const quarter = [[.9767, -.0361], [.9767, .0361], [.4349, .281], [.281, .4349]] as const
	let path = `M${(.4349 * radius).toFixed(2)},${(-.281 * radius).toFixed(2)}`
	for (let turns = 0; turns < 4; turns++) {
		const [pointStart, pointEnd, valleyStart, next] = quarter.map(([x, y]) => turned(x * radius, y * radius, turns))
		path += `L${pointStart}A${tip},${tip},0,0,1,${pointEnd}L${valleyStart}A${valley},${valley},0,0,0,${next}`
	}

	return `${path}Z`
}

/** A point turned clockwise about the origin by whole quarter turns, as path coordinates. */
function turned(x: number, y: number, turns: number) {
	for (let turn = 0; turn < turns; turn++) {
		[x, y] = [-y, x]
	}

	return `${x.toFixed(2)},${y.toFixed(2)}`
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-rising-stars': RisingStars
	}
}
