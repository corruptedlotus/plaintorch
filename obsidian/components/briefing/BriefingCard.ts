import { component, css, html, nothing, property, unsafeCSS } from '@a11d/lit'
import { IconName } from 'components'
import { CardComponent } from 'components/design'
import { 'everglow-banner' as EverglowBanner } from 'assets/design'
import { 'grunge-1-png' as GrungeNoise } from 'assets/design'

@component('p7t-briefing-card')
export class BriefingCard<T> extends CardComponent {
	@property({ type: Object }) data?: T
	protected readonly icon?: IconName

	static override get styles() {
		return css`
			${super.styles}

			:host {
				background:
					/*url('${unsafeCSS(GrungeNoise)}'),*/
					radial-gradient(
						color-mix(in srgb, black 90%, transparent) 38%,
						color-mix(in srgb, var(--p7t-flare-accent) 40%, black) 70%,
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
				height: calc(10em + 20%);
				color: var(--background-primary);
				overflow: clip;

				&::part(icon-frame) {
					margin-top: -2.5em;
					mask-position: right;
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
				font-size: 1em;
			}

			.no-data {
				font-weight: 400;
				opacity: .75;
				font-size: 1.4em;
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
					border-style: dashed !important;
					display: flex !important;
					flex-direction: column !important;
					align-items: center !important;
					justify-content: center !important;
				}
			</style>
			${this.offlineTemplate}
		`
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
}