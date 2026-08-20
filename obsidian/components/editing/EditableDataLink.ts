import { bindingDefaultProperty, Component, component, css, event, eventListener, html, property } from "@a11d/lit"

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

declare global {
	interface HTMLElementTagNameMap {
		'p7t-editable': EditablePart<unknown>
	}
}