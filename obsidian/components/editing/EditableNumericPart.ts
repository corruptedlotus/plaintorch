import { css, eventListener, html, nothing, property } from "@a11d/lit"
import { EditablePart } from "./EditableDataLink"

/** How long consecutive digits keep accumulating into the same entry. */
const digitEntryWindow = 900

/**
 * Base for keyboard-driven numeric editables (time-unit, starfire).
 *
 * The field is focusable rather than contenteditable: **Arrow up/right and down/left step** the value,
 * typed digits accumulate into an entry, Enter finishes, Escape reverts, and Backspace/Delete clears to the
 * floor. Every change raises `preview` for live surfaces; the committed value is published through the
 * inherited `edit`/`change` once editing ends. Steppers on the side mirror the arrow keys.
 *
 * Subclasses supply {@link applyDigits} (how typed digits map to a value), their bounds/step, and rendering.
 */
export abstract class EditableNumericPart extends EditablePart<number> {
	override readonly tabIndex = 0

	@property({ type: Number }) min = 0
	@property({ type: Number }) max = Number.POSITIVE_INFINITY
	@property({ type: Number }) step = 1

	protected valueAtFocus?: number
	/** How many typed digits are retained (time-unit keeps 2 hour digits; starfire keeps more). */
	protected digitLimit = 6

	private entryBuffer = ''
	private entryTimer?: ReturnType<typeof setTimeout>

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
			}

			:host(:focus) {
				outline-color: var(--p7t-flare-accent, var(--interactive-accent));
			}

			/*
			 * The steppers overlay the outer side rather than sitting in flow, so an idle unit reserves no space
			 * for them and its projected size never shifts on entering editing.
			 */
			.stepper {
				position: fixed;
				position-anchor: --stepper-anchor;
				anchor-try: normal flip-inline;
				inset-inline-start: anchor(end);
				display: flex;
				flex-direction: column;
				justify-content: space-between;
				align-items: center;
				margin-block: -.2rem;
				margin-inline: .2rem;
				opacity: 0;
				transition: all .2s ease, inset none;
				pointer-events: none;
				color: var(--background-primary);
				font-size: 1rem;
				gap: .25em;
				z-index: 99;
			}

			:host([active]) .stepper {
				opacity: 1;
				scale: 1;
				pointer-events: auto;

				& > * {
					background-color: var(--p7t-flare-accent, var(--interactive-accent));
					border-radius: 12px;
					transition: .2s ease;

					&:hover {
						background-color: var(--text-muted);
					}
				}
			}

		`
	}

	protected stepperTemplate() {
		return html`
			<div class='stepper'>
				<p7t-icon
					class='increment'
					icon='lucide:chevron-up'
					@mousedown=${(e: Event) => e.preventDefault()}
					@click=${() => this.stepValue(1)}>
				</p7t-icon>
				<p7t-icon
					class='decrement'
					icon='lucide:chevron-down'
					@mousedown=${(e: Event) => e.preventDefault()}
					@click=${() => this.stepValue(-1)}>
				</p7t-icon>
			</div>
			${!this.nullable ? nothing : this.clearButtonTemplate}
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
				this.stepValue(1)
				return
			case 'ArrowDown':
			case 'ArrowLeft':
				e.preventDefault()
				this.flushEntry()
				this.stepValue(-1)
				return
			case 'Enter':
				e.preventDefault()
				this.blur()
				return
			case 'Escape':
				e.preventDefault()
				this.flushEntry()
				if (this.value !== this.valueAtFocus) {
					this.value = this.valueAtFocus
					this.dispatchEvent(new CustomEvent<number>('preview', { detail: this.value ?? this.min }))
				}
				this.blur()
				return
			case 'Backspace':
			case 'Delete':
				e.preventDefault()
				this.flushEntry()
				// A nullable field empties to no value; otherwise it floors, as a numeric field always has a value.
				if (this.nullable) {
					this.setNull()
				}
				else {
					this.setValue(this.min)
				}
				return
			default:
				if (!/^\d$/.test(e.key)) return
				e.preventDefault()
				this.appendDigit(e.key)
		}
	}

	/** Advances the value one step in the given direction. Override for grid snapping. */
	protected stepValue(direction: 1 | -1) {
		this.setValue((this.value ?? this.min) + direction * this.step)
	}

	protected setValue(next: number) {
		const clamped = Math.min(Math.max(this.min, next), this.max)
		if (clamped === this.value) return

		this.value = clamped
		this.dispatchEvent(new CustomEvent<number>('preview', { detail: clamped }))
	}

	/** Empties a nullable field to no value, previewing the floor so live surfaces have a number to draw. */
	protected setNull() {
		if (this.value === undefined) return

		this.value = undefined
		this.dispatchEvent(new CustomEvent<number>('preview', { detail: this.min }))
	}

	protected flushEntry() {
		clearTimeout(this.entryTimer)
		this.entryBuffer = ''
	}

	private appendDigit(digit: string) {
		this.entryBuffer = (this.entryBuffer + digit).slice(-this.digitLimit)
		this.setValue(this.applyDigits(this.entryBuffer))

		clearTimeout(this.entryTimer)
		this.entryTimer = setTimeout(() => this.entryBuffer = '', digitEntryWindow)
	}

	/** Maps the accumulated digit buffer to a value (time-unit: `hours * 60`; starfire: the number itself). */
	protected abstract applyDigits(buffer: string): number
}
