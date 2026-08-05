import { component, css, eventListener, html, property } from "@a11d/lit"
import { EditablePart } from "./EditableDataLink"

/** Fine adjustment step, in whole minutes: a quarter of an hour. */
const quarter = 15

/** How long consecutive digits keep accumulating into the same hour entry. */
const digitEntryWindow = 900

/**
 * Editable counterpart of `p7t-time-unit`, operating on whole-minute working time units.
 *
 * Editing begins on focus, which reveals a stepper on either side of the unit. Steppers and the
 * arrow keys fine-adjust by quarters, while typing digits sets the hour marker (`digits * 60`).
 * Every adjustment raises `preview` for live surfaces; the committed value is published through
 * the inherited `edit`/`change` events once editing ends.
 */
@component('p7t-editable-time-unit')
export class EditableTimeUnit extends EditablePart<number> {
	/** Paints the unit in the accent colour rather than the ambient text colour. */
	@property({ type: Boolean, reflect: true }) accent = false

	/** Upper bound for the value, in whole minutes. */
	@property({ type: Number }) ceiling = 24 * 60

	override readonly tabIndex = 0

	private entryBuffer = ''
	private entryTimer?: ReturnType<typeof setTimeout>
	private valueAtFocus?: number

	static override get styles() {
		return css`
			${super.styles}

			:host {
				position: relative;
				display: inline-flex;
				align-items: center;
				justify-content: center;
				cursor: pointer;
				user-select: none;
				outline-offset: .3rem;
			}

			:host(:focus) {
				outline-color: var(--p7t-flare-accent, var(--interactive-accent));
			}

			:host([accent]) {
				color: var(--p7t-flare-accent, var(--interactive-accent));
			}

			/* Without the accent the arc should follow the ambient text colour instead. */
			:host(:not([accent])) {
				--p7t-time-unit-arc: currentColor;
			}

			/*
			 * The steppers overlay the outer sides rather than sitting in flow, so an idle unit
			 * reserves no space for them and its projected size never shifts on entering editing.
			 */
			.stepper {
				position: absolute;
				display: flex;
				flex-direction: column;
				justify-content: space-between;
				align-items: center;
				height: calc(100% + 1.2rem);
				margin-block: -.6rem;
				margin-inline: .2rem;
				scale: .4;
				opacity: 0;
				transition: .2s ease;
				pointer-events: none;
				color: var(--p7t-flare-accent, var(--interactive-accent));
				left: calc(100% + .3rem);
				background-color: color-mix(in srgb, black 50%, var(--background-primary));
				font-size: .4em;
				border-radius: 8px;
			}

			:host([active]) .stepper {
				opacity: .75;
				scale: 1;
				pointer-events: auto;
			}

			:host([active]) .stepper > *:hover {
				opacity: 1;
			}
		`
	}

	protected override get template() {
		return html`
			<p7t-time-unit .value=${Math.max(0, this.value ?? 0)}></p7t-time-unit>

			<div class='stepper'>
				<p7t-icon
					class='increment'
					icon='lucide:chevron-up'
					@mousedown=${(e: Event) => e.preventDefault()}
					@click=${() => this.step(1)}>
				</p7t-icon>
				<p7t-icon
					class='decrement'
					icon='lucide:chevron-down'
					@mousedown=${(e: Event) => e.preventDefault()}
					@click=${() => this.step(-1)}>
				</p7t-icon>
			</div>
		`
	}

	@eventListener({ type: 'focus', target: this })
	protected handleFocus() {
		this.valueAtFocus = this.value
		this.beginManualEditing()
	}

	@eventListener({ type: 'blur', target: this })
	protected handleBlur() {
		this.flushEntry()
		if (this.value === this.valueAtFocus) {
			this.active = false
			return
		}

		this.finishEditing(this.value)
	}

	@eventListener({ type: 'keydown', target: this })
	protected handleKeyDown(e: KeyboardEvent) {
		switch (e.key) {
			case 'ArrowUp':
			case 'ArrowRight':
				e.preventDefault()
				this.flushEntry()
				this.step(1)
				return
			case 'ArrowDown':
			case 'ArrowLeft':
				e.preventDefault()
				this.flushEntry()
				this.step(-1)
				return
			case 'Enter':
				e.preventDefault()
				this.blur()
				return
			case 'Escape':
				e.preventDefault()
				this.flushEntry()
				this.setValue(this.valueAtFocus ?? 0)
				this.blur()
				return
			case 'Backspace':
			case 'Delete':
				e.preventDefault()
				this.flushEntry()
				this.setValue(0)
				return
			default:
				if (!/^\d$/.test(e.key)) return
				e.preventDefault()
				this.appendDigit(e.key)
		}
	}

	private step(direction: 1 | -1) {
		const current = Math.max(0, this.value ?? 0)
		// Snap onto the quarter grid in the direction of travel so odd values tidy up as they move.
		const base = direction > 0 ? Math.floor(current / quarter) : Math.ceil(current / quarter)
		this.setValue((base + direction) * quarter)
	}

	private appendDigit(digit: string) {
		this.entryBuffer = (this.entryBuffer + digit).slice(-2)
		this.setValue(Number(this.entryBuffer) * 60)

		clearTimeout(this.entryTimer)
		this.entryTimer = setTimeout(() => this.entryBuffer = '', digitEntryWindow)
	}

	private flushEntry() {
		clearTimeout(this.entryTimer)
		this.entryBuffer = ''
	}

	private setValue(next: number) {
		const clamped = Math.min(Math.max(0, next), this.ceiling)
		if (clamped === this.value) return

		this.value = clamped
		this.dispatchEvent(new CustomEvent<number>('preview', { detail: clamped }))
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-editable-time-unit': EditableTimeUnit
	}
}
