import { component, css, html, nothing, property } from '@a11d/lit'
import { Directive, DirectiveStatus, LunarDirectiveStatus } from '@pleiades/sdk'
import { InfoItem } from '../design/InfoItem'
import type { Status } from './StatusItem'

/**
 * The directive an entity belongs to (PEP100) — its kind glyph beside its title — drawn one unified way in the
 * items, banners, and occurrence rows that used to hand-format it. A lunar and a stellar directive read apart by
 * their glyph. With no directive it shows a placeholder (an objective with none is a "World Quest").
 *
 * The built-in tooltip is a mini-banner: the crest, the title, the codename, and the workflow state — the gist of
 * the directive's own banner, without leaving the surface the chip sits on.
 */
@component('p7t-directive-item')
export class DirectiveItem extends InfoItem {
	@property({ type: Object }) directive?: Directive
	/** Shown when there is no directive — an objective with none is a world quest. */
	@property() placeholder = 'World Quest'

	static override get styles() {
		return css`
			${super.styles}

			.ref {
				display: inline-flex;
				align-items: center;
				gap: 4px;
				opacity: .6;
				font-weight: 400;
				font-size: .9em;
				line-height: .9;
			}

			.placeholder {
				opacity: .4;
				font-weight: 400;
				font-size: .9em;
				line-height: .9;
			}

			.ref p7t-icon {
				width: 20px;
				height: 20px;
			}

			.mini-banner {
				display: flex;
				align-items: center;
				gap: .6em;
				min-width: 12em;
			}

			.mini-banner .crest {
				width: 2.4em;
				height: 2.4em;
				flex: 0 0 auto;
			}

			.mini-banner .meta {
				display: flex;
				flex-direction: column;
				gap: .2em;
			}

			.mini-banner .title {
				font-weight: 600;
			}

			.mini-banner .codename {
				opacity: .6;
				font-size: .85em;
			}

			.mini-banner p7t-status-item {
				font-size: .8em;
			}
		`
	}

	private get kindIcon() {
		return this.directive?.isLunar ? 'directive-lunar' : 'directive'
	}

	private get statusName(): Status {
		const directive = this.directive!
		const name = directive.isLunar ? LunarDirectiveStatus[directive.status] : DirectiveStatus[directive.status]
		return (name ?? 'Planned') as Status
	}

	protected override get content() {
		const directive = this.directive
		if (!directive) {
			return html`<span class='placeholder'>${this.placeholder}</span>`
		}

		return html`
			<span class='ref'>
				<p7t-icon icon=${this.kindIcon}></p7t-icon>
				<span>${directive.title}</span>
			</span>
		`
	}

	protected override get tooltip() {
		const directive = this.directive
		if (!directive) {
			return nothing
		}

		return html`
			<div class='mini-banner'>
				<p7t-icon class='crest' icon=${this.kindIcon}></p7t-icon>
				<div class='meta'>
					<div class='title'>${directive.title}</div>
					${!directive.codename ? nothing : html`<div class='codename'>${directive.codename}</div>`}
					<p7t-status-item .status=${this.statusName}></p7t-status-item>
				</div>
			</div>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-directive-item': DirectiveItem
	}
}
