import { css, eventListener, property, PropertyValues, state } from "@a11d/lit"
import { EditablePart } from "./EditableDataLink"
import { toast } from "../../host"

/** How long an invalid value must sit before the warning outline appears, so it does not flash mid-keystroke. */
const warningDelay = 500

/**
 * A zero-width space that holds the caret in a field being edited with no text (see {@link EditableTextPart}). It is never
 * part of the value: every read of the text being edited strips it.
 */
const caretAnchor = '​'

/**
 * Hardened base for the contenteditable editables (title, summary, orbit).
 *
 * It centralises what the free-text editables used to each duplicate — and fixes the reported bug where a
 * plain Enter injected a newline instead of finishing the edit:
 * - single-line (default): Enter finishes; newlines are stripped; multiline plain Enter inserts a line and
 *   Shift+Enter finishes;
 * - Escape cancels and reverts;
 * - `required` refuses an empty commit, `maxLength` caps input, `validateContent` gates syntax;
 * - an invalid value warns (delayed) while editing and, on commit, raises a warning toast and reverts;
 * - a `placeholder` renders when empty so the box keeps a hit target even when void (the empty-contenteditable
 *   trap), and the caret is placed explicitly on focus.
 * - a field being edited with no text — one that starts empty, or one emptied mid-edit — holds the caret on a
 *   zero-width space. The field is a shadow host whose text is slotted, so with no light-DOM text there is no place
 *   for a caret in it: Chromium puts the selection inside the shadow tree instead, and keystrokes are lost until the
 *   field is refocused. The space gives the caret a text node to sit in; it is stripped from every read.
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

			/*
			 * Rendered placeholder: gives an empty field size and a visible affordance so it stays clickable. While an
			 * empty field is edited it holds the caret anchor, so it is not :empty; [blank] marks it instead.
			 */
			:host(:empty)::before,
			:host([blank])::before {
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
		// An inert field is not editable at all — it renders its read-only face and never takes the caret. Assigned only
		// when it changes: an update mid-edit (the warning outline) must not reset the editing host under the caret.
		const editable = this.disabled ? 'false' : 'plaintext-only'
		if (this.contentEditable !== editable) {
			this.contentEditable = editable
		}
		// While editing, the caret lives in this text; rewriting it would collapse the selection, so the text
		// is synced only when idle. The editing form is installed once, on focus.
		if (this.active) return
		this.removeAttribute('blank')
		const display = this.toDisplayText(this.value)
		if (this.textContent !== display) {
			this.textContent = display
		}
	}

	public override disconnectedCallback() {
		super.disconnectedCallback()
		clearTimeout(this.warnTimer)
	}

	/** A text field edits by holding the caret. */
	public override startEditing() {
		this.active = true
		this.focus()
	}

	/** Clearing empties the text and commits it like any other edit, so `required` still has its say. */
	public override clear() {
		this.textContent = ''
		this.blur()
	}

	@eventListener({ type: 'focus', target: this })
	protected handleFocus() {
		if (!this.active && this.editButton) {
			this.blur()
			return
		}

		if (!this.beginManualEditing()) {
			this.blur()
			return
		}

		this.setEditingText(this.toEditableText(this.value))
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
	protected handleInput(e: InputEvent) {
		if (!this.active) return

		const text = this.editingText
		let sanitized = this.multiline ? text : text.replace(/[\r\n]+/g, ' ')
		if (this.maxLength !== undefined && sanitized.length > this.maxLength) {
			sanitized = sanitized.slice(0, this.maxLength)
		}

		// Only rewrite on the rare overflow/paste-newline path — normal typing leaves the caret untouched — and around
		// the caret anchor (never mid-composition, which a rewrite would break): once on the first keystroke into an
		// empty field, to let go of the anchor, and whenever an edit empties the field. Emptied, the field holds only
		// the <br> Chromium leaves, and the caret is no longer in its text; some Chromium builds then type nowhere until
		// the field is refocused, so the anchor takes the caret back at once.
		const raw = this.textContent ?? ''
		const composing = e.isComposing
		const anchored = text !== '' && raw !== text && !composing
		const emptied = sanitized === '' && raw !== caretAnchor && !composing
		if (sanitized !== text || anchored || emptied) {
			this.setEditingText(sanitized)
		}

		this.toggleAttribute('blank', sanitized === '')
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

		const raw = this.editingText
		const message = this.validateMessage(raw)
		if (message !== undefined) {
			toast(message, 'warning')
			this.revert()
			return
		}

		this.finishEditing(this.parseValue(raw))
	}

	/** Restores the value held at focus and returns to idle, without dispatching a change. */
	protected revert() {
		this.cancelEditing()
		const display = this.toDisplayText(this.value)
		if (this.textContent !== display) {
			this.textContent = display
		}
	}

	private scheduleWarning() {
		clearTimeout(this.warnTimer)
		this.warnTimer = setTimeout(() => {
			this.warning = this.validateMessage(this.editingText) !== undefined
		}, warningDelay)
	}

	/** The text being edited, without the caret anchor an empty field holds. */
	protected get editingText(): string {
		return (this.textContent ?? '').replaceAll(caretAnchor, '')
	}

	/** Puts text in the field being edited with the caret at its end; with none, the caret anchor holds the caret. */
	private setEditingText(text: string) {
		this.textContent = text || caretAnchor
		this.toggleAttribute('blank', text === '')
		this.placeCaretAtEnd()
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

	/**
	 * Puts the caret at the end of the text, inside its last text node. A position on the host itself is not enough: it
	 * maps to the shadow tree when the host holds no text, so the caret is placed in the slotted text explicitly.
	 */
	private placeCaretAtEnd() {
		const selection = this.ownerDocument.getSelection()
		if (!selection) return
		const range = this.ownerDocument.createRange()
		const last = this.lastChild
		if (last instanceof Text) {
			range.setStart(last, last.length)
		}
		else {
			range.selectNodeContents(this)
			range.collapse(false)
		}

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
