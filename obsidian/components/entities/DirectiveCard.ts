import { Component, component, css, html, nothing, property } from '@a11d/lit'
import { Directive, DirectiveStatus, LunarDirectiveStatus } from '@pleiades/sdk'
import { IconName } from 'components/PleiadesIcon'
import '../media/MediaView'
import '../system/DatetimeView'
import './StatusItem'
import type { Status } from './StatusItem'

/**
 * The directive mini-card drawn in {@link DirectiveItem}'s tooltip — the crest, the title, the due date, and the
 * workflow state, the gist of the directive's own banner. A self-contained element (its own shadow root and styles)
 * so it renders identically wherever the tooltip system places it, independent of any host's shadow scope.
 */
@component('p7t-directive-card')
export class DirectiveCard extends Component {
	@property({ type: Object }) directive?: Directive

	static override get styles() {
		return css`
			:host { display: contents; }

			.mini-banner {
				display: flex;
				align-items: flex-start;
				gap: .6em;
				min-width: 12em;

				& .crest {
					width: 2.6em;
					height: 2.6em;
					flex: 0 0 auto;
				}

				& .meta {
					display: flex;
					flex-direction: column;
					gap: 0;
				}

				& .title {
					font-weight: 500;
					font-size: 1.2em;
				}

				& .codename {
					opacity: .6;
					font-size: .85em;
				}

				& .due {
					opacity: .6;
					font-size: .85em;
				}

				& p7t-status-item {
					font-size: .8em;
				}
			}
		`
	}

	private get kindIcon(): IconName {
		return this.directive?.isLunar ? 'directive-lunar' : 'directive'
	}

	private get statusName(): Status {
		const directive = this.directive!
		const name = directive.isLunar ? LunarDirectiveStatus[directive.status] : DirectiveStatus[directive.status]
		return (name ?? 'Planned') as Status
	}

	protected override get template() {
		const directive = this.directive
		if (!directive) {
			return nothing
		}

		return html`
			<div class='mini-banner'>
				<p7t-media
					class='crest'
					icon
					.media=${directive.iconMedia}
					.default=${this.kindIcon}>
				</p7t-media>
				<div class='meta'>
					<div class='title'>${directive.title}</div>
					${!directive.due ? nothing : html`<div class='due'>Due <p7t-datetime-view relative .date=${directive.due}></p7t-datetime-view></div>`}
					<p7t-status-item small .status=${this.statusName}></p7t-status-item>
				</div>
			</div>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-directive-card': DirectiveCard
	}
}
