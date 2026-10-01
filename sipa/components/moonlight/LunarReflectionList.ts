import { Component, component, css, html, property, repeat } from '@a11d/lit'
import { ContextMenuController, entityContextMenu, openEntityEditor } from '..'
import type { LunarReflectionEntry } from './moonlightTree'

/**
 * The decrees taking part in Lunar Reflection, as a list whose columns line up down its length: each decree's title,
 * the directive that owns it, and its humanised orbit. A decree with no orbit reflects on the base cadence and reads
 * "Base", accented.
 *
 * A row opens the decree's full banner, where its reflection and orbit are edited; a right-click raises its context
 * menu. On a narrow pane the columns fold, each row taking its title on one line and the rest beneath.
 */
@component('p7t-lunar-reflection-list')
export class LunarReflectionList extends Component {
	@property({ type: Array }) entries: readonly LunarReflectionEntry[] = []

	/** One menu for the whole list, resolved to the row the right-click landed on. */
	protected readonly contextMenu = new ContextMenuController(this, event => {
		const decree = this.entryAt(event)?.decree
		return decree ? entityContextMenu(decree) : undefined
	})

	static override get styles() {
		return css`
			:host {
				display: block;
				container-type: inline-size;
				font-family: var(--font-interface);
			}

			.list {
				display: grid;
				grid-template-columns: [title] minmax(0, 1.3fr) [directive] minmax(0, 1fr) [orbit] minmax(0, 1.3fr);
				column-gap: 1.2em;
			}

			.header,
			.row {
				display: grid;
				grid-template-columns: subgrid;
				grid-column: 1 / -1;
				align-items: center;
				padding-inline: .8em;
			}

			.header {
				padding-block: 0 .45em;
				color: var(--text-faint, var(--text-muted));
				font-size: .72em;
				font-weight: 500;
				letter-spacing: .08em;
				text-transform: uppercase;
			}

			.row {
				min-height: 2.6em;
				padding-block: .35em;
				border-radius: 10px;
				cursor: pointer;
				outline: none;
				transition: background-color .2s ease;

				&:hover,
				&:focus-visible {
					background-color: color-mix(in srgb, var(--text-normal) 7%, transparent);
				}

				&:focus-visible {
					box-shadow: inset 0 0 0 1px color-mix(in srgb, var(--p7t-flare-accent, var(--interactive-accent)) 60%, transparent);
				}
			}

			.row + .row {
				box-shadow: 0 -1px 0 color-mix(in srgb, var(--text-normal) 6%, transparent);
			}

			.title {
				display: flex;
				align-items: center;
				gap: .55em;
				min-width: 0;
				font-size: 1.05em;
				font-weight: 350;

				& p7t-icon {
					flex: none;
					width: 1.3em;
					height: 1.3em;
					color: color-mix(in srgb, var(--p7t-flare-accent, var(--interactive-accent)) 60%, var(--text-normal));
				}

				& span {
					overflow: hidden;
					text-overflow: ellipsis;
					white-space: nowrap;
				}
			}

			.directive,
			.orbit {
				display: flex;
				align-items: center;
				min-width: 0;
				overflow: hidden;
				font-size: .92em;
			}

			.directive {
				color: var(--text-muted);
			}

			.base {
				color: var(--p7t-flare-accent, var(--interactive-accent));
				font-weight: 500;
				letter-spacing: .04em;
			}

			/* A narrow pane folds the columns: the title on its own line, the directive and orbit side by side beneath. */
			@container (width < 34em) {
				.header {
					display: none;
				}

				.row {
					display: flex;
					flex-wrap: wrap;
					gap: .2em 1em;
				}

				.title {
					flex-basis: 100%;
				}
			}
		`
	}

	protected override get template() {
		return html`
			<div class='list'>
				<div class='header' aria-hidden='true'>
					<span>Decree</span>
					<span>Directive</span>
					<span>Orbit</span>
				</div>
				${repeat(this.entries, entry => entry.decree.id, ({ decree, directive }) => html`
					<div
						class='row'
						role='button'
						tabindex='0'
						data-id=${decree.id}
						@click=${() => openEntityEditor(decree)}
						@keydown=${(e: KeyboardEvent) => this.onRowKey(e, decree)}>
						<div class='title'>
							<p7t-icon icon='reflective'></p7t-icon>
							<span>${decree.title}</span>
						</div>
						<div class='directive'>
							<p7t-directive-item .directive=${directive}></p7t-directive-item>
						</div>
						<div class='orbit'>
							${decree.orbit
								? html`<p7t-schedule-item .orbit=${decree.orbit} .calendar=${decree.calendar}></p7t-schedule-item>`
								: html`<span class='base'>Base</span>`}
						</div>
					</div>
				`)}
			</div>
		`
	}

	private onRowKey(e: KeyboardEvent, decree: LunarReflectionEntry['decree']) {
		if (e.key === 'Enter' || e.key === ' ') {
			e.preventDefault()
			openEntityEditor(decree)
		}
	}

	/** The entry of the row an event happened in, found through the event's path across the shadow boundary. */
	private entryAt(event: Event): LunarReflectionEntry | undefined {
		const row = event.composedPath().find((node): node is HTMLElement => node instanceof HTMLElement && node.classList.contains('row'))
		return this.entries.find(entry => entry.decree.id === row?.dataset.id)
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-lunar-reflection-list': LunarReflectionList
	}
}
