import { bindingDefaultProperty, Component, component, css, event, eventListener, html, nothing, property } from "@a11d/lit"
import { IconName } from "components/PleiadesIcon"
import { defaultNullGlyph, nullGlyphStyle, nullGlyphTemplate } from "../design/nullGlyph"

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
	 * (e.g. {@link EditableOrbitDatetime}) uses this to embed the plain field's face while it is not itself in
	 * editing mode, then lifts it to let the field be edited. Off by default, so a standalone field is unchanged.
	 */
	@property({ type: Boolean, reflect: true }) disabled = false

	/** The glyph drawn for an absent value; overridable, or replaced wholesale via the `null` slot. */
	@property() nullGlyph: IconName = defaultNullGlyph

	@event() edit!: EventDispatcher<T | undefined>

	editingPromise?: Promise<T | undefined>

	@eventListener({ type: 'click', target: this })
	protected handleClick() {
		if (this.disabled) {
			return
		}

		this.beginEditing()
	}

	protected beginManualEditing() {
		this.active = true
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
		this.edit.dispatch(value)
		this.active = false
		this.dispatchEvent(new Event('change'))
	}

	/** Clears the field to no value and finishes editing — the null commit behind the clear affordance. */
	clear() {
		this.finishEditing(undefined)
	}

	/** The null indicator: the null glyph, or whatever a consumer slots into `null` as a placeholder/fallback. */
	protected get nullDisplayTemplate() {
		return nullGlyphTemplate(this.nullGlyph)
	}

	/** A clear affordance, shown only while editing a nullable field. */
	protected get clearButtonTemplate() {
		if (!this.nullable || !this.active) {
			return nothing
		}

		return html`
			<p7t-icon
				class='clear'
				icon='lucide:x'
				title='Clear'
				@mousedown=${(e: Event) => e.preventDefault()}
				@click=${() => this.clear()}>
			</p7t-icon>
		`
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
			}

			slot {
				/*display: inline;*/
				user-select: auto;
			}

			@keyframes pulse {
				from { outline-color: var(--p7t-flare-accent, var(--interactive-accent)); }
				to { outline-color: var(--text-normal); }
			}

			:host(:hover) {
				outline-color: var(--p7t-flare-accent, var(--interactive-accent));
			}

			:host([active]) {
				animation: pulse .7s ease-in-out infinite alternate;
			}

			/* An inert field reads as plain text: no editability outline, no edit cursor. */
			:host([disabled]) {
				cursor: default;
			}

			:host([disabled]:hover) {
				outline-color: transparent;
			}

			${nullGlyphStyle}

			.clear {
				width: .9em;
				height: .9em;
				cursor: pointer;
				opacity: .55;
			}

			.clear:hover {
				opacity: 1;
				color: var(--text-error, crimson);
			}
		`
	}

	protected override get template() {
		return html`<slot></slot>`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-editable': EditablePart<unknown>
	}
}