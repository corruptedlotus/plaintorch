import { Component, component, css, html, nothing, property, PropertyValues, repeat, state } from '@a11d/lit'
import { ExecutiveOrder, ExecutiveOrderUpdate } from '@pleiades/sdk'
import { Notice } from 'obsidian'
import { core } from '..'
import type { EditablePart } from '../editing/EditableDataLink'

/**
 * The executive orders an onrush carries, edited in place (PEP102.5).
 *
 * The onrush detail window shows this beneath the sprint's banner, in the spirit of the directive tab's grid
 * but for one sprint's orders alone. Each row edits its own name and its effective window and can be removed;
 * a new order is added blank and named inline. Orders are not a tracked repository, so the list is fetched
 * here and re-read after each write rather than observed through the store.
 */
@component('p7t-onrush-orders')
export class OnrushOrders extends Component {
	/** The sprint whose orders these are. */
	@property() onrushId = ''

	@state() private orders: readonly ExecutiveOrder[] = []
	@state() private loading = true

	static override get styles() {
		return css`
			:host {
				display: flex;
				flex-direction: column;
				font-family: var(--font-interface);
				color: var(--text-normal);
				padding: .4em .6em 1em;
			}

			.heading {
				display: flex;
				align-items: center;
				gap: .5ch;
				font-size: .8em;
				font-weight: 600;
				text-transform: uppercase;
				letter-spacing: .04em;
				color: var(--p7t-flare-accent, var(--interactive-accent));
				margin-block: .2em .6em;

				& p7t-icon {
					width: 1.3em;
					height: 1.3em;
				}
			}

			.rows {
				display: grid;
				grid-template-columns: minmax(6em, 1fr) auto auto;
				align-items: center;
				gap: .3em .8em;
			}

			.notice {
				grid-column: 1 / -1;
				padding: .8em .2em;
				opacity: .5;
				font-weight: 300;
			}

			.title {
				text-align: start;
				justify-content: flex-start;
				font-weight: 400;
				padding-block: .3em;
			}

			.window {
				display: flex;
				align-items: center;
				gap: .4em;
				opacity: .8;
				font-size: .9em;
				white-space: nowrap;

				& p7t-icon {
					width: 1.1em;
					height: 1.1em;
					opacity: .6;
				}
			}

			.remove {
				display: flex;
				padding: .3em;
				border: none;
				border-radius: 6px;
				background: transparent;
				color: color-mix(in srgb, var(--text-normal) 45%, transparent);
				cursor: pointer;
				transition: .2s ease;

				& p7t-icon {
					width: 1.2em;
					height: 1.2em;
				}

				&:hover {
					color: var(--text-error, crimson);
					background-color: color-mix(in srgb, var(--text-error, crimson) 12%, transparent);
				}
			}

			.add {
				align-self: flex-start;
				margin-top: .8em;
			}

			.exec-row {
				display: grid;
				grid-column: 1 / -1;
				grid-template-columns: subgrid;
				background-color: color-mix(in srgb, var(--text-normal) 5%, transparent);
				border-radius: 6px;
				padding: .3em .6em;
				margin-inline: -.6em;
				align-items: center;
			}

			.summary {
				grid-column: 1 / -1;
				justify-content: flex-start;
				text-align: start;
			}
		`
	}

	protected override connected() {
		void this.refresh()
	}

	protected override updated(changed: PropertyValues) {
		if (changed.has('onrushId')) {
			void this.refresh()
		}
	}

	private async refresh() {
		if (!this.onrushId) {
			this.orders = []
			this.loading = false
			return
		}

		this.orders = await core.onrush.listExecutiveOrders(this.onrushId)
		this.loading = false
	}

	protected override get template() {
		return html`
			<div class='heading'>
				<p7t-icon icon='exec-order'></p7t-icon>
				<span>Executive Orders</span>
			</div>
			<div class='rows'>
				${this.orders.length > 0 ? nothing : html`
					<div class='notice'>${this.loading ? 'Loading…' : 'No executive orders yet.'}</div>
				`}
				${repeat(this.orders, order => order.id, order => this.rowTemplate(order))}
			</div>
			<p7t-button class='add' icon='exec-order' @click=${() => this.addOrder()}>
				<span>Add order</span>
			</p7t-button>
		`
	}

	private rowTemplate(order: ExecutiveOrder) {
		return html`
			<div class='exec-row'>
				<p7t-editable-plaintext
					class='title'
					required
					label='Order title'
					placeholder='Untitled'
					.value=${order.title}
					@change=${(e: Event) => this.saveOrder(order.id, { title: (e.target as EditablePart<string>).value ?? '' })}>
				</p7t-editable-plaintext>
				<div class='window'>
					<p7t-editable-date
						.value=${order.effectiveFrom}
						@change=${(e: Event) => this.saveOrder(order.id, { effectiveFrom: (e.target as EditablePart<string>).value })}>
					</p7t-editable-date>
					<p7t-icon icon='lucide:arrow-right'></p7t-icon>
					<p7t-editable-date
						.value=${order.effectiveUntil}
						@change=${(e: Event) => this.saveOrder(order.id, { effectiveUntil: (e.target as EditablePart<string>).value })}>
					</p7t-editable-date>
				</div>
				<button class='remove' aria-label='Remove order' @click=${() => this.removeOrder(order)}>
					<p7t-icon icon='lucide:trash-2'></p7t-icon>
				</button>
				<p7t-editable-plaintext
					class='summary'
					multiline
					placeholder='No summary'
					.value=${order.summary}
					@change=${(e: Event) => this.saveOrder(order.id, { summary: (e.target as EditablePart<string>).value ?? '' })}>
				</p7t-editable-plaintext>
			</div>
		`
	}

	private async addOrder() {
		const created = await core.onrush.issueExecutiveOrder(this.onrushId, { title: 'New order' })
		if (!created) {
			new Notice('PLAINTORCH could not add that order.')
			return
		}

		await this.refresh()
		await this.refreshOwningSprint()
	}

	private async saveOrder(orderId: string, update: ExecutiveOrderUpdate) {
		const saved = await core.onrush.updateExecutiveOrder(orderId, update)
		if (!saved) {
			new Notice('PLAINTORCH could not save that order.')
		}

		await this.refresh()
	}

	private async removeOrder(order: ExecutiveOrder) {
		const removed = await core.onrush.deleteExecutiveOrder(order.id)
		if (!removed) {
			new Notice('PLAINTORCH could not remove that order.')
			return
		}

		new Notice(`Removed ${order.title}.`)
		await this.refresh()
		await this.refreshOwningSprint()
	}

	/** Re-reads the sprints so the graph's order tally follows an add or a removal made here. */
	private async refreshOwningSprint() {
		await Promise.all([
			core.repos.onrushCurrent.refresh(),
			core.repos.onrushPlanning.refresh()
		])
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-onrush-orders': OnrushOrders
	}
}
