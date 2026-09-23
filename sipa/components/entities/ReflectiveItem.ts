import { component, html, property } from "@a11d/lit"
import { Reflective } from "@pleiades/sdk"
import { Notice } from "obsidian"
import { OccurrenceItem } from "./OccurrenceItem"
import { core } from ".."

/**
 * A single daily reflective. Its notch quick-switches the executed flag; its toplane surfaces the lunar
 * directive of the decree that generated it (absent for manual/drawn reflectives).
 *
 * The record is owned by the aggregate that rendered it, so a committed toggle is announced upward with a
 * bubbling `reflectivechange` event carrying the merged record, and the aggregate reconciles.
 */
@component('p7t-reflective-item')
export class ReflectiveItem extends OccurrenceItem {
	@property({ type: Object }) reflective?: Reflective

	protected override get heading() {
		return this.reflective?.description ?? ''
	}

	protected override get directive() {
		return this.reflective?.decree?.directive
	}

	protected override get notchTemplate() {
		return html`<p7t-icon icon=${this.reflective?.executed ? 'state-done' : 'state-zero'}></p7t-icon>`
	}

	protected override async notchAction() {
		const reflective = this.reflective
		if (!reflective) return

		const updated = await core.polaris.updateReflective(reflective.id, { executed: !reflective.executed })
		if (!updated) {
			new Notice('Failed to update reflective.')
			return
		}

		// The update response carries no decree navigation, so the known decree is kept for the directive line.
		const merged: Reflective = { ...reflective, ...updated, decree: updated.decree ?? reflective.decree }
		this.reflective = merged
		this.dispatchEvent(new CustomEvent<Reflective>('reflectivechange', { detail: merged, bubbles: true, composed: true }))
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-reflective-item': ReflectiveItem
	}
}
