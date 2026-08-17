import { Component, component, css, event, html, property, state } from "@a11d/lit"
import { EditablePart } from "./EditableDataLink"

export type ScheduleMode = 'orbit' | 'datetime'

/** The schedule a fate-like entity carries: a recurring orbit, or a fixed date and time — never both. */
export interface ScheduleValue {
	mode: ScheduleMode
	orbit?: string
	date?: string
	time?: string
}

/**
 * Dynamic editable for entities that can be scheduled either way — a recurring **orbit** or a one-off
 * **date/time**. A switch flips between the two; the display prefers the orbit (orbit &gt; datetime), and
 * committing in one mode clears the other so the entity never carries both. Emits `schedulechange` with the
 * chosen mode and its value for the host to persist.
 */
@component('p7t-editable-orbit-datetime')
export class EditableOrbitDatetime extends Component {
	@property() orbit?: string
	@property() date?: string
	@property() time?: string

	@event() schedulechange!: EventDispatcher<ScheduleValue>

	/** The chosen mode; falls back to whichever shape the values already describe (orbit takes precedence). */
	@state() private chosenMode?: ScheduleMode

	private get mode(): ScheduleMode {
		return this.chosenMode ?? (this.orbit ? 'orbit' : this.date ? 'datetime' : 'orbit')
	}

	static override get styles() {
		return css`
			:host {
				display: inline-flex;
				align-items: center;
				gap: .5ch;
			}

			.switch {
				display: inline-flex;
				align-items: center;
				justify-content: center;
				padding: .15em;
				border: none;
				border-radius: 6px;
				background-color: color-mix(in srgb, var(--text-normal) 8%, transparent);
				color: color-mix(in srgb, var(--text-normal) 65%, transparent);
				cursor: pointer;
				transition: .2s ease;
			}

			.switch:hover {
				color: var(--text-normal);
				background-color: color-mix(in srgb, var(--text-normal) 16%, transparent);
			}

			.switch p7t-icon {
				width: 1.1em;
				height: 1.1em;
			}

			.fields {
				display: inline-flex;
				align-items: center;
				gap: .5ch;
			}
		`
	}

	protected override get template() {
		const mode = this.mode
		return html`
			<button
				class='switch'
				aria-label='Switch schedule type'
				title=${mode === 'orbit' ? 'Recurring schedule — switch to a fixed date' : 'Fixed date — switch to a recurring schedule'}
				@click=${() => this.switchMode()}>
				<p7t-icon icon=${mode === 'orbit' ? 'lucide:repeat' : 'lucide:calendar-clock'}></p7t-icon>
			</button>
			<div class='fields'>
				${mode === 'orbit' ? html`
					<p7t-editable-orbit
						.value=${this.orbit}
						@change=${(e: Event) => this.commit('orbit', (e.target as EditablePart<string>).value)}>
					</p7t-editable-orbit>
				` : html`
					<p7t-editable-date
						.value=${this.date}
						@change=${(e: Event) => this.commit('date', (e.target as EditablePart<string>).value)}>
					</p7t-editable-date>
					<p7t-editable-time
						.value=${this.time}
						@change=${(e: Event) => this.commit('time', (e.target as EditablePart<string>).value)}>
					</p7t-editable-time>
				`}
			</div>
		`
	}

	private switchMode() {
		const next: ScheduleMode = this.mode === 'orbit' ? 'datetime' : 'orbit'
		this.chosenMode = next
		if (next === 'orbit') {
			// Recurring wins over a lingering date; clearing it keeps the fate from materializing both shapes.
			this.date = undefined
			this.time = undefined
		}
		else {
			this.orbit = undefined
		}

		this.emit()
	}

	private commit(field: 'orbit' | 'date' | 'time', raw: string | undefined) {
		const value = raw && raw.length > 0 ? raw : undefined
		if (field === 'orbit') {
			this.orbit = value
			this.chosenMode = 'orbit'
		}
		else if (field === 'date') {
			this.date = value
			this.chosenMode = 'datetime'
		}
		else {
			this.time = value
			this.chosenMode = 'datetime'
		}

		this.emit()
	}

	private emit() {
		this.schedulechange.dispatch(this.mode === 'orbit'
			? { mode: 'orbit', orbit: this.orbit }
			: { mode: 'datetime', date: this.date, time: this.time })
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-editable-orbit-datetime': EditableOrbitDatetime
	}
}
