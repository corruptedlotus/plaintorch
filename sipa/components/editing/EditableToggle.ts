import { component, css, eventListener, html, property } from "@a11d/lit"
import { IconName } from "../PleiadesIcon"
import { EditablePart } from "./EditableDataLink"
import '../design/IconItem'

/**
 * An editable on/off switch (PEP100 patch 2): a boolean field that commits the moment it is pressed.
 *
 * It keeps the whole {@link EditablePart} contract — a bindable `value`, `disabled`, and the `edit`/`change` pair
 * raised by `finishEditing` — so `${binder.bind('field')}` and a plain `@change` handler work unchanged. An
 * undefined value reads as off.
 *
 * - A click, or Space/Enter while focused, flips the value and commits at once: there is no editing phase, no
 *   prompt and no pulse.
 * - It is focusable (`tabIndex = 0`) and exposes itself as an ARIA `switch` with `aria-checked`, and reflects a
 *   `checked` attribute for styling (`p7t-editable-toggle[checked]`). A `tabindex` or `role` set by the consumer
 *   wins over those defaults.
 * - Its face is a `<p7t-icon-item>` drawn from {@link onIcon}/{@link onText} or {@link offIcon}/{@link offText}. A
 *   caller that wants a different face slots it into the named `on` and `off` slots; only the slot for the current
 *   state is rendered.
 */
@component('p7t-editable-toggle')
export class EditableToggle extends EditablePart<boolean> {
	/** The glyph drawn while the toggle is on. */
	@property() onIcon: IconName = 'lucide:toggle-right'

	/** The glyph drawn while the toggle is off (or unset). */
	@property() offIcon: IconName = 'lucide:toggle-left'

	/** The label drawn while the toggle is on. */
	@property() onText = 'On'

	/** The label drawn while the toggle is off (or unset). */
	@property() offText = 'Off'

	/** Whether the toggle currently reads as on; an undefined or null value reads as off. */
	get isOn() {
		return this.value === true
	}

	static override get styles() {
		return css`
			${super.styles}

			:host {
				display: inline-flex;
				cursor: pointer;
				user-select: none;
			}
		`
	}

	/**
	 * Focusability and the switch role are applied on connect rather than as constructor-time fields: an element that
	 * gains attributes in its constructor cannot be made by `document.createElement`, only by a template. They are
	 * defaults only: a `tabindex` or `role` the consumer already set (say `tabindex='-1'`) is kept, on the first connect
	 * and after any re-parenting.
	 */
	protected override connected() {
		if (!this.hasAttribute('tabindex')) {
			this.tabIndex = 0
		}

		if (!this.hasAttribute('role')) {
			this.setAttribute('role', 'switch')
		}
	}

	/** Mirrors the state onto the host after each render: the `checked` attribute, `aria-checked` and `aria-disabled`. */
	protected override updated() {
		this.toggleAttribute('checked', this.isOn)
		this.setAttribute('aria-checked', String(this.isOn))
		if (this.disabled) {
			this.setAttribute('aria-disabled', 'true')
		}
		else {
			this.removeAttribute('aria-disabled')
		}
	}

	/** A toggle has no editing phase: pressing it flips the value and commits immediately. */
	public override startEditing() {
		if (this.disabled) {
			return
		}

		this.finishEditing(!this.isOn)
	}

	/** Space or Enter flips the toggle like a click; a held key's auto-repeat does not flip it again. */
	@eventListener({ type: 'keydown', target: this })
	protected handleKeyDown(e: KeyboardEvent) {
		if (e.key !== ' ' && e.key !== 'Enter') {
			return
		}

		e.preventDefault()
		if (!e.repeat) {
			this.startEditing()
		}
	}

	/** The face for the current state: the caller's `on`/`off` slot when given, else the built-in icon item. */
	protected override get contentTemplate() {
		const on = this.isOn
		return html`
			<slot name=${on ? 'on' : 'off'}>
				<p7t-icon-item .icon=${on ? this.onIcon : this.offIcon}>${on ? this.onText : this.offText}</p7t-icon-item>
			</slot>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-editable-toggle': EditableToggle
	}
}
