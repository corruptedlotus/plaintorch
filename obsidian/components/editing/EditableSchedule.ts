import { component, css, event, eventListener, html, nothing, property, state } from "@a11d/lit"
import { ScheduleItem } from "../entities/ScheduleItem"
import { EditablePart } from "./EditableDataLink"
import "./EditableOrbit"
import "./EditableDate"
import "./EditableTime"
import "../design/Tooltip"

export type ScheduleMode = 'orbit' | 'datetime'

/** The schedule a fate-like entity carries: a recurring orbit, or a fixed date and time — never both. */
export interface ScheduleValue {
	mode: ScheduleMode
	orbit?: string
	date?: string
	time?: string
	/** The end of a datetime range, when {@link EditableSchedule.range} is on (e.g. a fate's event window). */
	endTime?: string
}

/**
 * The editable schedule. It **is** a {@link ScheduleItem} — it inherits the orbit/date/time composition, the mode
 * logic, the shared calendar calculator and the composed tooltip — and only overrides the three chip templates
 * ({@link orbitTemplate}, {@link dateTemplate}, {@link timeTemplate}) to swap each read-only chip for its editable
 * counterpart. Those fields are held {@link EditablePart.disabled | inert} until the schedule enters editing, so idle
 * it reads exactly like the plain chip — the same single surrogate tooltip and all; a click arms it, and a switch
 * (accent-coloured while editing) flips orbit ⇄ datetime.
 *
 * The display prefers the orbit (orbit &gt; datetime), and committing in one mode clears the other so the entity
 * never carries both. An empty schedule falls back to the orbit "No schedule" face even after a switch to datetime.
 * Emits `schedulechange` with the chosen mode and its value for the host to persist.
 */
@component('p7t-editable-schedule')
export class EditableSchedule extends ScheduleItem {
	/** Whether the datetime mode edits a start–end range (a second time field) rather than a single time. */
	@property({ type: Boolean }) range = false

	@event() schedulechange!: EventDispatcher<ScheduleValue>

	/** The chosen mode; falls back to whichever shape the values already describe (orbit takes precedence). */
	@state() private chosenMode?: ScheduleMode

	/** Whether the editor is armed; idle the inert fields render the same read-only face the plain chip shows. */
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
		this.endEditing()
	}

	/** The active mode — chosen if the user has flipped, else inferred from the values (orbit wins). Never "none". */
	private get editMode(): ScheduleMode {
		return this.chosenMode ?? (this.orbit ? 'orbit' : this.date ? 'datetime' : 'orbit')
	}

	/** True when the schedule carries no value at all, in either shape. */
	private get isEmpty(): boolean {
		return !this.orbit && !this.date && !this.time && !this.endTime
	}

	static override get styles() {
		return css`
			${super.styles}

			:host {
				display: inline-flex;
				align-items: center;
			}

			/*
			 * Idle the control carries the same editability outline every other editable shows on hover, so it reads
			 * as a thing you can click into. While editing the outline gives way to the fields' own affordances.
			 */
			.control {
				display: inline-flex;
				align-items: center;
				gap: .5ch;
				border-radius: 4px;
				outline: 1px solid transparent;
				outline-offset: .16rem;
				transition: .3s ease;
			}

			.control:not(.editing) {
				cursor: pointer;
			}

			.control:not(.editing):hover {
				outline-color: var(--p7t-flare-accent, var(--interactive-accent));
			}

			.fields {
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
				cursor: pointer;
				transition: .2s ease;
				/* Accent-coloured while editing — the switch only shows then, so this is its resting look. */
				background-color: var(--p7t-flare-accent, var(--interactive-accent));
				color: var(--text-on-accent, white);
			}

			.switch:hover {
				filter: brightness(1.1);
			}

			.switch p7t-icon {
				width: 1.1em;
				height: 1.1em;
				/* Full opacity over the accent fill — overrides the dimmed icon rule inherited from the schedule chip. */
				opacity: 1;
			}

			/* The idle mode indicator: the plain chip's dimmed glyph, so an unarmed schedule reads like the read-only one. */
			.mode-glyph {
				width: 1.1em;
				height: 1.1em;
				opacity: .7;
				flex: 0 0 auto;
			}

			.range-sep {
				width: 1em;
				height: 1em;
				opacity: .5;
			}
		`
	}

	protected override get template() {
		const mode = this.editMode
		const icon = mode === 'orbit' ? 'lucide:repeat' : 'lucide:calendar-clock'

		// The composed schedule tooltip is surrogated by this chip, not by the inner fields: idle it wraps the whole
		// control in the one tooltip {@link ScheduleItem} builds (the orbit reading, or the Gregorian date + time),
		// and the inert fields draw no tooltip of their own. While editing it steps aside for the live fields.
		const tip = this.editing ? nothing : this.tooltip
		const tipText = typeof tip === 'string' ? tip : ''
		const tipRich = tip !== nothing && tip !== undefined && tip !== null && typeof tip !== 'string'
		const hasTip = tipText.length > 0 || tipRich

		return html`
			<p7t-tooltip ?disabled=${!hasTip} .text=${tipText}>
				<div class='control ${this.editing ? 'editing' : ''}' @click=${() => this.onControlClick()}>
					${this.editing ? html`
						<p7t-tooltip
							text=${mode === 'orbit' ? 'Recurring schedule — switch to a fixed date' : 'Fixed date — switch to a recurring schedule'}
						>
							<button class='switch' aria-label='Switch schedule type' @click=${() => this.switchMode()}>
								<p7t-icon icon=${icon}></p7t-icon>
							</button>
						</p7t-tooltip>
					` : html`
						<!-- Idle the mode reads as the same dimmed glyph the plain schedule chip shows; arming swaps it for the accent switch. -->
						<p7t-icon class='mode-glyph' icon=${icon}></p7t-icon>
					`}
					<span class='fields'>
						${mode === 'orbit' ? this.orbitTemplate() : html`${this.dateTemplate()}${this.timeTemplate()}`}
					</span>
				</div>
				${tipRich ? html`<div slot='tooltip'>${tip}</div>` : nothing}
			</p7t-tooltip>
		`
	}

	/** The orbit chip, as an editable field held inert until the schedule is armed. */
	protected override orbitTemplate(): unknown {
		return html`
			<p7t-editable-orbit
				?disabled=${!this.editing}
				.value=${this.orbit}
				@change=${(e: Event) => this.commit('orbit', (e.target as EditablePart<string>).value)}>
			</p7t-editable-orbit>
		`
	}

	/** The date chip, as an editable field held inert until the schedule is armed. */
	protected override dateTemplate(): unknown {
		return html`
			<p7t-editable-date
				?disabled=${!this.editing}
				.value=${this.date}
				@change=${(e: Event) => this.commit('date', (e.target as EditablePart<string>).value)}>
			</p7t-editable-date>
		`
	}

	/** The time chip (with the range end when {@link range} is on), as editable fields held inert until armed. */
	protected override timeTemplate(): unknown {
		return html`
			<p7t-editable-time
				?disabled=${!this.editing}
				.value=${this.time}
				@change=${(e: Event) => this.commit('time', (e.target as EditablePart<string>).value)}>
			</p7t-editable-time>
			${!this.range ? nothing : html`
				<p7t-icon class='range-sep' icon='lucide:arrow-right'></p7t-icon>
				<p7t-editable-time
					?disabled=${!this.editing}
					.value=${this.endTime}
					@change=${(e: Event) => this.commit('endtime', (e.target as EditablePart<string>).value)}>
				</p7t-editable-time>
			`}
		`
	}

	/** A click on the idle face arms the editor and lands the caret in the first field; while editing it is a no-op. */
	private onControlClick() {
		if (this.editing) {
			return
		}

		this.enterEditing()
	}

	private enterEditing() {
		this.editing = true
		void this.updateComplete.then(() => {
			this.renderRoot.querySelector<HTMLElement>('.fields > *')?.focus()
		})
	}

	/**
	 * Leaves editing. An empty schedule drops any chosen datetime mode so it falls back to the orbit "No schedule"
	 * face — a user who flips to datetime, enters nothing, and clicks away should not be left staring at empty
	 * date/time fields.
	 */
	private endEditing() {
		this.editing = false
		if (this.isEmpty) {
			this.chosenMode = undefined
		}
	}

	/** Returns to the read-only face once focus has actually left the whole control, not merely moved between fields. */
	@eventListener({ type: 'focusout', target: this })
	protected onFocusOut() {
		window.setTimeout(() => {
			if (!this.matches(':focus-within')) {
				this.endEditing()
			}
		}, 0)
	}

	private switchMode() {
		const next: ScheduleMode = this.editMode === 'orbit' ? 'datetime' : 'orbit'
		this.chosenMode = next
		if (next === 'orbit') {
			// Recurring wins over a lingering date; clearing it keeps the fate from materializing both shapes.
			this.date = undefined
			this.time = undefined
			this.endTime = undefined
		}
		else {
			this.orbit = undefined
		}

		this.emit()
	}

	private commit(field: 'orbit' | 'date' | 'time' | 'endtime', raw: string | undefined) {
		const value = raw && raw.length > 0 ? raw : undefined
		if (field === 'orbit') {
			this.orbit = value
			this.chosenMode = 'orbit'
		}
		else if (field === 'date') {
			this.date = value
			this.chosenMode = 'datetime'
		}
		else if (field === 'endtime') {
			this.endTime = value
			this.chosenMode = 'datetime'
		}
		else {
			this.time = value
			this.chosenMode = 'datetime'
		}

		this.emit()
	}

	private emit() {
		this.schedulechange.dispatch(this.editMode === 'orbit'
			? { mode: 'orbit', orbit: this.orbit }
			: { mode: 'datetime', date: this.date, time: this.time, endTime: this.range ? this.endTime : undefined })
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-editable-schedule': EditableSchedule
	}
}
