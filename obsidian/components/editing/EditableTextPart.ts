import { css, eventListener, property, PropertyValues, state } from "@a11d/lit"
import { Notice } from "obsidian"
import { EditablePart } from "./EditableDataLink"

/** How long an invalid value must sit before the warning outline appears, so it does not flash mid-keystroke. */
const warningDelay = 500

/**
 * Hardened base for the contenteditable editables (title, summary, orbit).
 *
 * It centralises what the free-text editables used to each duplicate — and fixes the reported bug where a
 * plain Enter injected a newline instead of finishing the edit:
 * - single-line (default): Enter finishes; newlines are stripped; multiline plain Enter inserts a line and
 *   Shift+Enter finishes;
 * - Escape cancels and reverts;
 * - `required` refuses an empty commit, `maxLength` caps input, `validateContent` gates syntax;
 * - an invalid value warns (delayed) while editing and, on commit, raises a Notice and reverts;
 * - a `placeholder` renders when empty so the box keeps a hit target even when void (the empty-contenteditable
 *   trap), and the caret is placed explicitly on focus.
 *
 * Subclasses provide only the value shape: {@link toDisplayText}, {@link toEditableText}, {@link parseValue},
 * and optionally {@link validateContent}.
 */
export abstract class EditableTextPart<T> extends EditablePart<T> {
	// Not `readonly`: it is flipped to 'false' while the field is disabled so an inert field cannot take the caret.
	override contentEditable = 'plaintext-only'
	override readonly spellcheck = false

	/** Refuses an empty commit (void prevention). */
	@property({ type: Boolean, reflect: true }) required = false
	/** Allows newlines: plain Enter inserts a line, Shift+Enter finishes. */
	@property({ type: Boolean, reflect: true }) multiline = false
	/** Hard cap on the number of characters. */
	@property({ type: Number }) maxLength?: number
	/** Shown, dimmed, while the field is empty; also keeps an empty field clickable. */
	@property() placeholder = ''
	/** Human name for validation messages ("Title cannot be empty."). */
	@property() label = 'Value'

	@state() protected warning = false

	protected valueAtFocus?: T
	private cancelling = false
	private warnTimer?: ReturnType<typeof setTimeout>

	static override get styles() {
		return css`
			${super.styles}

			:host {
				display: inline-block;
				min-width: 2ch;
				min-height: 1lh;
				white-space: nowrap;
				cursor: text;
			}

			:host([multiline]) {
				white-space: pre-wrap;
			}

			:host([warning]) {
				outline-color: var(--text-error, crimson);
				animation: none;
			}

			/* Rendered placeholder: gives an empty field size and a visible affordance so it stays clickable. */
			:host(:empty)::before {
				content: attr(data-placeholder);
				opacity: .4;
				font-weight: inherit;
				pointer-events: none;
			}
		`
	}

	protected override updated(changed: PropertyValues) {
		super.updated?.(changed)
		this.dataset.placeholder = this.placeholder
		this.toggleAttribute('warning', this.warning)
		// An inert field is not editable at all — it renders its read-only face and never takes the caret.
		this.contentEditable = this.disabled ? 'false' : 'plaintext-only'
		// While editing, the caret lives in this text; rewriting it would collapse the selection, so the text
		// is synced only when idle. The editing form is installed once, on focus.
		if (this.active) return
		const display = this.toDisplayText(this.value)
		if (this.textContent !== display) {
			this.textContent = display
		}
	}

	public override disconnectedCallback() {
		super.disconnectedCallback()
		clearTimeout(this.warnTimer)
	}

	@eventListener({ type: 'focus', target: this })
	protected handleFocus() {
		if (this.disabled) {
			this.blur()
			return
		}

		this.valueAtFocus = this.value
		this.beginManualEditing()
		this.textContent = this.toEditableText(this.value)
		this.placeCaretAtEnd()
	}

	@eventListener({ type: 'keydown', target: this })
	protected handleKeyDown(e: KeyboardEvent) {
		if (e.key === 'Enter') {
			// Multiline keeps plain Enter as a newline; every other case finishes the edit.
			if (this.multiline && !e.shiftKey) return
			e.preventDefault()
			this.blur()
			return
		}

		if (e.key === 'Escape') {
			e.preventDefault()
			this.cancelling = true
			this.blur()
		}
	}

	@eventListener({ type: 'input', target: this })
	protected handleInput() {
		if (!this.active) return

		const text = this.textContent ?? ''
		let sanitized = this.multiline ? text : text.replace(/[\r\n]+/g, ' ')
		if (this.maxLength !== undefined && sanitized.length > this.maxLength) {
			sanitized = sanitized.slice(0, this.maxLength)
		}

		// Only rewrite on the rare overflow/paste-newline path — normal typing leaves the caret untouched.
		if (sanitized !== text) {
			this.textContent = sanitized
			this.placeCaretAtEnd()
		}

		this.scheduleWarning()
	}

	@eventListener({ type: 'blur', target: this })
	protected handleBlur() {
		clearTimeout(this.warnTimer)
		this.warning = false

		if (this.cancelling) {
			this.cancelling = false
			this.revert()
			return
		}

		const raw = this.textContent ?? ''
		const message = this.validateMessage(raw)
		if (message !== undefined) {
			new Notice(message)
			this.revert()
			return
		}

		this.finishEditing(this.parseValue(raw))
	}

	/** Restores the value held at focus and returns to idle, without dispatching a change. */
	protected revert() {
		this.value = this.valueAtFocus
		this.active = false
		const display = this.toDisplayText(this.value)
		if (this.textContent !== display) {
			this.textContent = display
		}
	}

	private scheduleWarning() {
		clearTimeout(this.warnTimer)
		this.warnTimer = setTimeout(() => {
			this.warning = this.validateMessage(this.textContent ?? '') !== undefined
		}, warningDelay)
	}

	protected validateMessage(raw: string): string | undefined {
		const trimmed = raw.trim()
		if (this.required && trimmed.length === 0) {
			return `${this.label} cannot be empty.`
		}

		if (this.maxLength !== undefined && raw.length > this.maxLength) {
			return `${this.label} is too long (max ${this.maxLength}).`
		}

		return this.validateContent(trimmed)
	}

	private placeCaretAtEnd() {
		const selection = this.ownerDocument.getSelection()
		if (!selection) return
		const range = this.ownerDocument.createRange()
		range.selectNodeContents(this)
		range.collapse(false)
		selection.removeAllRanges()
		selection.addRange(range)
	}

	/** Idle rendering of the value. */
	protected abstract toDisplayText(value: T | undefined): string

	/** Text placed in the field when editing begins (defaults to the display form). */
	protected toEditableText(value: T | undefined): string {
		return this.toDisplayText(value)
	}

	/** Parses committed text into the stored value. */
	protected abstract parseValue(raw: string): T | undefined

	/** Subclass syntax check on already-trimmed text; return an error message or `undefined`. */
	protected validateContent(_trimmed: string): string | undefined {
		return undefined
	}
}
