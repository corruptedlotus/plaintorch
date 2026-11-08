import { component, css, event, html, nothing, property, state } from "@a11d/lit"
import type { OrbitUnit } from "@pleiades/sdk"
import { ScheduleItem } from "../entities/ScheduleItem"
import { OrbitBubble, type OrbitBubbleData } from "./OrbitBubble"

/**
 * A schedule chosen through {@link EditableSchedule}: its orbit (none clears it) and, when that is a one-shot `Z{…}`
 * literal, the calendar the literal is written on, for the host to send with it.
 */
export type ScheduleValue = OrbitBubbleData

/**
 * The editable orbit of whatever schedules by one — a fate (orbit-only, PEP111), a decree, a timeframe. It **is** a
 * {@link ScheduleItem}: idle it reads exactly like the plain chip — the humanised orbit, with the orbit tooltip — and
 * carries the editability outline on hover.
 *
 * A click (or Enter/Space) opens an {@link OrbitBubble} beside it to edit the orbit: as notation with its live reading, or
 * as a one-shot moment written as a `Z{…}` literal — the mode it opens in follows the orbit. A second click closes it.
 * Saving sets the orbit and emits `schedulechange` with the {@link ScheduleValue} for the host to persist. An owner
 * whose orbit takes less than a fate's narrows the one-shot with {@link oneShotUnits} and {@link oneShotDuration}.
 */
@component('p7t-editable-schedule')
export class EditableSchedule extends ScheduleItem {
	@event() schedulechange!: EventDispatcher<ScheduleValue>

	/**
	 * The scopes a one-shot may take, for an owner whose orbit takes fewer than a literal can write: a timeframe or a
	 * reflecting decree only a whole day. Unset, all of them; empty, notation alone.
	 */
	@property({ type: Array }) oneShotUnits?: readonly OrbitUnit[]

	/** Whether a one-shot may carry a duration: a decree's or a timeframe's orbit takes no span. */
	@property({ type: Boolean }) oneShotDuration = true

	/** Whether the orbit bubble is open: the face keeps its accent outline and gives up its tooltip meanwhile. */
	@state() private editing = false

	static override get styles() {
		return css`
			${super.styles}

			.control {
				display: inline-flex;
				align-items: center;
				border-radius: 4px;
				outline: 1px solid transparent;
				outline-offset: .16rem;
				cursor: pointer;
				transition: .3s ease;
			}

			.control:hover,
			.control:focus-visible,
			.control.editing {
				outline-color: var(--p7t-flare-accent, var(--interactive-accent));
			}
		`
	}

	protected override get template() {
		return html`
			<div class='control ${this.editing ? 'editing' : ''}' role='button' tabindex='0'
				aria-label='Edit schedule' aria-haspopup='dialog' aria-expanded=${this.editing ? 'true' : 'false'}
				@click=${(e: Event) => void this.edit(e)}
				@keydown=${(e: KeyboardEvent) => this.onKeyDown(e)}>
				${super.template}
			</div>
		`
	}

	/** The orbit tooltip steps aside while the bubble is open over the same schedule. */
	protected override get tooltip() {
		return this.editing ? nothing : super.tooltip
	}

	private onKeyDown(e: KeyboardEvent) {
		if (e.key === 'Enter' || e.key === ' ') {
			e.preventDefault()
			void this.edit(e)
		}
	}

	/**
	 * Opens the orbit bubble on the schedule as it stands, read on the owner's calendar (else the vault's preferred one);
	 * pressed again while open, it closes it. A save sets the orbit and announces it.
	 */
	private async edit(anchor: Event) {
		this.editing = true
		try {
			await OrbitBubble.prompt(anchor, {
				calendar: this.calendar,
				oneShotUnits: this.oneShotUnits,
				oneShotDuration: this.oneShotDuration,
				run: value => this.apply(value)
			}, { orbit: this.orbit })
		}
		finally {
			this.editing = false
		}
	}

	private apply(value: ScheduleValue) {
		const orbit = value.orbit || undefined
		const unchanged = orbit === (this.orbit || undefined) && (value.calendar === undefined || value.calendar === this.calendar)
		if (unchanged) {
			return
		}

		this.orbit = orbit
		this.schedulechange.dispatch({ orbit, calendar: value.calendar })
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-editable-schedule': EditableSchedule
	}
}
