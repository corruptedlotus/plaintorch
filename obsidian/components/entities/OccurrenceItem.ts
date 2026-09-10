import { Component, component, css, html, HTMLTemplateResult, nothing, property } from "@a11d/lit"
import type { Directive } from "@pleiades/sdk"
import { itemLayoutStyles } from "./itemStyles"

/**
 * List-row base for the non-PUCK occurrence records — reflectives, attentives, eventives.
 *
 * These are numeric-keyed rows served inside an aggregate (a Polaris cycle, the agenda), not PUCK-tracked
 * entities, so they cannot ride on {@link EntityItem} (which resolves and watches by PUCK identity). This
 * reproduces the same notch + toplane + title grid — via the shared {@link itemLayoutStyles} — and exposes
 * the small set of hooks each occurrence overrides: its heading, its relevant directive, its notch, and
 * what the notch does.
 */
@component('p7t-occurrence-item')
export class OccurrenceItem extends Component {
	@property({ type: Boolean, reflect: true }) interactive = false

	get disabled() { return false }

	static override get styles() {
		return css`
			${itemLayoutStyles}

			.toplane {
				font-size: .9em;
			}
		`
	}

	protected override get template() {
		return html`
			<div class='grid ${this.disabled ? 'disabled' : ''}'>
				<div @click=${async () => await this.notchAction()} class='notch part'>${this.notchTemplate}</div>
				<div class='toplane'>
					${this.preTitle}
					<div class='filler'></div>
					${this.info}
				</div>
				<div class='title'>
					<span @click=${() => this.navigate()}>${this.heading}</span>
				</div>
			</div>
		`
	}

	/** The row's primary text. */
	protected get heading(): string { return '' }

	/** The relevant directive to surface in the toplane, if any. */
	protected get directive(): Directive | undefined { return undefined }

	protected get preTitle(): HTMLTemplateResult | typeof nothing {
		const directive = this.directive
		// An occurrence with no directive shows nothing here (not the objective's "World Quest" placeholder).
		if (!directive) return nothing
		return html`<p7t-directive-item .directive=${directive}></p7t-directive-item>`
	}

	protected get info(): HTMLTemplateResult | typeof nothing { return nothing }

	protected get notchTemplate(): HTMLTemplateResult {
		return html`<p7t-icon icon='state-active'></p7t-icon>`
	}

	protected async notchAction(): Promise<void> { }

	protected navigate(): void { }
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-occurrence-item': OccurrenceItem
	}
}
