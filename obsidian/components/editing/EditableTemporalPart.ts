import { css, html } from "@a11d/lit"
import { EditablePart } from "./EditableDataLink"
import "../design/Tooltip"

/**
 * Base for type-specific temporal editables (date, time) built on a **native input** rather than free text —
 * so the value is entered through a real picker and can never be a malformed string.
 *
 * Idle renders a formatted, on-brand display; clicking swaps to the native `<input>` (and opens its picker).
 * Picking a value commits; blurring without a change exits; Escape reverts. The stored value stays the ISO
 * string the core already persists (`YYYY-MM-DD` for date, `HH:MM[:SS]` for time).
 *
 * Subclasses set {@link inputType} and {@link toDisplayText}.
 */
export abstract class EditableTemporalPart extends EditablePart<string> {
	protected abstract readonly inputType: 'date' | 'time' | 'datetime-local'
	protected valueAtFocus?: string

	static override get styles() {
		return css`
			${super.styles}

			:host {
				display: inline-flex;
				align-items: center;
				min-width: 3ch;
				cursor: pointer;
			}

			.display {
				white-space: nowrap;
			}

			.display.empty {
				opacity: .4;
			}

			input {
				font: inherit;
				color: inherit;
				background: transparent;
				border: none;
				outline: none;
				padding: 0;
				margin: 0;
			}

			input::-webkit-calendar-picker-indicator {
				cursor: pointer;
			}
		`
	}

	protected override get template() {
		if (this.active) {
			return html`
				<input
					type=${this.inputType}
					.value=${this.value ?? ''}
					@change=${(e: Event) => this.commit((e.target as HTMLInputElement).value)}
					@blur=${() => this.exit()}
					@keydown=${(e: KeyboardEvent) => this.onKeyDown(e)}>
			`
		}

		const display = html`
			<span class='display ${this.value ? '' : 'empty'}' @click=${() => this.beginEdit()}>
				${this.toDisplayText(this.value)}
			</span>
		`

		// A subclass whose reading is in one calendar can offer the same moment in another as a tooltip.
		const tip = this.idleTooltip
		return !tip ? display : html`<p7t-tooltip .text=${tip}>${display}</p7t-tooltip>`
	}

	/** An optional hover reading of the idle value — e.g. the date the same day in the device's default calendar. */
	protected get idleTooltip(): string {
		return ''
	}

	private beginEdit() {
		if (this.disabled) {
			return
		}

		this.valueAtFocus = this.value
		this.active = true
		void this.updateComplete.then(() => {
			const input = this.renderRoot.querySelector('input')
			if (!input) return
			input.focus()
			// Opening the picker is a nicety; it throws outside a user gesture, which is harmless here.
			try { input.showPicker?.() } catch { /* the field stays editable regardless */ }
		})
	}

	private commit(raw: string) {
		this.finishEditing(raw.length > 0 ? raw : undefined)
	}

	private exit() {
		if (this.active) {
			this.active = false
		}
	}

	private onKeyDown(e: KeyboardEvent) {
		if (e.key === 'Enter') {
			e.preventDefault()
			this.commit((e.target as HTMLInputElement).value)
			return
		}

		if (e.key === 'Escape') {
			e.preventDefault()
			this.value = this.valueAtFocus
			this.active = false
		}
	}

	protected abstract toDisplayText(value: string | undefined): string
}
