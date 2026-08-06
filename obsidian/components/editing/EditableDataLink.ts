import { bindingDefaultProperty, Component, component, css, event, eventListener, html, property, PropertyValues } from "@a11d/lit"

@component('p7t-editable')
export class EditablePart<T> extends Component {
	@property({ type: Boolean, reflect: true }) protected active = false

	@bindingDefaultProperty()
	@property({ type: Object }) value?: T

	@event() edit!: EventDispatcher<T | undefined>

	editingPromise?: Promise<T | undefined>

	@eventListener({ type: 'click', target: this })
	protected handleClick() {
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
		`
	}

	protected override get template() {
		return html`<slot></slot>`
	}
}

@component('p7t-editable-plaintext')
export class EditablePlainText extends EditablePart<string> {
	override readonly contentEditable = 'plaintext-only'
	override readonly spellcheck = false

	protected override updated(_changedProperties: PropertyValues) {
		// While the field is being edited the caret lives in this text; rewriting it — even to the same string —
		// would collapse the selection and drop what the reader is typing, so it is synced only when idle.
		if (this.active) return
		this.textContent = this.value ?? ''
	}

	@eventListener({ type: 'focus', target: this })
	protected handleFocus() {
		this.beginManualEditing()
	}

	@eventListener({ type: 'blur', target: this })
	@eventListener({ type: 'keyup', target: this })
	protected handleInput(e: KeyboardEvent | unknown) {
		if (e instanceof KeyboardEvent && !(e.key === 'Enter' && e.ctrlKey)) return
		this.blur()
		this.finishEditing(this.textContent ?? '')
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-editable': EditablePart<unknown>,
		'p7t-editable-plaintext': EditablePlainText
	}
}