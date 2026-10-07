import { component, css, html, nothing, property } from '@a11d/lit'
import { EntityWatch, IconName, navigateToEntity } from '..'
import { CardComponent } from '../design'
import { 'everglow-banner' as EverglowBanner } from '../../assets/design'

@component('p7t-briefing-card')
export class BriefingCard<T> extends CardComponent {
	@property({ type: Object }) data?: T

	/**
	 * Observes the canonical entity behind `data`, so a card re-renders when that instance is edited from
	 * another surface. Absorption mutates the instance in place, keeping the same reference, which Lit's
	 * property check never sees — the subscription is what turns such an edit into a re-render. A no-op when
	 * `data` is not a tracked entity (an agenda, say), so every card shares it from the base.
	 */
	protected readonly watch = new EntityWatch(this, () => this.data)

	protected readonly icon?: IconName

	/**
	 * What plays at the back of the card, blurred, in its flare accent: rising stars (`p7t-rising-stars`) or rising
	 * embers (`p7t-rising-embers`). Nothing by default.
	 */
	protected readonly backdrop?: 'rising-stars' | 'rising-embers'

	static override get styles() {
		return css`
			${super.styles}

			:host {
				& > * {
					position: relative;
					z-index: 1;
				}
			}

			:host::part(header) {
				position: relative;
				z-index: 1;
			}

			/* The backdrop at the back of the card, over the crest but behind the content, softened into glows — no more
			   than keeps a popped star's four points readable. */
			.backdrop {
				position: absolute;
				z-index: 0;
				inset: 0;
				border-radius: inherit;
			}

			/* Embers are small flakes with a halo of their own, which the stars' blur would wash out. */
			p7t-rising-embers.backdrop::part(stage) {
				filter: blur(1.5px);
			}

			p7t-rising-stars::part(stage) {
				filter: blur(3.5px);
				opacity: .25;
			}

			.mask {
				position: absolute;
				z-index: 0;
				inset-inline: 1%;
				top: 0;
				width: 98%;
				height: min(100% + 1.6em, 10em + 20%);
				color: var(--background-primary);
				overflow: clip;
				pointer-events: none;

				&::part(icon-frame) {
					margin-top: -2.5em;
					mask-position: right;
				}

				:host([collapsed]) & {
					color: color-mix(in srgb, var(--p7t-flare-accent) 30%, transparent);

					&::part(icon-frame) {
						margin-top: -1em;
					}
				}
			}

			:host::part(footer) {
				position: relative;
				z-index: 1;
				display: grid;
				grid-template-columns: 1fr auto;
				gap: 1em;
				align-items: end;
			}

			:host::part(content) {
				position: relative;
				z-index: 2;
				inset-inline: -1em;
				width: calc(100% + 1em);
				overflow-y: auto;
				padding-inline: .5em;
				flex-shrink: 1;
				flex-basis: 0;
				scrollbar-width: thin;
				scrollbar-color: color-mix(in srgb, var(--text-normal) 20%, transparent) transparent;
			}

			.list {
				display: flex;
				flex-direction: column;
				align-items: stretch;
			}

			.start-button {
				font-size: 1.05em;
			}

			.no-data {
				font-weight: 400;
				opacity: .75;
				font-size: 1.4em;
			}

			.pre-heading-link {
				display: inline-flex;
				align-items: center;
				gap: .8ch;

				& p7t-icon {
					cursor: pointer;
					transition: .3s ease;
					color: var(--p7t-flare-accent, var(--interactive-accent));

					&:not(:hover) {
						opacity: .7;
					}

					:host(:not(:hover)) & {
						opacity: 0;
						transform: translateX(-1ch);
					}
				}
			}
		`
	}

	protected get offlineTemplate() {
		return html`
			<span class='no-data'>No Data</span>
		`
	}

	protected override get template() {
		return this.data ? html`
			${!this.icon ? nothing : html`<p7t-icon icon='${this.icon}' class='mask'></p7t-icon>`}
			${this.backdropTemplate}
			${super.template}
		` : html`
			<style>
				:host {
					background:
						color-mix(in srgb, var(--p7t-flare-accent, var(--interactive-accent)) 4%, transparent)
						url('${EverglowBanner}') center/cover
						!important;
					display: flex !important;
					flex-direction: column !important;
					align-items: center !important;
					justify-content: center !important;
				}
			</style>
			${this.offlineTemplate}
		`
	}

	private get backdropTemplate() {
		switch (this.backdrop) {
			case 'rising-stars':
				return html`
					<p7t-rising-stars class='backdrop'
						bubble-size='20' wind='0' max-width='70' max-height='50' density='.4' pop-chance='.2'>
					</p7t-rising-stars>
				`
			case 'rising-embers':
				return html`
					<p7t-rising-embers class='backdrop'
						wind='2' turbulence='.8' density='1.2' base-glow='0' max-width='70' max-height='50' ember-size='6' spark-chance='.08'>
					</p7t-rising-embers>`
			default:
				return nothing
		}
	}

	protected override get preHeadingTemplate() {
		return html`<span class='pre-heading-link'>
			${this.preHeading}
			<p7t-icon @click=${() => navigateToEntity(this.data!.id)} icon='lucide:file-symlink'></p7t-icon>
		</span>`
	}

	protected override get content() {
		return html`
			<div class='list'>
				${this.listContent}
			</div>
		`
	}

	protected override get footer() {
		return html`
		`
	}

	protected get listContent() { return html`` }

	protected navigateToEntityFile() {

	}
}