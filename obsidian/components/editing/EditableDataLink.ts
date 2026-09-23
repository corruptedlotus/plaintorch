import { bindingDefaultProperty, Component, component, css, event, eventListener, html, nothing, property, type HTMLTemplateResult } from "@a11d/lit"
import { IconName } from "components/PleiadesIcon"
import { defaultNullGlyph, nullGlyphStyle, nullGlyphTemplate } from "../design/nullGlyph"

/**
 * The root of every editable field — free text, numbers, dates, media, and the searching selects.
 *
 * What the whole tree shares lives here, so a derivation only says how *its* edit works:
 * - the value contract: a bindable `value`, `nullable`, `disabled`, and the `edit`/`change` pair raised by
 *   {@link finishEditing};
 * - the editing lifecycle: {@link startEditing} is the one entry point (a click on the content, or the edit
 *   button), {@link beginManualEditing} marks a field active and remembers the value it started from, and
 *   {@link cancelEditing} returns to that value without a commit;
 * - the look: the hover outline, the pulse while `active`, the inert face, the dimmed placeholder;
 * - the affordances: small buttons tethered to the field's outside ({@link affordanceTemplate}) — the clear
 *   button of a nullable field and the {@link editButton | edit button} — which a derivation extends through
 *   {@link affordancesTemplate} rather than by hand-placing buttons in its own template.
 *
 * A derivation draws itself through {@link contentTemplate}; the base composes that with the affordances.
 *
 * Used directly (`p7t-editable`), it wraps slotted content and edits through {@link doEdit} — anything that can
 * turn the current value into a promise of the next one: a modal prompt, a toggle.
 */
@component('p7t-editable')
export class EditablePart<T> extends Component {
	@property({ type: Boolean, reflect: true }) protected active = false

	@bindingDefaultProperty()
	@property({ type: Object }) value?: T

	/**
	 * Whether this field can be cleared to no value (undefined/null). It turns on the null affordances: a clear
	 * button while editing, and the null glyph when the value is absent. Off by default, so non-nullable fields
	 * are unchanged.
	 */
	@property({ type: Boolean, reflect: true }) nullable = false

	/**
	 * Holds the field inert: it still draws its read-only face but refuses to enter editing. A composite editable
	 * (e.g. {@link EditableSchedule}) uses this to embed the plain field's face while it is not itself in
	 * editing mode, then lifts it to let the field be edited. Off by default, so a standalone field is unchanged.
	 */
	@property({ type: Boolean, reflect: true }) disabled = false

	/**
	 * Moves the way into editing off the content and onto a small edit button that appears beside the field on
	 * hover. With it on, pressing the content does nothing — it neither starts an edit nor takes focus — so the
	 * content is free to be something else: a link, a drag handle, selectable text. Keyboard focus still edits.
	 */
	@property({ type: Boolean, reflect: true }) editButton = false

	/** The glyph drawn for an absent value; overridable, or replaced wholesale via the `null` slot. */
	@property() nullGlyph: IconName = defaultNullGlyph

	@event() edit!: EventDispatcher<T | undefined>

	editingPromise?: Promise<T | undefined>

	/** The value the current edit started from — what {@link cancelEditing} returns to. */
	protected valueAtFocus?: T

	/** Whether the value is absent. */
	protected get isNull() {
		return this.value === undefined || this.value === null
	}

	@eventListener({ type: 'click', target: this })
	protected handleClick() {
		if (this.editButton) {
			return
		}

		this.startEditing()
	}

	/** With the edit button on, a press on the content must not focus the field either — focus is how most fields begin an edit. */
	@eventListener({ type: 'mousedown', target: this })
	protected handleMouseDown(e: MouseEvent) {
		if (this.editButton && !this.active) {
			e.preventDefault()
		}
	}

	/**
	 * Begins an edit, however this kind of field edits. The base asks {@link doEdit}; a field that edits in place
	 * overrides this to focus itself, swap to its input, open its search.
	 */
	public startEditing() {
		if (this.disabled || this.active) {
			return
		}

		this.beginEditing()
	}

	/** Marks the field as being edited in place and remembers the value to return to. Refused while disabled. */
	protected beginManualEditing(): boolean {
		if (this.disabled) {
			return false
		}

		this.valueAtFocus = this.value
		this.active = true
		return true
	}

	protected beginEditing() {
		this.editingPromise = this.doEdit(this.value)
		if (!!this.editingPromise) {
			this.active = true
			this.editingPromise.then(
				newValue => this.finishEditing(newValue),
				() => this.active = false,
			)
		}
	}

	protected finishEditing(value: T | undefined) {
		this.value = value
		// The committed value is the new baseline, so a blur that follows a commit finds nothing left to publish.
		this.valueAtFocus = value
		this.edit.dispatch(value)
		this.active = false
		this.dispatchEvent(new Event('change'))
	}

	/** Ends the edit without a commit: the value returns to what it was and nothing is dispatched. */
	protected cancelEditing() {
		this.value = this.valueAtFocus
		this.active = false
	}

	/** Clears the field to no value and finishes editing — the null commit behind the clear affordance. */
	clear() {
		this.finishEditing(undefined)
	}

	/** The null indicator: the null glyph, or whatever a consumer slots into `null` as a placeholder/fallback. */
	protected get nullDisplayTemplate() {
		return nullGlyphTemplate(this.nullGlyph)
	}

	/**
	 * One affordance button. A press on it never takes focus — the field being edited keeps its caret — and its
	 * click stays its own, not also a click on the content.
	 */
	protected affordanceTemplate(name: string, icon: IconName, title: string, action: () => void) {
		return html`
			<p7t-icon
				class='affordance ${name}'
				icon=${icon}
				title=${title}
				@mousedown=${(e: Event) => e.preventDefault()}
				@click=${(e: Event) => { e.stopPropagation(); action() }}>
			</p7t-icon>
		`
	}

	/** A clear affordance, shown only while editing a nullable field. */
	protected get clearButtonTemplate() {
		return !this.nullable || !this.active ? nothing : this.affordanceTemplate('clear', 'lucide:x', 'Clear', () => this.clear())
	}

	/** The edit affordance of {@link editButton}, offered while the field is idle. */
	protected get editButtonTemplate() {
		return !this.editButton || this.disabled || this.active ? nothing : this.affordanceTemplate('edit', 'lucide:pencil', 'Edit', () => this.startEditing())
	}

	doEdit = (value: T | undefined): Promise<T | undefined> | undefined => undefined

	static override get styles() {
		return css`
			:host {
				display: flex;
				align-items: center;
				justify-content: center;
				outline: 1px solid transparent;
				outline-offset: .16rem;
				border-radius: 4px;
				transition: .3s ease;
				anchor-name: --editable-anchor;
			}

			slot {
				/*display: inline;*/
				user-select: auto;
			}

			[hidden] {
				display: none !important;
			}

			@keyframes pulse {
				from { outline-color: var(--p7t-flare-accent, var(--interactive-accent)); }
				to { outline-color: var(--text-normal); }
			}

			:host(:hover), :host(:focus) {
				outline-color: var(--p7t-flare-accent, var(--interactive-accent));
			}

			:host([active]) {
				animation: pulse .7s ease-in-out infinite alternate;
			}

			/* An inert field reads as plain text: no editability outline, no edit cursor. */
			:host([disabled]), :host([editbutton]:not([active])) {
				cursor: default;
			}

			:host([disabled]:hover) {
				outline-color: transparent;
			}

			/* What a field shows in place of a value it does not have. */
			.placeholder {
				opacity: .4;
			}

			${nullGlyphStyle}

			.overlay {
				flex: 0 0 0;
				width: 0;
				height: 0;
			}

			/* Affordances tether to the field's outside, so they take no room in it and never shift its size. */
			.affordance {
				position: fixed;
				position-anchor: --editable-anchor;
				border-radius: 6px;
				font-size: 1rem;
				width: .9em;
				height: .9em;
				margin-inline: .2em;
				cursor: pointer;
				background-color: black;
				transition: all .2s ease, inset none;
				z-index: 9;
			}

			.clear {
				anchor-try: normal flip-block;
				inset-inline-end: anchor(start);
				top: anchor(top);
				margin: .1em;

				&:hover {
					color: var(--text-error, crimson);
				}
			}

			/* Flush against the field, so the pointer crosses from one to the other without losing the hover. */
			.edit {
				anchor-try: normal flip-inline;
				inset-inline-start: anchor(end);
				top: anchor(top);
				padding: .1em;
				opacity: 0;
				pointer-events: none;

				:host(:hover) & {
					opacity: .7;
					pointer-events: auto;
				}

				&:hover {
					opacity: 1;
					color: var(--p7t-flare-accent, var(--interactive-accent));
				}
			}
		`
	}

	/** The field itself. The base wraps whatever is slotted into it. */
	protected get contentTemplate(): HTMLTemplateResult | typeof nothing {
		return html`<slot></slot>`
	}

	/** The buttons tethered around the field. A derivation with more of them (steppers) adds to this. */
	protected get affordancesTemplate(): HTMLTemplateResult | typeof nothing {
		return html`${this.editButtonTemplate}${this.clearButtonTemplate}`
	}

	protected override get template() {
		return html`${this.contentTemplate}<div class='overlay'>${this.affordancesTemplate}</div>`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-editable': EditablePart<unknown>
	}
}
