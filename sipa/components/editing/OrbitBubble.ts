import { component, css, html, state, type PropertyValues } from "@a11d/lit"
import { OrbitUnit, type DeclarativeCalendar } from "@pleiades/sdk"
import { humanizeOrbit } from "../../orbits"
import { CalendarRef } from "../data/CalendarRef"
import { InteractionBubbleBase, type InteractionBubbleOptions } from "../design/InteractionBubbleBase"
import type { EditablePart } from "./EditableDataLink"
import { momentAtGranularity } from "./EditableDateTime"
import type { GranularitySelect } from "./GranularitySelect"
import { oneShotGranularities, readOneShot, writeOneShot, type OneShot } from "./oneShotOrbit"
import "./EditableDateTime"
import "./EditableDuration"
import "./GranularitySelect"

/** What an orbit bubble edits and hands back. */
export interface OrbitBubbleData {
	/** The orbit notation; none clears the schedule. */
	readonly orbit?: string
	/**
	 * The calendar a one-shot's `Z{…}` literal is written on, set only when {@link orbit} is one. A host sends it with the
	 * orbit, so the core reads the literal's year, month and day on the calendar they were written on.
	 */
	readonly calendar?: DeclarativeCalendar
}

/** What an orbit bubble is told. */
export interface OrbitBubbleOptions extends InteractionBubbleOptions<OrbitBubbleData> {
	/** The calendar the schedule's owner names, if any; the orbit is read, and a one-shot written, on it, else on the vault's preferred one. */
	readonly calendar?: DeclarativeCalendar
}

/** How the bubble edits the schedule: as orbit notation, or as one single moment written as a `Z{…}` literal. */
export type OrbitBubbleMode = 'orbit' | 'one-shot'

/** Today, all day: where a fresh one-shot starts, as the eventive creation row does. */
function today(): string {
	const now = new Date()
	const pad = (value: number) => value.toString().padStart(2, '0')
	return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}T00:00:00`
}

/**
 * Edits a schedule's orbit in a bubble beside it, in one of two modes picked by a toggle at its top:
 * - **Orbit** — the raw notation, with its humanised reading beneath it, live as one types;
 * - **One-shot** — one single moment, on the components the eventive creation row edits an epoch with: its
 *   **Scope** (a whole day, or a moment to the minute or second — what a `Z{…}` literal can write), **When** (in
 *   segments, on the schedule's calendar) and **Duration**. It is written as a `Z{y/M/d[Th:m[:s]]}[=dur]` literal.
 *
 * It opens in One-shot when the orbit is nothing but such a literal, and in Orbit otherwise. Switching carries the value
 * across: a one-shot opens Orbit on its literal, and a literal typed in Orbit opens One-shot on its moment.
 *
 * Its data is the orbit and, for a one-shot literal (however it was entered), the calendar it is written on. Save
 * (or Enter in the notation, Ctrl+Enter anywhere) commits; invalid notation, or a moment the calendar cannot write,
 * does not.
 */
@component('p7t-orbit-bubble')
export class OrbitBubble extends InteractionBubbleBase<OrbitBubbleData, OrbitBubbleOptions> {
	@state() private shape: OrbitBubbleMode = 'orbit'
	/** The notation as typed in Orbit mode. */
	@state() private draft = ''
	/** The moment as set in One-shot mode. */
	@state() private oneShot: OneShot = { moment: today(), granularity: OrbitUnit.Day }

	/** The calendar the orbit is read and a one-shot written on: the owner's, else the vault's preferred one. */
	private readonly calendars = new CalendarRef(this, () => this.options?.calendar)

	constructor() {
		super({})
	}

	static override get styles() {
		return css`
			${super.styles}

			:host {
				max-width: min(calc(100vw - 16px), 30rem);
			}

			.head {
				display: flex;
				align-items: center;
				justify-content: space-between;
				gap: 1em;
			}

			.title {
				font-weight: 600;
				color: var(--p7t-bubble-accent);
			}

			.modes {
				display: inline-flex;
				padding: 2px;
				border-radius: 8px;
				background-color: color-mix(in srgb, currentColor 7%, transparent);
			}

			.modes button {
				padding: .2em .7em;
				background-color: transparent;
				opacity: .7;
			}

			.modes button[aria-checked=true] {
				background-color: color-mix(in srgb, var(--p7t-bubble-accent) 30%, transparent);
				opacity: 1;
			}

			.notation {
				box-sizing: border-box;
				width: 100%;
				min-width: 16rem;
				padding: .35em .5em;
				border: 1px solid color-mix(in srgb, currentColor 20%, transparent);
				border-radius: 6px;
				background: color-mix(in srgb, currentColor 5%, transparent);
				color: inherit;
				font-family: var(--font-monospace, monospace);
				font-size: 1em;
			}

			.notation:focus-visible {
				outline: 2px solid var(--p7t-bubble-accent);
				outline-offset: 1px;
			}

			.reading {
				min-height: 1.4em;
				font-weight: 300;
			}

			.reading.empty {
				opacity: .6;
			}

			.reading.invalid {
				color: var(--text-error, #e26d6d);
			}

			.cells {
				display: grid;
				grid-template-columns: repeat(3, auto);
				grid-template-rows: auto auto;
				justify-content: start;
				align-items: center;
				column-gap: 1.2em;
				row-gap: .3em;
			}

			.cell {
				display: grid;
				grid-template-rows: subgrid;
				grid-row: 1 / -1;
			}

			.caption {
				font-size: .7em;
				font-weight: 600;
				text-transform: uppercase;
				letter-spacing: .04em;
				color: var(--text-muted);
			}

			p7t-editable-datetime {
				font-size: 1.1em;
				font-weight: 300;
			}

			.literal {
				font-family: var(--font-monospace, monospace);
				font-size: .9em;
				opacity: .6;
			}
		`
	}

	/** Opens on the orbit it was handed: One-shot for a lone `Z{…}` literal, Orbit for anything else. */
	protected override willUpdate(changed: PropertyValues<this>) {
		super.willUpdate(changed)
		if (!this.hasUpdated) {
			const oneShot = readOneShot(this.data.orbit, this.calendars.calendar)
			this.draft = this.data.orbit ?? ''
			this.shape = oneShot ? 'one-shot' : 'orbit'
			this.oneShot = oneShot ?? this.oneShot
		}
	}

	protected override get content() {
		return html`
			<div class='head'>
				<span id='label' class='title'>Schedule</span>
				<div class='modes' role='radiogroup' aria-label='Schedule type'>
					${this.modeButton('orbit', 'Orbit', 'lucide:repeat')}
					${this.modeButton('one-shot', 'One-shot', 'lucide:calendar-clock')}
				</div>
			</div>
			<div @keydown=${(e: KeyboardEvent) => this.onKeyDown(e)}>
				${this.shape === 'orbit' ? this.orbitTemplate : this.oneShotTemplate}
			</div>
			${this.actionsTemplate({ label: 'Save', icon: 'lucide:check' })}
		`
	}

	private modeButton(mode: OrbitBubbleMode, label: string, icon: 'lucide:repeat' | 'lucide:calendar-clock') {
		return html`
			<button role='radio' aria-checked=${this.shape === mode ? 'true' : 'false'} @click=${() => this.switchMode(mode)}>
				<p7t-icon icon=${icon}></p7t-icon><span>${label}</span>
			</button>
		`
	}

	/** The notation, with its humanised reading beneath it as one types. */
	private get orbitTemplate() {
		const reading = this.reading
		return html`
			<input class='notation' autofocus spellcheck='false' aria-label='Orbit notation' placeholder='w[d{1,3}[h{9}]]'
				.value=${this.draft}
				@input=${(e: Event) => this.editDraft((e.target as HTMLInputElement).value)}>
			<div class='reading ${reading.kind}' aria-live='polite'>${reading.text}</div>
		`
	}

	/** The single moment, edited as the eventive creation row edits an epoch, with the literal it writes beneath. */
	private get oneShotTemplate() {
		const oneShot = this.oneShot
		const calendar = this.calendars.calendar
		const literal = writeOneShot(oneShot, calendar)
		return html`
			<div class='cells'>
				<div class='cell'>
					<span class='caption'>Scope</span>
					<p7t-granularity-select
						.units=${oneShotGranularities}
						.value=${oneShot.granularity}
						@change=${(e: Event) => this.changeGranularity((e.target as GranularitySelect).value)}>
					</p7t-granularity-select>
				</div>
				<div class='cell'>
					<span class='caption'>When</span>
					<p7t-editable-datetime autofocus
						label='When'
						.calendar=${calendar}
						.granularity=${oneShot.granularity}
						.value=${oneShot.moment}
						@change=${(e: Event) => this.editOneShot({ moment: (e.target as EditablePart<string>).value ?? oneShot.moment })}>
					</p7t-editable-datetime>
				</div>
				<div class='cell'>
					<span class='caption'>Duration</span>
					<p7t-editable-duration
						nullable
						.granularity=${oneShot.granularity}
						.value=${oneShot.duration}
						@change=${(e: Event) => this.editOneShot({ duration: (e.target as EditablePart<string>).value })}>
					</p7t-editable-duration>
				</div>
			</div>
			<div class='reading ${literal ? '' : 'invalid'}'>
				${literal ? html`<code class='literal'>${literal}</code>` : 'This calendar cannot write that day.'}
			</div>
		`
	}

	/** The live reading of the notation: its humanised phrase, or why there is none. */
	private get reading(): { readonly kind: 'valid' | 'empty' | 'invalid', readonly text: string } {
		const notation = this.draft.trim()
		if (!notation) {
			return { kind: 'empty', text: 'No schedule — saving clears it.' }
		}

		try {
			const { text, invalid } = humanizeOrbit(notation, false, this.calendars.orbitCalendar)
			return text && !invalid ? { kind: 'valid', text } : { kind: 'invalid', text: 'Invalid orbit notation.' }
		}
		catch {
			return { kind: 'invalid', text: 'Invalid orbit notation.' }
		}
	}

	/**
	 * The orbit the bubble would save, and the calendar a one-shot literal is written on. Undefined when there is
	 * nothing it can save: invalid notation, or a day the calendar cannot write.
	 */
	private get result(): OrbitBubbleData | undefined {
		const calendar = this.calendars.calendar
		const orbit = this.shape === 'orbit' ? this.draft.trim() : writeOneShot(this.oneShot, calendar)
		if (orbit === undefined || (this.shape === 'orbit' && this.reading.kind === 'invalid')) {
			return undefined
		}

		return !orbit ? {} : readOneShot(orbit, calendar) ? { orbit, calendar } : { orbit }
	}

	private editDraft(value: string) {
		this.draft = value
		this.syncData()
	}

	private editOneShot(change: Partial<OneShot>) {
		this.oneShot = { ...this.oneShot, ...change }
		this.syncData()
	}

	/** A new scope makes a different part of the moment real, so the moment moves to the start of its new unit. */
	private changeGranularity(granularity: OrbitUnit | undefined) {
		if (granularity === undefined || granularity === this.oneShot.granularity) {
			return
		}

		this.editOneShot({ granularity, moment: momentAtGranularity(this.oneShot.moment, granularity, this.calendars.calendar) })
	}

	/** Switches mode, carrying the value across: a one-shot becomes its literal, and a typed literal its moment. */
	private switchMode(mode: OrbitBubbleMode) {
		if (mode === this.shape) {
			return
		}

		const calendar = this.calendars.calendar
		if (mode === 'orbit') {
			this.draft = writeOneShot(this.oneShot, calendar) ?? this.draft
		}
		else {
			this.oneShot = readOneShot(this.draft, calendar) ?? this.oneShot
		}

		this.shape = mode
		this.syncData()
		void this.updateComplete.then(() => this.renderRoot.querySelector<HTMLElement>('[autofocus]')?.focus())
	}

	/** Keeps {@link data} the value as it stands, replaced rather than mutated, so it is what a commit hands back. */
	private syncData() {
		const result = this.result
		if (result) {
			this.data = result
		}
	}

	/** Enter in the notation, or Ctrl+Enter anywhere, saves; a plain Enter elsewhere finishes its own field's edit. */
	private onKeyDown(e: KeyboardEvent) {
		if (e.key !== 'Enter' || e.isComposing) {
			return
		}

		const inNotation = e.composedPath()[0] instanceof HTMLInputElement && this.shape === 'orbit'
		if (inNotation || e.ctrlKey || e.metaKey) {
			e.preventDefault()
			// A field still holding an edit lands it on blur, before the value is read.
			;(this.shadowRoot?.activeElement as HTMLElement | null)?.blur()
			void this.updateComplete.then(() => this.commit())
		}
	}

	/** Saves the value as it stands; refused while there is nothing valid to save. */
	override commit(data?: OrbitBubbleData) {
		const result = data ?? this.result
		if (!result) {
			return
		}

		super.commit(result)
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-orbit-bubble': OrbitBubble
	}
}
