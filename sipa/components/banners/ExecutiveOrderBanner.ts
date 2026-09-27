import { component, css, html, nothing } from "@a11d/lit"
import { ExecutiveOrder, ExecutiveOrderUpdate } from "@pleiades/sdk"
import { core, IconName, ReactiveBinder } from ".."
import { EntityBanner } from './EntityBanner'

/**
 * Banner for an executive order (PEP102.5): a constraint or direction shaping how an onrush is moved through.
 *
 * Repo-backed like the other banners (it resolves and observes its order through the executive-order repository),
 * so an edit made here propagates everywhere the order is shown. The flare accent is the same warn yellow the
 * briefing hero draws active orders in, so the surfaces read as one.
 */
@component('p7t-executive-order-banner')
export class ExecutiveOrderBanner extends EntityBanner<ExecutiveOrder> {
	override icon: IconName = 'exec-order'

	protected override readonly entityTypeName = 'ExecutiveOrder' as const

	protected binder = new ReactiveBinder<ExecutiveOrder>(this, 'entity', {
		sourceUpdate: () => this.beginEntityEdit(),
		sourceUpdated: async (_, keyPath) => {
			const entity = this.entity!
			// Summary and the effective dates are nullable: a cleared field commits as null (a clear), not a no-op.
			const update: ExecutiveOrderUpdate = {}
			switch (keyPath) {
				case 'title': update.title = entity.title; break
				case 'summary': update.summary = entity.summary ?? null; break
				case 'effectiveFrom': update.effectiveFrom = entity.effectiveFrom ?? null; break
				case 'effectiveUntil': update.effectiveUntil = entity.effectiveUntil ?? null; break
				default: return
			}

			await this.commitEntityEdit(async () => await core.onrush.updateExecutiveOrder(entity.id, update))
		}
	})

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
			<p7t-editable-plaintext required label='Title' placeholder='Untitled' ${this.binder.bind('title')}></p7t-editable-plaintext>
		`
	}

	protected override get secondary() {
		return html`
			<p7t-editable-plaintext multiline class='summary' placeholder='No summary' ${this.binder.bind('summary')}></p7t-editable-plaintext>
		`
	}

	protected override get info() {
		return html`
			<div class='window'>
				<span class='label'>Effective</span>
				<p7t-editable-date ${this.binder.bind('effectiveFrom')}></p7t-editable-date>
				<p7t-icon icon='lucide:arrow-right'></p7t-icon>
				<p7t-editable-date ${this.binder.bind('effectiveUntil')}></p7t-editable-date>
			</div>
			${!this.onrushBound ? nothing : html`<span class='bound'>Onrush-bound — in effect while its onrush runs</span>`}
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-executive-order-banner': ExecutiveOrderBanner
	}
}
