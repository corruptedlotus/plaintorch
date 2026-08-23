import { component, css, html, nothing } from "@a11d/lit"
import { ExecutiveOrder, ExecutiveOrderUpdate } from "@pleiades/sdk"
import { Notice } from "obsidian"
import { core, IconName } from ".."
import { EntityBanner } from './EntityBanner'
import type { EditablePart } from "../editing/EditableDataLink"

/**
 * Banner for an executive order (PEP102.5): a constraint or direction shaping how an onrush is moved through.
 *
 * Executive orders have no registered entity repository, so — unlike the other banners — this one renders from
 * the entity the note resolution hands it and persists edits straight through the onrush SDK, folding the
 * response back over the local entity for immediate reflection. The flare accent is overridden to the same warn
 * yellow the briefing hero draws active orders in, so the surfaces read as one.
 */
@component('p7t-executive-order-banner')
export class ExecutiveOrderBanner extends EntityBanner<ExecutiveOrder> {
	override icon: IconName = 'exec-order'

	static override get styles() {
		return css`
			${super.styles}

			:host {
				padding-inline: 1.2em;
				/* Matches the briefing hero's active-order colour, so an order reads the same wherever it appears. */
				--p7t-flare-accent: #ffd23f;
			}

			:host::part(sub-heading) {
				font-weight: 300;
				font-size: .9em;
				margin-top: -.2em;
				opacity: 1;
			}

			.summary {
				font-weight: 300;
				opacity: .9;
			}

			.window {
				display: flex;
				align-items: center;
				gap: .5em;
				font-weight: 300;
				opacity: .85;

				& .label {
					font-size: .7em;
					text-transform: uppercase;
					letter-spacing: .08em;
					opacity: .7;
				}

				& p7t-icon {
					width: 1em;
					height: 1em;
					opacity: .5;
				}
			}

			.bound {
				font-size: .85em;
				opacity: .6;
			}
		`
	}

	private get onrushBound(): boolean {
		const order = this.entity
		return !!order && !order.effectiveFrom && !order.effectiveUntil
	}

	protected override get preHeadingTemplate() {
		return html`<span>Executive Order ${this.entity?.id ?? ''}</span>`
	}

	protected override get headingTemplate() {
		return html`
			<p7t-editable-plaintext required label='Title' placeholder='Untitled' .value=${this.entity?.title}
				@change=${(e: Event) => void this.save({ title: (e.target as EditablePart<string>).value })}>
			</p7t-editable-plaintext>
		`
	}

	protected override get secondary() {
		return html`
			<p7t-editable-plaintext multiline class='summary' placeholder='No summary' .value=${this.entity?.summary}
				@change=${(e: Event) => void this.save({ summary: (e.target as EditablePart<string>).value })}>
			</p7t-editable-plaintext>
		`
	}

	protected override get info() {
		return html`
			<div class='window'>
				<span class='label'>Effective</span>
				<p7t-editable-date .value=${this.entity?.effectiveFrom}
					@change=${(e: Event) => void this.save({ effectiveFrom: (e.target as EditablePart<string>).value })}>
				</p7t-editable-date>
				<p7t-icon icon='lucide:arrow-right'></p7t-icon>
				<p7t-editable-date .value=${this.entity?.effectiveUntil}
					@change=${(e: Event) => void this.save({ effectiveUntil: (e.target as EditablePart<string>).value })}>
				</p7t-editable-date>
			</div>
			${!this.onrushBound ? nothing : html`<span class='bound'>Onrush-bound — in effect while its onrush runs</span>`}
		`
	}

	/** Persists a field edit and folds the response back over the local entity (there is no repo to observe here). */
	private async save(update: ExecutiveOrderUpdate) {
		const order = this.entity
		if (!order) {
			return
		}

		const updated = await core.onrush.updateExecutiveOrder(order.id, update)
		if (!updated) {
			new Notice('PLAINTORCH could not save that change.')
			return
		}

		this.entity = updated
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-executive-order-banner': ExecutiveOrderBanner
	}
}
