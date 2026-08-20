import { Component, component, css, event, eventListener, html, property, state } from "@a11d/lit"
import { EditablePart } from "./EditableDataLink"
import "../entities/ScheduleItem"

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

	/** Whether the editor is showing its fields; idle it shows the read-only {@link ScheduleItem} face. */
	@state() private editing = false

	override connectedCallback() {
		super.connectedCallback()
		// A pointer down anywhere outside the control ends editing. Focusout alone is unreliable here: the
		// date/time fields mount their native input only once clicked and drop it on commit, so focus is often
		// never held by the control — a click elsewhere would then never fire a focusout to close it.
		document.addEventListener('pointerdown', this.onDocumentPointerDown, true)
	}

	override disconnectedCallback() {
		super.disconnectedCallback()
		document.removeEventListener('pointerdown', this.onDocumentPointerDown, true)
	}

	private readonly onDocumentPointerDown = (e: PointerEvent) => {
		if (!this.editing || e.composedPath().includes(this)) return
		// A field still holding focus commits on blur, so blur it first — otherwise collapsing the editor out from
		// under it could drop the in-progress edit.
		;((this.renderRoot as ShadowRoot).activeElement as HTMLElement | null)?.blur()
		this.editing = false
	}

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

			/*
			 * Idle the face carries the same editability outline every other editable shows on hover, so it reads
			 * as a thing you can click into — matching {@link EditablePart}'s treatment, which this control does not
			 * inherit since it wraps the read-only chip rather than being an editable itself.
			 */
			.display {
				cursor: pointer;
				outline: 1px solid transparent;
				outline-offset: .16rem;
				border-radius: 4px;
				transition: .3s ease;
			}

			.display:hover {
				outline-color: var(--p7t-flare-accent, var(--interactive-accent));
			}
		`
	}

	protected override get template() {
		const mode = this.mode

		// Idle shows only the read-only chip — which already carries the type icon, so the switch (which repeats
		// that icon) is withheld until editing, when flipping the type is actually on offer. This is what kept the
		// icon from appearing twice on the idle face.
		if (!this.editing) {
			return html`
				<p7t-schedule-item
					class='display'
					short
					.orbit=${this.orbit}
					.date=${this.date}
					.time=${this.time}
					@click=${() => this.enterEditing()}>
				</p7t-schedule-item>
			`
		}

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

	/** Reveals the fields and lands the caret in the first one, so a single click on the face begins editing. */
	private enterEditing() {
		this.editing = true
		void this.updateComplete.then(() => {
			this.renderRoot.querySelector<HTMLElement>('.fields > *')?.focus()
		})
	}

	/** Returns to the read-only face once focus has actually left the whole control, not merely moved between fields. */
	@eventListener({ type: 'focusout', target: this })
	protected onFocusOut() {
		window.setTimeout(() => {
			if (!this.matches(':focus-within')) {
				this.editing = false
			}
		}, 0)
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
