import { component, css, property, query, svg } from '@a11d/lit'
import { ParticleStageBase, random, setting, svgElement, translate } from './ParticleStageBase'

/** How long a crackling ember flares before it throws its sparks, in milliseconds. */
const flareDuration = 90
/** The most embers in flight at once; a spawn beyond it is dropped. */
const maximumEmbers = 400

/** How an ember ends its rise: it crackles (a flare, then sparks), or it quietly cools to ash. */
type EmberPhase = 'rising' | 'crackling' | 'ash'

interface Ember {
	readonly element: SVGPathElement
	phase: EmberPhase
	/** Milliseconds spent in the current phase. */
	elapsed: number
	/** Milliseconds since the ember was spawned. */
	age: number
	readonly startX: number
	readonly startY: number
	readonly endY: number
	x: number
	y: number
	/** How long the rise takes, in milliseconds. */
	readonly duration: number
	/** How hot the ember starts out: 1 is the hot colour, 0.6 the ember colour, 0.3 deep red, 0 ash. */
	readonly startTemperature: number
	temperature: number
	/** The temperature the current phase cools down from. */
	fromTemperature: number
	scale: number
	/** The scale the current phase starts from. */
	fromScale: number
	/** In degrees. */
	rotation: number
	/** In degrees per second. */
	spin: number
	/** How far the wind has carried the ember sideways, in px. */
	drift: number
	/** How strongly the wind takes this ember, as a factor. */
	readonly windFactor: number
	/** How far the ember sways from its path, in px, before the turbulence setting scales it. */
	readonly sway: number
	/** The two sway waves: frequencies in radians per second, and phases. */
	readonly swayWaves: readonly [number, number, number, number]
	/** The two flicker waves' frequencies in radians per second, and the first one's phase. */
	readonly flickerWaves: readonly [number, number, number]
	/** How long the ash takes to float off and fade, in milliseconds. */
	readonly ashDuration: number
	/** Whether the ember has cooled enough to leave the glowing layer for the ash layer. */
	inAsh: boolean
}

interface Spark {
	readonly element: SVGLineElement
	x: number
	y: number
	/** Velocity in px per second. */
	vx: number
	vy: number
	elapsed: number
	/** In milliseconds. */
	readonly life: number
}

/**
 * Rising embers: glowing flakes rise from the bottom edge, tumbling, swaying in a gusting wind and cooling as they
 * climb, over a warm, pulsing light along the bottom. At the top of its rise an ember either crackles — a brief flare,
 * then a spray of sparks that arc and fall — or quietly cools to ash that floats off and fades. Ported from the
 * "Rising Embers" design artefact; the attributes are its settings, under the same names.
 *
 * An ember cools along a ramp from hot to ash, drawn from the theme rather than colours of its own: hot is the warning
 * colour (`--text-warning`), the ember is the flare accent (`--p7t-flare-accent`, falling back to the interactive
 * accent), a deep red is derived from the ember, and ash is the secondary background (`--background-secondary`) — so
 * a cooled flake sinks into the surface it rises over. The stage, its clipping and its sleeping are
 * {@link ParticleStageBase}'s.
 */
@component('p7t-rising-embers')
export class RisingEmbers extends ParticleStageBase {
	/** Embers per second for every 100 px of spawn width. */
	@property({ type: Number }) density = 1.5
	/** The embers' average flake length in px. Each one varies from 60% to 140% of it. */
	@property({ type: Number, attribute: 'ember-size' }) emberSize = 5
	/** The width of the spawn area, in % of the element's width, measured from the inline-start edge. */
	@property({ type: Number, attribute: 'max-width' }) maxWidth = 100
	/** The highest an ember rises from the bottom, in % of the element's height. Each gives out between 40% and 100% of it. */
	@property({ type: Number, attribute: 'max-height' }) maxHeight = 85
	/** How much the embers wander sideways, as a factor; 0 rises straight up. */
	@property({ type: Number }) turbulence = 1
	/** A steady, gusting drift, from -1 to 1. Positive blows toward the inline-end edge. */
	@property({ type: Number }) wind = .2
	/** The share of embers, from 0 to 1, that crackle into sparks. The rest quietly cool to ash. */
	@property({ type: Number, attribute: 'spark-chance' }) sparkChance = .15
	/** The halo around hot embers, as a factor; 0 turns it off. Ash never glows. */
	@property({ type: Number }) glow = 1
	/** The strength of the warm light along the bottom of the spawn area, from 0 to 1. */
	@property({ type: Number, attribute: 'base-glow' }) baseGlow = .35

	static override get styles() {
		return css`
			${super.styles}

			/* The ramp's deep red, darkened and reddened from the ember colour. */
			:host {
				--deep-red: rgb(from var(--p7t-flare-accent, var(--interactive-accent)) calc(r * .75) calc(g * .3) calc(b * .25));
			}

			/* Each ember and spark carries its temperature as --temperature, and its colour is mixed from it: the warning
			   colour at 1, the ember colour at 0.6, deep red at 0.3, ash at 0. Each color-mix takes over one stretch, so the
			   colour moves linearly between neighbouring stops. */
			.ember, .spark {
				--cooling-colour: color-mix(in srgb,
					var(--text-warning) calc(clamp(0, (var(--temperature, 0) - .6) / .4, 1) * 100%),
					color-mix(in srgb,
						var(--p7t-flare-accent, var(--interactive-accent)) calc(clamp(0, (var(--temperature, 0) - .3) / .3, 1) * 100%),
						color-mix(in srgb,
							var(--deep-red) calc(clamp(0, var(--temperature, 0) / .3, 1) * 100%),
							var(--background-secondary))));
			}

			.ember {
				fill: var(--cooling-colour);
			}

			.spark {
				fill: none;
				stroke: var(--cooling-colour);
				stroke-linecap: round;
			}

			#fire stop {
				stop-color: var(--p7t-flare-accent, var(--interactive-accent));
			}
		`
	}

	@query('.fire') private readonly fire?: SVGEllipseElement
	@query('.ash') private readonly ashLayer?: SVGGElement
	@query('.glow') private readonly glowLayer?: SVGGElement
	@query('.halo') private readonly halo?: SVGFEGaussianBlurElement

	private embers = new Array<Ember>()
	private sparks = new Array<Spark>()
	/** The halo's blur as last applied, so it is only written when it changes. */
	private haloDeviation = -1

	protected override get layers() {
		return svg`
			<defs>
				<filter id='glow' x='-20%' y='-20%' width='140%' height='140%' color-interpolation-filters='sRGB'>
					<feGaussianBlur class='halo' in='SourceGraphic' stdDeviation='2.6' result='halo' />
					<feMerge>
						<feMergeNode in='halo' />
						<feMergeNode in='halo' />
						<feMergeNode in='SourceGraphic' />
					</feMerge>
				</filter>
				<radialGradient id='fire' cx='.5' cy='.5' r='.5'>
					<stop offset='0' stop-opacity='.9' />
					<stop offset='.45' stop-opacity='.35' />
					<stop offset='1' stop-opacity='0' />
				</radialGradient>
			</defs>
			<ellipse class='fire' fill='url(#fire)' opacity='0' />
			<g class='ash'></g>
			<g class='glow' filter='url(#glow)'></g>
		`
	}

	protected override advance(delta: number) {
		this.applyHalo()
		this.advanceFire()

		const spawnWidth = this.width * setting(this.maxWidth, 100, 0, 100) / 100
		const count = this.spawns(setting(this.density, 1.5, 0, 50) * spawnWidth / 100, delta)
		for (let index = 0; index < count; index++) {
			this.spawn(spawnWidth)
		}

		this.embers = this.embers.filter(ember => this.advanceEmber(ember, delta))
		this.sparks = this.sparks.filter(spark => this.advanceSpark(spark, delta))
	}

	protected override clear() {
		for (const { element } of [...this.embers, ...this.sparks]) {
			element.remove()
		}

		this.embers = []
		this.sparks = []
	}

	private applyHalo() {
		const deviation = +(setting(this.glow, 1, 0, 5) * 2.6 * this.scale).toFixed(2)
		if (deviation === this.haloDeviation) {
			return
		}

		this.haloDeviation = deviation
		if (deviation > 0) {
			this.halo?.setAttribute('stdDeviation', String(deviation))
			this.glowLayer?.setAttribute('filter', 'url(#glow)')
		} else {
			this.glowLayer?.removeAttribute('filter')
		}
	}

	/** The warm light along the bottom of the spawn area, pulsing gently. */
	private advanceFire() {
		if (!this.fire) {
			return
		}

		const spawnWidth = this.width * setting(this.maxWidth, 100, 0, 100) / 100
		const width = Math.max(1, spawnWidth)
		this.fire.setAttribute('cx', (this.rtl ? this.width - width / 2 : width / 2).toFixed(1))
		this.fire.setAttribute('cy', this.height.toFixed(1))
		this.fire.setAttribute('rx', (width * .6).toFixed(1))
		this.fire.setAttribute('ry', Math.min(this.height * .28, 260 * this.scale).toFixed(1))
		const pulse = this.reducedMotion ? 1 : .8 + .12 * Math.sin(this.clock * 2.3) + .08 * Math.sin(this.clock * 5.9 + 1)
		this.fire.setAttribute('opacity', (spawnWidth > 0 ? setting(this.baseGlow, .35, 0, 1) * pulse : 0).toFixed(3))
	}

	private spawn(spawnWidth: number) {
		const riseHeight = this.height * setting(this.maxHeight, 85, 0, 100) / 100
		if (!this.glowLayer || this.embers.length >= maximumEmbers || riseHeight <= 0 || spawnWidth <= 0) {
			return
		}

		const size = setting(this.emberSize, 5, 1, 80) * random(.6, 1.4)
		const offset = random(0, Math.max(size, spawnWidth))
		const x = this.rtl ? this.width - offset : offset
		const startY = this.height + size
		const endY = this.height - riseHeight * random(.4, 1)
		const speed = random(70, 160) * this.scale
		this.embers.push({
			element: svgElement('path', { class: 'ember', d: flake(size), opacity: 0 }, this.glowLayer),
			phase: 'rising', elapsed: 0, age: 0,
			startX: x, startY, endY, x, y: startY,
			duration: Math.max(1200, (startY - endY) / speed * 1400),
			startTemperature: random(.85, 1), temperature: 1, fromTemperature: 0,
			scale: 1, fromScale: 1,
			rotation: random(0, 360), spin: random(-320, 320),
			drift: 0, windFactor: random(.6, 1.4),
			sway: random(6, 26) * this.scale,
			swayWaves: [random(.6, 1.4), random(1.7, 3.1), random(0, 6.3), random(0, 6.3)],
			flickerWaves: [random(14, 26), random(5, 11), random(0, 6.3)],
			ashDuration: random(1100, 1900),
			inAsh: false,
		})
	}

	/** Moves an ember on by a frame; false once it is spent and removed. */
	private advanceEmber(ember: Ember, delta: number) {
		ember.elapsed += delta
		ember.age += delta
		const age = ember.age / 1000
		const seconds = delta / 1000
		ember.rotation += ember.spin * seconds

		const [flickerA, flickerB, flickerPhase] = ember.flickerWaves
		const flicker = this.reducedMotion ? .85 : .78 + .22 * Math.sin(flickerA * age + flickerPhase) * Math.sin(flickerB * age)
		const progress = ember.phase === 'rising' ? Math.min(1, ember.elapsed / ember.duration) : 1
		// Sideways: the ember's own sway, plus the shared, gusting wind.
		ember.drift += this.windDrift(setting(this.wind, .2, -1, 1), ember.windFactor, progress, delta)
		const [swayA, swayB, swayPhaseA, swayPhaseB] = ember.swayWaves
		const sway = setting(this.turbulence, 1, 0, 5) * ember.sway * (.6 * Math.sin(swayA * age + swayPhaseA) + .4 * Math.sin(swayB * age + swayPhaseB))

		let opacity: number
		if (ember.phase === 'rising') {
			// Rising and cooling; the sway grows in over the first stretch of the climb.
			const eased = 1 - Math.pow(1 - progress, 1.8)
			ember.y = ember.startY + (ember.endY - ember.startY) * eased
			ember.x = ember.startX + ember.drift + sway * Math.min(1, progress * 2.5)
			ember.temperature = ember.startTemperature * (1 - .5 * progress) + (flicker - .78) * .5
			ember.scale = 1 - .3 * progress
			opacity = Math.min(1, ember.elapsed / 150) * (.55 + .45 * flicker)
			if (progress >= 1) {
				ember.fromTemperature = ember.startTemperature * .5
				ember.fromScale = ember.scale
				ember.phase = Math.random() < setting(this.sparkChance, .15, 0, 1) ? 'crackling' : 'ash'
				ember.elapsed = 0
			}
		} else if (ember.phase === 'crackling') {
			// A brief flare back to full heat, then the sparks, and on to ash.
			const flare = Math.min(1, ember.elapsed / flareDuration)
			ember.x = ember.startX + ember.drift + sway
			ember.temperature = 1
			ember.scale = ember.fromScale * (1 + .7 * Math.sin(flare * Math.PI))
			opacity = 1
			if (flare >= 1) {
				this.emitSparks(ember)
				ember.phase = 'ash'
				ember.elapsed = 0
				ember.fromTemperature = .35
				ember.fromScale *= .7
				ember.spin *= 1.6
			}
		} else {
			// Ash: goes dark, floats up a little and sinks, slows its spin, and fades.
			const fade = Math.min(1, ember.elapsed / ember.ashDuration)
			const cooling = Math.min(1, ember.elapsed / (ember.ashDuration * .4))
			ember.x = ember.startX + ember.drift + sway
			ember.y += (-14 + 30 * fade) * this.scale * seconds
			ember.temperature = ember.fromTemperature * Math.pow(1 - cooling, 1.4)
			if (ember.temperature > .1) {
				ember.temperature += (flicker - .78) * .3
			}

			ember.scale = ember.fromScale * (1 - .15 * fade)
			ember.spin *= Math.pow(.4, seconds)
			opacity = fade < .35 ? 1 : 1 - (fade - .35) / .65
			if (!ember.inAsh && ember.temperature < .12) {
				this.ashLayer?.append(ember.element)
				ember.inAsh = true
			}

			if (fade >= 1) {
				ember.element.remove()
				return false
			}
		}

		ember.element.setAttribute('transform', `${translate(ember.x, ember.y)} rotate(${ember.rotation.toFixed(1)}) scale(${Math.max(.01, ember.scale).toFixed(3)})`)
		ember.element.style.setProperty('--temperature', clamp(ember.temperature).toFixed(3))
		ember.element.setAttribute('opacity', clamp(opacity).toFixed(3))
		return true
	}

	/** Three to five sparks, thrown upward in a fan from where the ember crackled. */
	private emitSparks(ember: Ember) {
		if (!this.glowLayer) {
			return
		}

		const count = 3 + Math.floor(Math.random() * 3)
		for (let index = 0; index < count; index++) {
			const angle = -Math.PI / 2 + random(-1.2, 1.2)
			const speed = random(140, 340) * this.scale
			this.sparks.push({
				element: svgElement('line', { class: 'spark' }, this.glowLayer),
				x: ember.x, y: ember.y, vx: Math.cos(angle) * speed, vy: Math.sin(angle) * speed,
				elapsed: 0, life: random(280, 520),
			})
		}
	}

	/** Moves a spark on by a frame — gravity pulls it down, drag slows it sideways — and cools it; false once spent. */
	private advanceSpark(spark: Spark, delta: number) {
		const seconds = delta / 1000
		spark.elapsed += delta
		spark.vy += 520 * this.scale * seconds
		spark.vx *= Math.pow(.4, seconds)
		spark.x += spark.vx * seconds
		spark.y += spark.vy * seconds
		const progress = Math.min(1, spark.elapsed / spark.life)
		// A short streak trailing behind the spark, along its velocity.
		spark.element.setAttribute('x1', spark.x.toFixed(2))
		spark.element.setAttribute('y1', spark.y.toFixed(2))
		spark.element.setAttribute('x2', (spark.x - spark.vx * .035).toFixed(2))
		spark.element.setAttribute('y2', (spark.y - spark.vy * .035).toFixed(2))
		spark.element.style.setProperty('--temperature', (1 - .65 * progress).toFixed(3))
		spark.element.setAttribute('stroke-width', (1.4 * this.scale * (1 - .5 * progress)).toFixed(2))
		spark.element.setAttribute('opacity', (1 - progress * progress).toFixed(3))
		if (progress >= 1) {
			spark.element.remove()
			return false
		}

		return true
	}
}

function clamp(value: number) {
	return Math.min(1, Math.max(0, value))
}

/** An irregular, slightly elongated five-sided flake, centred on the origin, about `length` across. */
function flake(length: number) {
	const points = new Array<string>()
	for (let index = 0; index < 5; index++) {
		const angle = index / 5 * Math.PI * 2 + random(-.35, .35)
		const distance = random(.7, 1.05) * length / 2
		points.push(`${(Math.cos(angle) * distance).toFixed(2)},${(Math.sin(angle) * distance * .55).toFixed(2)}`)
	}

	return `M${points.join('L')}Z`
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-rising-embers': RisingEmbers
	}
}
