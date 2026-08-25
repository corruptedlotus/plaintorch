import { Component, component, css, html, nothing, property, PropertyValues, repeat, state } from '@a11d/lit'
import { ObjectiveCollege, Timeframe, TimeframeInclusion, TimeframeUpdate } from '@pleiades/sdk'
import { Notice } from 'obsidian'
import { core, SelectCollegeModal } from '..'
import type { EditablePart } from '../editing/EditableDataLink'

/** A time-of-day (TimeOnly) as the core serialises it, trimmed to the `HH:mm` an `<input type="time">` shows. */
const timeInputValue = (value: string | undefined) => (value ?? '').slice(0, 5)

/** Widens an `HH:mm` picker value back to the `HH:mm:ss` the TimeOnly contract parses. */
const toTimeOnly = (value: string) => (value.length === 5 ? `${value}:00` : value)

/**
 * The timeframes a lunar directive defines, edited in place (PEP100 patch).
 *
 * Modelled on the onrush detail window's orders list: the lunar directive editing modal shows this beneath the
 * directive's banner, each row editing one timeframe's title, its window, its Orbit scoping, its icon key, and the
 * college it auto-includes. Timeframes are not a tracked repository, so the list is fetched here and re-read after
 * each write rather than observed through the store.
 */
@component('p7t-timeframes-editor')
export class TimeframesEditor extends Component {
	/** The lunar directive whose timeframes these are. */
	@property() directiveId = ''

	@state() private timeframes: readonly Timeframe[] = []
	@state() private loading = true

	static override get styles() {
		return css`
			:host {
				display: flex;
				flex-direction: column;
				font-family: var(--font-interface);
				color: var(--text-normal);
				padding: .4em .6em 1em;
			}

			.heading {
				display: flex;
				align-items: center;
				gap: .5ch;
				font-size: .8em;
				font-weight: 600;
				text-transform: uppercase;
				letter-spacing: .04em;
				color: var(--p7t-flare-accent, var(--interactive-accent));
				margin-block: .2em .6em;

				& p7t-icon {
					width: 1.3em;
					height: 1.3em;
				}
			}

			.notice {
				padding: .8em .2em;
				opacity: .5;
				font-weight: 300;
			}

			.cards {
				display: flex;
				flex-direction: column;
				gap: .5em;
			}

			.card {
				display: flex;
				flex-direction: column;
				gap: .5em;
				background-color: color-mix(in srgb, var(--text-normal) 5%, transparent);
				border-radius: 8px;
				padding: .5em .7em;
			}

			.card-head {
				display: flex;
				align-items: center;
				gap: .6em;
			}

			.title {
				flex: 1;
				text-align: start;
				justify-content: flex-start;
				font-weight: 400;
			}

			.fields {
				display: flex;
				flex-wrap: wrap;
				align-items: center;
				gap: .3em 1.1em;
			}

			.field {
				display: flex;
				align-items: center;
				gap: .35em;
				font-size: .9em;
			}

			.caption {
				font-size: .82em;
				font-weight: 600;
				text-transform: uppercase;
				letter-spacing: .03em;
				color: color-mix(in srgb, var(--text-normal) 55%, transparent);
			}

			input[type='time'] {
				font-family: inherit;
				color: inherit;
				background: transparent;
				border: 1px solid color-mix(in srgb, var(--text-normal) 18%, transparent);
				border-radius: 5px;
				padding: .1em .3em;
			}

			.field p7t-icon {
				width: 1.2em;
				height: 1.2em;
				opacity: .7;
			}

			.include {
				display: flex;
				align-items: center;
				gap: .35em;

				& p7t-icon {
					width: 1.3em;
					height: 1.3em;
					opacity: 1;
				}

				&.muted {
					opacity: .5;
				}
			}


			.add {
				align-self: flex-start;
				margin-top: .8em;
			}
		`
	}

	protected override connected() {
		void this.refresh()
	}

	protected override updated(changed: PropertyValues) {
		if (changed.has('directiveId')) {
			void this.refresh()
		}
	}

	private async refresh() {
		if (!this.directiveId) {
			this.timeframes = []
			this.loading = false
			return
		}

		this.timeframes = await core.directives.listTimeframes(this.directiveId)
		this.loading = false
	}

	protected override get template() {
		return html`
			<div class='heading'>
				<p7t-icon icon='lucide:clock'></p7t-icon>
				<span>Timeframes</span>
			</div>
			<div class='cards'>
				${this.timeframes.length > 0 ? nothing : html`
					<div class='notice'>${this.loading ? 'Loading…' : 'No timeframes yet.'}</div>
				`}
				${repeat(this.timeframes, timeframe => timeframe.id, timeframe => this.cardTemplate(timeframe))}
			</div>
			<p7t-button class='add' icon='lucide:clock' @click=${() => this.addTimeframe()}>
				<span>Add timeframe</span>
			</p7t-button>
		`
	}

	private cardTemplate(timeframe: Timeframe) {
		return html`
			<div class='card'>
				<div class='card-head'>
					<p7t-editable-plaintext
						class='title'
						.value=${timeframe.title}
						@change=${(e: Event) => this.saveTimeframe(timeframe.id, { title: (e.target as EditablePart<string>).value ?? '' })}>
					</p7t-editable-plaintext>
					<p7t-button ghost danger icon='lucide:trash-2' label='Remove timeframe' @click=${() => this.removeTimeframe(timeframe)}></p7t-button>
				</div>
				<div class='fields'>
					<div class='field'>
						<p7t-icon icon='lucide:clock'></p7t-icon>
						<input
							type='time'
							.value=${timeInputValue(timeframe.startTime)}
							@change=${(e: Event) => this.saveTime(timeframe.id, 'startTime', e)}>
						<p7t-icon icon='lucide:arrow-right'></p7t-icon>
						<input
							type='time'
							.value=${timeInputValue(timeframe.endTime)}
							@change=${(e: Event) => this.saveTime(timeframe.id, 'endTime', e)}>
					</div>
					<div class='field'>
						<span class='caption'>Orbit</span>
						<p7t-editable-orbit
							.value=${timeframe.orbit}
							@change=${(e: Event) => this.saveTimeframe(timeframe.id, { orbit: (e.target as EditablePart<string>).value ?? '' })}>
						</p7t-editable-orbit>
					</div>
					<div class='field'>
						<span class='caption'>Icon</span>
						<p7t-editable-media
							icon
							.media=${timeframe.iconMedia}
							.value=${timeframe.icon ?? ''}
							@change=${(e: Event) => this.saveIcon(timeframe.id, e)}>
						</p7t-editable-media>
					</div>
					<div class='field'>
						<span class='caption'>Auto-include</span>
						<p7t-editable
							.value=${timeframe.autoInclusionCollege}
							.doEdit=${SelectCollegeModal.prompt}
							@change=${(e: Event) => this.saveCollege(timeframe.id, e)}>
							${this.includeTemplate(timeframe)}
						</p7t-editable>
					</div>
				</div>
			</div>
		`
	}

	/** The display inside the auto-include selector: the chosen college's glyph and name, or a muted placeholder. */
	private includeTemplate(timeframe: Timeframe) {
		const active = timeframe.autoInclusion === TimeframeInclusion.College && timeframe.autoInclusionCollege !== undefined
		if (!active) {
			return html`
				<span class='include muted'>
					<p7t-icon icon='college-none'></p7t-icon>
					<span>None</span>
				</span>
			`
		}

		const name = ObjectiveCollege[timeframe.autoInclusionCollege!]
		return html`
			<span class='include'>
				<p7t-icon icon='college-${name.toLowerCase()}'></p7t-icon>
				<span>${name}</span>
			</span>
		`
	}

	private saveTime(timeframeId: number, field: 'startTime' | 'endTime', e: Event) {
		const value = (e.target as HTMLInputElement).value
		if (!value) {
			return
		}

		void this.saveTimeframe(timeframeId, { [field]: toTimeOnly(value) })
	}

	private saveIcon(timeframeId: number, e: Event) {
		const value = (e.target as EditablePart<string>).value?.trim() ?? ''
		void this.saveTimeframe(timeframeId, { icon: value || null })
	}

	private saveCollege(timeframeId: number, e: Event) {
		const college = (e.target as EditablePart<ObjectiveCollege>).value
		if (college === undefined) {
			return
		}

		// The "None" college is how a timeframe is turned back into a manual (non-auto-including) one.
		const update: TimeframeUpdate = college === ObjectiveCollege.Unspecified
			? { autoInclusion: TimeframeInclusion.None, autoInclusionCollege: null }
			: { autoInclusion: TimeframeInclusion.College, autoInclusionCollege: college }
		void this.saveTimeframe(timeframeId, update)
	}

	private async saveTimeframe(timeframeId: number, update: TimeframeUpdate) {
		const saved = await core.directives.updateTimeframe(timeframeId, update)
		if (!saved) {
			new Notice('PLAINTORCH could not save that timeframe.')
		}

		await this.refresh()
	}

	private async addTimeframe() {
		const created = await core.directives.createTimeframe(this.directiveId, {
			title: 'New timeframe',
			startTime: '09:00:00',
			endTime: '17:00:00',
		})
		if (!created) {
			new Notice('PLAINTORCH could not add that timeframe.')
			return
		}

		await this.refresh()
	}

	private async removeTimeframe(timeframe: Timeframe) {
		const removed = await core.directives.deleteTimeframe(timeframe.id)
		if (!removed) {
			new Notice('PLAINTORCH could not remove that timeframe.')
			return
		}

		new Notice(`Removed ${timeframe.title}.`)
		await this.refresh()
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-timeframes-editor': TimeframesEditor
	}
}
