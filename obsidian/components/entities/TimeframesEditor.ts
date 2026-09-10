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
							style="width: 1.8em; height: 1.8em;"
							.media=${timeframe.iconMedia}
							.value=${timeframe.icon ?? ''}
							@change=${(e: Event) => this.saveIcon(timeframe.id, e)}>
						</p7t-editable-media>
					</div>
					<div class='field'>
						<span class='caption'>Auto-include</span>
						<p7t-item-group
							.items=${timeframe.autoInclusionColleges ?? []}
							.renderItem=${(college: unknown) => html`<p7t-college-item mode='named' .college=${college as ObjectiveCollege}></p7t-college-item>`}
							.onAdd=${() => void this.addCollege(timeframe)}
							.onRemove=${(college: unknown) => this.removeCollege(timeframe, college as ObjectiveCollege)}>
						</p7t-item-group>
					</div>
				</div>
			</div>
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

	/** Adds a college to a timeframe's auto-inclusion list, ignoring "None" and duplicates. */
	private async addCollege(timeframe: Timeframe) {
		const college = await SelectCollegeModal.prompt()
		if (college === undefined || college === ObjectiveCollege.Unspecified) {
			return
		}

		const current = timeframe.autoInclusionColleges ?? []
		if (current.includes(college)) {
			return
		}

		this.saveColleges(timeframe.id, [...current, college])
	}

	private removeCollege(timeframe: Timeframe, college: ObjectiveCollege) {
		this.saveColleges(timeframe.id, (timeframe.autoInclusionColleges ?? []).filter(item => item !== college))
	}

	/** Persists the college list; a non-empty list turns on college auto-inclusion, an empty one turns it off. */
	private saveColleges(timeframeId: number, colleges: ObjectiveCollege[]) {
		void this.saveTimeframe(timeframeId, {
			autoInclusion: colleges.length > 0 ? TimeframeInclusion.College : TimeframeInclusion.None,
			autoInclusionColleges: colleges,
		})
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
