import { component, css, html, nothing, property, unsafeCSS } from '@a11d/lit'
import { EntityWatch, IconName, navigateToEntity } from '..'
import { CardComponent } from '../design'
import { 'everglow-banner' as EverglowBanner } from '../../assets/design'
import { 'grunge-1-png' as GrungeNoise } from '../../assets/design'

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

	static override get styles() {
		return css`
			${super.styles}

			:host {
				background:
					/*url('${unsafeCSS(GrungeNoise)}'),*/
					radial-gradient(
						var(--background-primary-alt) 38%,
						color-mix(in srgb, var(--p7t-flare-accent) 20%, black) 70%,
						transparent 94%);
				background-size: /*contain,*/ 95vw 95vw;
				background-blend-mode: soft-light;
				background-position: /*bottom,*/ 40% 84%;

				& > * {
					position: relative;
					z-index: 1;
				}
			}

			:host::part(header) {
				position: relative;
				z-index: 1;
			}

			.mask {
				position: absolute;
				z-index: 0;
				inset-inline: 1%;
				top: 0;
				width: 98%;
				height: min(100% + 2em, 10em + 20%);
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