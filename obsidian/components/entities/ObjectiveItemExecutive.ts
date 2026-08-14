import { component, css, html, property } from "@a11d/lit"
import { ObjectiveItem } from "./ObjectiveItem"
import { Executive } from "@pleiades/sdk"
import { ExecutiveModal, getApp, resolveMediaIcon } from ".."

@component('p7t-objective-item-exec')
export class ObjectiveItemExecutive extends ObjectiveItem {
	
	@property({
		updated(this: ObjectiveItemExecutive, value: Executive | undefined) {
			this.entity = value?.objective
		}
	}) executive?: Executive

	static override get styles() {
		return css`
			${super.styles}

			p7t-time-unit {
				font-size: 1.5em;
				font-weight: 400;
				margin-block: .2em .05em;
				align-self: center;
			}

			.affinity-info {
				display: flex;
				align-items: center;

				& p7t-icon {
					width: 20px;
					height: 20px;
				}
			}
		`
	}

	override get disabled() {
		return !!this.executive?.executed
	}

	/**
	 * An executive affined to a timeframe shows that timeframe's icon where an objective would show its Celestron
	 * value (PEP100 patch). With no affinity the Celestron reading is kept, so nothing is lost when a timeframe is
	 * not in play.
	 */
	protected override get info() {
		const timeframe = this.executive?.affinityTimeframe
		if (!timeframe) {
			return super.info
		}

		const icon = resolveMediaIcon(timeframe.iconMedia, getApp(), 'lucide:clock')
		return html`
			<div class='affinity-info' title=${timeframe.title}>
				<p7t-icon .icon=${icon}></p7t-icon>
			</div>
		`
	}

	protected override get extraActionTemplate() {
		return undefined
	}

	protected override async notchAction() {
		if (!this.executive) return
		new ExecutiveModal(getApp(), this.executive, executive => {
			this.executive = executive
			this.dispatchEvent(new CustomEvent<void>('updateRequest', { bubbles: true, composed: true }))
		}).open()
	}

	protected override get notchTemplate() {
		// if done, inherit super, otherwise show the time still left against the estimation
		return this.executive?.executed ? html`
			<p7t-icon icon='state-done'></p7t-icon>
		` : (!this.executive?.estimation ? html`
				<p7t-icon icon='state-zero'></p7t-icon>
			` : (this.executive.elapsed < this.executive.estimation ? html`
					<p7t-time-unit .value=${this.executive.estimation - this.executive.elapsed}></p7t-time-unit>
				` : (this.executive.elapsed < (this.executive.maximum ?? 999999) ? html`
					<p7t-icon icon='state-active'></p7t-icon>
				` : html`
					<p7t-icon icon='state-warn'></p7t-icon>
				`)
			)
		)
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-objective-item-exec': ObjectiveItemExecutive
	}
}
