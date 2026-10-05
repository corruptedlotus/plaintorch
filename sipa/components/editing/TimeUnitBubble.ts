import { component, css, html, query } from "@a11d/lit"
import { InteractionBubbleBase, type InteractionBubbleOptions } from "../design/InteractionBubbleBase"
import type { IconName } from "../PleiadesIcon"
import type { EditableTimeUnit } from "./EditableTimeUnit"
import "./EditableTimeUnit"

/** What a time-unit bubble is told. Every part is optional and falls back to a plain default. */
export interface TimeUnitBubbleOptions extends InteractionBubbleOptions<number | undefined> {
	/** What the bubble asks for, at its top ("Set estimation"). Falls back to "Time". */
	readonly heading?: string
	/** The committing button's label. Falls back to "Save". */
	readonly action?: string
	/** A glyph on the committing button. Falls back to a check. */
	readonly actionIcon?: IconName
	/** The dismissing button's label. Falls back to "Cancel". */
	readonly cancel?: string
	/**
	 * Whether the time can be cleared to none, as an unset allocation can. Off by default: a plain bubble floors the
	 * time at zero, and never commits none.
	 */
	readonly nullable?: boolean
}

const fallbackHeading = 'Time'
const fallbackAction = 'Save'
const fallbackActionIcon: IconName = 'lucide:check'

/**
 * Edits one whole-minute working time unit in a bubble beside what asked: an estimate set the moment an activity is
 * planned, a quick allocation.
 *
 * Its body is `p7t-editable-time-unit` at a glance's size, already editing when the bubble opens: digits set the hours,
 * the arrow keys (and the steppers beside it) move by quarters, and Backspace clears it (or floors it). Enter, or the
 * committing button, saves the time as it stands; Escape, the dismissing button or a press elsewhere leaves it as it was.
 *
 * Its data is the time in minutes: `undefined` for none, which only a
 * {@link TimeUnitBubbleOptions.nullable | nullable} bubble commits.
 *
 * ```ts
 * const estimation = await TimeUnitBubble.prompt(button, { heading: 'Set estimation', nullable: true }, executive.estimation)
 * ```
 */
@component('p7t-time-unit-bubble')
export class TimeUnitBubble extends InteractionBubbleBase<number | undefined, TimeUnitBubbleOptions> {
	@query('p7t-editable-time-unit') private readonly field?: EditableTimeUnit

	constructor() {
		super(undefined)
	}

	static override get styles() {
		return css`
			${super.styles}

			.heading {
				font-weight: 600;
				color: var(--p7t-bubble-accent);
			}

			.field {
				display: flex;
				justify-content: center;
				padding-block: .2em;
			}

			/* The field follows the bubble's accent, however a host retints it. */
			p7t-editable-time-unit {
				--p7t-flare-accent: var(--p7t-bubble-accent);
				font-size: 2.4em;
				font-weight: 300;
				line-height: 1;
			}

			.hints {
				display: flex;
				justify-content: center;
				gap: 1.2em;
				font-size: .72em;
				color: var(--text-faint, color-mix(in srgb, currentColor 55%, transparent));
			}

			kbd {
				font-family: inherit;
				font-weight: 600;
			}
		`
	}

	protected override get content() {
		const options = this.options ?? {}
		const heading = options.heading || fallbackHeading
		return html`
			<span id='label' class='heading'>${heading}</span>
			<div class='field' @keydown=${{ handleEvent: (e: KeyboardEvent) => this.onFieldKeyDown(e), capture: true }}>
				<p7t-editable-time-unit
					autofocus
					accent
					aria-label=${heading}
					?nullable=${!!options.nullable}
					.value=${this.data}
					@preview=${() => this.data = this.field?.value}
					@change=${() => this.data = this.field?.value}>
				</p7t-editable-time-unit>
			</div>
			<div class='hints'>
				<span><kbd>0–9</kbd> hours</span>
				<span><kbd>↑↓</kbd> quarters</span>
			</div>
			${this.actionsTemplate(
				{ label: options.action || fallbackAction, icon: options.actionIcon ?? fallbackActionIcon },
				{ label: options.cancel },
			)}
		`
	}

	/**
	 * Enter saves the time as it stands. Taken on its way down to the field, which would otherwise end its own edit on
	 * it and let go of the focus, leaving none for the bubble to hand back on closing.
	 */
	private onFieldKeyDown(e: KeyboardEvent) {
		if (e.key !== 'Enter' || e.isComposing) {
			return
		}

		e.preventDefault()
		e.stopPropagation()
		this.commit()
	}

	/** Saves the time the field holds, which a plain bubble floors at zero rather than commit none. */
	override commit(data: number | undefined = this.field ? this.field.value : this.data) {
		super.commit(data ?? (this.options?.nullable ? undefined : 0))
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-time-unit-bubble': TimeUnitBubble
	}
}
