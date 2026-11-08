import { Component, component, css, html, property, svg } from '@a11d/lit'
import { 'moon-nearside-webp' as MoonTexture } from '../../assets/design'

/** The lit share of the moon at each snapshot either side of the full moon: a thin crescent, a crescent, a half moon, a gibbous moon. */
const phases = [.1, .28, .5, .76]

/**
 * The cycle's snapshots, edge to edge: the waxing phases lead in from the left edge to the full moon at the centre (step
 * 4, the moon that stays), and the waning phases, lit from the other side, lead out from it to the right edge. A step
 * is both where a snapshot stands and when it shows.
 */
const snapshots = [
	...phases.map((_, index) => ({ step: index, mask: `waxing-${index}` })),
	...phases.map((_, index) => ({ step: 8 - index, mask: `waning-${index}` })).reverse(),
]

/**
 * One blade of the swirling ring the full moon radiates, the outer ring of the briefing cards' empty state
 * (`everglow-banner.svg`): measured from the ring's centre, in units of its inner radius. Thirty of them, each turned a
 * further 12°, make the ring.
 */
const blade = 'M.8195,-.7415L.7774,-.7255A.0138,.0138,0,0,0,.7704,-.7196L.7413,-.6712A.0029,.0029,0,0,0,.745,-.6671L.7962,-.691A.0138,.0138,0,0,0,.8027,-.6973L.8231,-.7375A.0029,.0029,0,0,0,.8195,-.7415Z'

/**
 * The three emblems a completed cycle leaves on the moon, in turn, from the Project Everglow artwork: each drawn on a
 * square around its circle (`centre`, the circle's radius too), with only the shapes that show.
 */
const emblems = [
	{
		name: 'aurora',
		centre: 170,
		shapes: svg`
			<path d='M106.05,97.87c43.29-35.26,107.2-28.73,142.47,14.57,29.6,36.35,29.74,87.23,3.4,123.49,33.01-38.73,34.73-96.68,1.45-137.55-37.5-46.04-105.22-52.97-151.26-15.47-46.04,37.5-52.97,105.22-15.47,151.26.45.55.91,1.09,1.37,1.64-31.28-43.12-23.76-103.88,18.05-137.93Z' />
			<path d='M229.64,129.23c-9.91-10.49-22.14-17.45-35.13-20.87,9.55,3.61,18.45,9.38,25.92,17.29,26.4,27.96,25.14,72.18-2.82,98.58s-72.18,25.14-98.58-2.82c-9.36-9.91-15.23-21.87-17.67-34.35,1.44,16.07,8.07,31.79,20.01,44.42,28.24,29.9,75.37,31.25,105.27,3.01,29.9-28.24,31.25-75.37,3.01-105.27Z' />
			<circle cx='170' cy='29.58' r='8.32' />
			<polygon points='162.19 297.02 170 275.51 177.81 297.02 170 318.53 162.19 297.02' />
			<polygon points='102.56 272.91 115.88 264.08 114.89 280.03 101.57 288.85 102.56 272.91' />
			<polygon points='237.44 272.91 224.12 264.08 225.11 280.03 238.43 288.85 237.44 272.91' />
			<polygon points='170 131.32 178.46 161.87 209.02 170.34 178.46 178.8 170 209.35 161.54 178.8 130.98 170.34 161.54 161.87 170 131.32' />
		`,
	},
	{
		name: 'elune',
		centre: 170.34,
		shapes: svg`
			<path d='M253.6,105.48c-2.09-2.57-4.28-5-6.55-7.33.46.54.91,1.08,1.36,1.63,35.06,43.05,28.57,106.61-14.49,141.67s-106.61,28.57-141.67-14.49c-31.1-38.19-29.5-92.49,1.3-128.75-37.76,38.52-41.35,100.09-6.48,142.9,37.45,45.99,105.1,52.9,151.08,15.45,45.99-37.45,52.9-105.1,15.45-151.08Z' />
			<path d='M234.82,126.33c-9.84-35.36-46.49-56.05-81.85-46.21-34.56,9.62-55.09,44.83-46.82,79.44-1.44-27.09,16.03-52.57,43.22-60.13,31.44-8.75,64.13,9.71,72.88,41.14,6.58,23.64-2.23,47.99-20.37,62.33,27.04-14.28,41.48-45.9,32.94-76.56Z' />
			<circle cx='52.55' cy='102.67' r='5.14' />
			<circle cx='170.34' cy='306.67' r='5.14' />
			<circle cx='288.12' cy='102.67' r='5.14' />
			<path d='M158.59,79.38c4.94-9.94,10-28.27,11.75-42.06v42.06s-11.75,0-11.75,0Z' />
			<path d='M182.08,79.38c-4.94-9.94-10-28.27-11.75-42.06v42.06s11.75,0,11.75,0Z' />
			<path d='M193.9,156.96c1.63,3.15,2.55,6.72,2.55,10.51,0,12.66-10.27,22.93-22.93,22.93s-22.93-10.27-22.93-22.93,10.27-22.93,22.93-22.93c3.79,0,7.36.93,10.51,2.55-4.03-2.35-8.71-3.7-13.71-3.7-15.06,0-27.27,12.21-27.27,27.27s12.21,27.27,27.27,27.27,27.27-12.21,27.27-27.27c0-5-1.35-9.68-3.7-13.71Z' />
		`,
	},
	{
		name: 'xatash',
		centre: 170,
		shapes: svg`
			<path d='M219.33,82.34c-29.19-19.76-68.26-20.45-98.41-.14,31.32-15.12,67.74-14.7,98.41.14Z' />
			<path d='M259.23,124.19c19.25,40.24,9.31,89.99-26.77,119.37-42.29,34.44-104.71,28.06-139.15-14.23-25.01-30.71-28.49-72.04-12.32-105.56-20.05,35.54-17.55,81.21,9.78,114.77,35.64,43.76,100,50.34,143.76,14.7,39.07-31.82,48.5-86.55,24.7-129.06Z' />
			<path d='M147.78,215.5c-21.06-17.15-24.23-48.23-7.08-69.29s48.23-24.23,69.29-7.08c8.08,6.58,13.52,15.22,16.2,24.56-1.59-14.19-8.52-27.79-20.47-37.52-24.21-19.72-59.82-16.08-79.54,8.13-19.72,24.21-16.08,59.82,8.13,79.54,11.74,9.56,26.17,13.62,40.16,12.51-9.46-.84-18.78-4.41-26.68-10.85Z' />
			<path d='M93.91,106.7l11.53-1.26,1.26-11.53-23.61-10.82,10.82,23.61ZM158.49,70.56l11.51,12.53,11.51-12.53-11.51-41.98-11.51,41.98ZM70.56,158.49l-41.98,11.51,41.98,11.51,12.53-11.51-12.53-11.51ZM246.09,106.7l10.82-23.61-23.61,10.82,1.26,11.53,11.53,1.26ZM234.56,234.56l-1.26,11.53,23.61,10.82-10.82-23.61-11.53,1.26ZM269.44,158.49l-12.53,11.51,12.53,11.51,41.98-11.51-41.98-11.51ZM93.91,233.3l-10.82,23.61,23.61-10.82-1.26-11.53-11.53-1.26ZM158.49,269.44l11.51,41.98,11.51-41.98-11.51-12.53-11.51,12.53Z' />
		`,
	},
]

/**
 * The Moonlight page's banner: the moon's cycle across the banner, edge to edge, in quick snapshots. A thin crescent
 * shows by the left edge and fades; a fuller moon shows a little nearer the centre, and so on, until the full moon
 * arrives at the centre and stays. As it arrives it radiates one swirling ring of blades (the briefing cards' empty
 * state's outer ring), and the cycle runs on past it, faintly now, waning out to the right edge. From then on the whole
 * cycle repeats faintly either side of the full moon, each pass through the centre radiating another ring.
 *
 * Each pass through the centre leaves a shadow of an emblem on the moon, in the accent colour, which lingers until the
 * next: Aurora, then Elune, then Xatash, and round again.
 *
 * The moon is the LROC Wide Angle Camera mosaic of the near side (NASA/GSFC/Arizona State University, public domain),
 * lit from the right as it waxes and from the left as it wanes, with a soft terminator and the dark side faintly
 * showing. The show starts the first
 * time the banner comes into view, so a page opened later still opens on the moon's arrival; with reduced motion
 * preferred, the full moon simply stands there. Everything is CSS animation of opacity and transforms, which the browser
 * stops servicing while the banner is hidden (a dormant page).
 *
 * Purely decorative and hidden from assistive technology; it fills whatever box it is given, as a hero's banner
 * (`slot='banner'` in a `p7t-page-hero`).
 */
@component('p7t-moonlight-banner')
export class MoonlightBanner extends Component {
	/** Whether the show has begun: set the first time the banner comes into view, and never cleared. */
	@property({ type: Boolean, reflect: true }) playing = false

	private readonly visibilityObserver = new IntersectionObserver(([entry]) => {
		if (entry?.isIntersecting) {
			this.playing = true
			this.visibilityObserver.disconnect()
		}
	})

	static override get styles() {
		return css`
			:host {
				/*
				 * The moon's size and height in the banner; the distance between neighbouring snapshots, which puts the first
				 * and last near the edges; and the show's pace: a cycle, and the gap between snapshots.
				 */
				--p7t-moon-size: 8.6em;
				--p7t-moon-top: 42%;
				--spacing: 11%;
				--cycle: 3s;
				--interval: .2s;

				display: block;
				position: relative;
				overflow: clip;
				border-radius: inherit;
				pointer-events: none;
				contain: layout paint style;
				color: color-mix(in srgb, var(--p7t-flare-accent, var(--interactive-accent)) 35%, white);
			}

			.definitions {
				position: absolute;
				width: 0;
				height: 0;
			}

			.moon, .ring {
				position: absolute;
				left: var(--at, 50%);
				top: var(--p7t-moon-top);
				width: var(--p7t-moon-size);
				aspect-ratio: 1;
				translate: -50% -50%;

				& svg {
					display: block;
					width: 100%;
					height: 100%;
					overflow: visible;
				}
			}

			.moon {
				opacity: 0;
			}

			/* A grey, flatly lit mosaic: brightened into moonlight, the shading at the limb giving it its roundness. */
			.surface {
				filter: brightness(4) contrast(.7);
			}

			.earthshine {
				opacity: .07;
			}

			.halo {
				opacity: .55;
			}

			.phase .halo {
				opacity: .3;
			}

			.ring {
				opacity: 0;
				fill: currentColor;
			}

			.emblem {
				opacity: 0;
			}

			#disk-overlay {
				transform: scale(3.6);
				mix-blend-mode: difference;
				opacity: 0.7;
			}
			
			.emblem-shape {
				fill: color-mix(in srgb, var(--interactive-accent) 25%, var(--text-normal));
				fill-opacity: 1;
			}

			/* Once the full moon has arrived, the phases carry on faintly either side of it. */
			@media (prefers-reduced-motion: no-preference) {
				:host([playing]) {
					.phases {
						animation: dim .4s ease-out calc(4 * var(--interval)) forwards;
					}

					.phase {
						animation: snapshot var(--cycle) linear calc(var(--step) * var(--interval)) infinite backwards;
					}

					.main {
						animation: arrive .7s ease-out calc(4 * var(--interval)) both;
					}

					.main .halo {
						animation: flare var(--cycle) linear calc(4 * var(--interval)) infinite backwards;
					}

					.ring {
						animation: radiate var(--cycle) linear calc(4 * var(--interval) + .15s) infinite backwards;
					}

					.emblem {
						animation: emblem calc(3 * var(--cycle)) linear calc(4 * var(--interval) + .3s + var(--emblem) * var(--cycle)) infinite backwards;
					}
				}
			}

			/* Without motion, the full moon simply stands there, with the first emblem on it. */
			@media (prefers-reduced-motion: reduce) {
				.phase, .ring {
					display: none;
				}

				.main, .emblem.aurora {
					opacity: 1;
				}
			}

			@keyframes dim {
				to { opacity: .4; }
			}

			@keyframes snapshot {
				0% { opacity: 0; scale: .96; animation-timing-function: ease-out; }
				4% { opacity: 1; }
				8% { opacity: 1; animation-timing-function: ease-in; }
				13% { opacity: 0; scale: 1; }
				100% { opacity: 0; scale: 1; }
			}

			@keyframes arrive {
				from { opacity: 0; scale: .92; }
				to { opacity: 1; scale: 1; }
			}

			@keyframes flare {
				0% { opacity: .55; animation-timing-function: ease-out; }
				5% { opacity: 1; animation-timing-function: ease-in-out; }
				30% { opacity: .55; }
				100% { opacity: .55; }
			}

			@keyframes radiate {
				0% { opacity: 0; scale: .8; rotate: 0deg; animation-timing-function: cubic-bezier(.15, .6, .3, 1); }
				3% { opacity: .85; animation-timing-function: ease-in; }
				28% { opacity: 0; scale: 4.8; rotate: 26deg; }
				100% { opacity: 0; scale: 20; rotate: -1800deg; }
			}

			@keyframes emblem {
				0% { opacity: 0; animation-timing-function: ease-in-out; }
				4% { opacity: 1; }
				20% { opacity: 1; animation-timing-function: ease-in-out; }
				30% { opacity: 0; }
				100% { opacity: 0; }
			}
		`
	}

	protected override connected() {
		if (!this.playing) {
			this.visibilityObserver.observe(this)
		}
	}

	protected override disconnected() {
		this.visibilityObserver.disconnect()
	}

	protected override get template() {
		return html`
			${this.definitionsTemplate}
			<div class='phases'>
				${snapshots.map(({ step, mask }) => html`
					<div class='moon phase' style=${`--step: ${step}; --at: calc(50% + ${step - 4} * var(--spacing))`}>
						${this.moonTemplate(`url(#${mask})`)}
					</div>
				`)}
			</div>
			<div class='ring'>
				<svg viewBox='-1 -1 2 2' aria-hidden='true'>
					${Array.from({ length: 30 }, (_, index) => svg`<path d=${blade} transform='rotate(${index * 12})' />`)}
				</svg>
			</div>
			<div class='moon main'>
				${this.moonTemplate(undefined, svg`
					<g id='disk-overlay' clip-path='url(#disc)' filter='url(#shadow)'>
						${emblems.map(({ name, centre, shapes }, index) => svg`
							<g class='emblem ${name}' style='--emblem: ${index}' transform='scale(${1 / centre}) translate(${-centre},${-centre})'>
								<g class='emblem-shape'>${shapes}</g>
							</g>
						`)}
					</g>
				`)}
			</div>
		`
	}

	/**
	 * The shapes every moon refers to: the disc, the shading, the soft terminator, and each phase's lit part, waxing (lit
	 * from the right) and waning (the same, mirrored).
	 */
	private get definitionsTemplate() {
		return html`
			<svg class='definitions' aria-hidden='true'>
				<defs>
					<clipPath id='disc'>
						<circle r='1' />
					</clipPath>
					<radialGradient id='limb' cx='0' cy='0' r='1' gradientUnits='userSpaceOnUse'>
						<stop offset='.5' stop-color='black' stop-opacity='0' />
						<stop offset='1' stop-color='black' stop-opacity='.45' />
					</radialGradient>
					<radialGradient id='halo' cx='0' cy='0' r='2.4' gradientUnits='userSpaceOnUse'>
						<stop offset='.38' stop-color='white' stop-opacity='.32' />
						<stop offset='.6' stop-color='white' stop-opacity='.09' />
						<stop offset='1' stop-color='white' stop-opacity='0' />
					</radialGradient>
					<filter id='terminator' filterUnits='userSpaceOnUse' x='-1.5' y='-1.5' width='3' height='3'>
						<feGaussianBlur stdDeviation='.04' />
					</filter>
					<filter id='shadow' filterUnits='userSpaceOnUse' x='-1.5' y='-1.5' width='3' height='3'>
						<feGaussianBlur stdDeviation='.018' />
					</filter>
					${phases.map((lit, index) => svg`
						<mask id='waxing-${index}' maskUnits='userSpaceOnUse' x='-1.5' y='-1.5' width='3' height='3'>
							<path d=${litPath(lit)} fill='white' filter='url(#terminator)' />
						</mask>
						<mask id='waning-${index}' maskUnits='userSpaceOnUse' x='-1.5' y='-1.5' width='3' height='3'>
							<g transform='scale(-1 1)'>
								<path d=${litPath(lit)} fill='white' filter='url(#terminator)' />
							</g>
						</mask>
					`)}
				</defs>
			</svg>
		`
	}

	/**
	 * One moon of unit radius in a box its diameter wide: its halo, the dark side faintly, then the lit part (all of it
	 * without a `mask`), shaded at the limb, with whatever `overlay` lies on its face.
	 */
	private moonTemplate(mask?: string, overlay?: unknown) {
		return html`
			<svg viewBox='-1 -1 2 2' aria-hidden='true'>
				<circle class='halo' r='2.4' fill='url(#halo)' />
				${!mask ? '' : svg`<image class='earthshine' href=${MoonTexture} x='-1' y='-1' width='2' height='2' clip-path='url(#disc)' />`}
				<g mask=${mask ?? 'none'}>
					<image class='surface' href=${MoonTexture} x='-1' y='-1' width='2' height='2' clip-path='url(#disc)' />
					<circle r='1' fill='url(#limb)' />
				</g>
				${overlay ?? ''}
			</svg>
		`
	}
}

/**
 * The lit part of a waxing moon of unit radius, lit from the right: the right half of the disc, bounded on the left by
 * the terminator, a half ellipse whose width makes the lit share of the disc `lit` (from 0 to 1).
 */
function litPath(lit: number) {
	const terminator = Math.abs(1 - 2 * lit).toFixed(3)
	// Back up the terminator from the bottom: bulging right for a crescent, left for a gibbous moon.
	return `M0,-1A1,1,0,0,1,0,1A${terminator},1,0,0,${lit < .5 ? 0 : 1},0,-1Z`
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-moonlight-banner': MoonlightBanner
	}
}
