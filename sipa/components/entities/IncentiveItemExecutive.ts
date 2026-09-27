import { component, css, html, property } from "@a11d/lit"
import { Executive, ExecutiveIncentive, isDecreeIncentive } from "@pleiades/sdk"
import { EntityItem } from "./EntityItem"
import { ActiveTimeframesRef, ExecutiveModal, tooltip, type ContextMenuSpec } from ".."
import { polarisActivityMenu } from "./polarisActivity"

/**
 * A Polaris executive as a list row — the single successor to the former ObjectiveItemExecutive and
 * DecreeItemAttentive. Since PEP111 an executive backs an objective **or** a decree, so this one component
 * draws both: the incentive's directive glyph and college chrome, an allocation notch, and a timeframe-affinity
 * chip where a bare incentive would show its Celestron. A decree-backed executive carries a small decree glyph
 * so the two kinds still read apart. While its affinity timeframe is active the row wears the item flare, a side
 * glass glow in the flare accent (PEP100 patch 3).
 *
 * A committed change is announced upward with a bubbling `updateRequest` event so the hosting cycle reconciles.
 */
@component('p7t-incentive-item-exec')
export class IncentiveItemExecutive extends EntityItem<ExecutiveIncentive> {

	@property({
		updated(this: IncentiveItemExecutive, value: Executive | undefined) {
			// The row *is* the executive, but its chrome (title, directive, college) is the incentive behind it.
			this.entity = value?.incentive
		}
	}) executive?: Executive

	/** The timeframes active right now — the one listing the active-timeframe chips read too. */
	private readonly activeTimeframes = new ActiveTimeframesRef(this)

	override get disabled() {
		return !!this.executive?.executed
	}

	/**
	 * An open executive flares while its affinity timeframe is active (PEP100 patch 3): the row is where its work
	 * belongs right now. A done one never does — there is nothing left to point at.
	 */
	protected override get flaring() {
		const executive = this.executive
		return !!executive
			&& !executive.executed
			&& this.activeTimeframes.isActive(executive.affinityTimeframeId ?? executive.affinityTimeframe?.id)
	}

	static override get styles() {
		return css`
			${super.styles}

			.college {
				width: 36px;
				align-self: stretch;
				display: flex;
				align-items: center;
				justify-content: center;
			}

			.decree-indicator {
				opacity: .7;
			}
		`
	}

	/**
	 * Inside a cycle the row is the executive, not the incentive: its menu marks it done, manages its time, edits
	 * the objective or decree behind it, or takes it out of the cycle — never the incentive's own "Delete". A
	 * host-supplied menu still wins, as on every entity item.
	 */
	protected override contextMenuSpec(): ContextMenuSpec | undefined {
		if (this.menu || !this.interactive || !this.executive) {
			return super.contextMenuSpec()
		}

		return polarisActivityMenu(this.executive, updated => {
			this.executive = updated
			this.dispatchEvent(new CustomEvent<void>('updateRequest', { bubbles: true, composed: true }))
		})
	}

	protected override get preTitle() {
		return html`<p7t-directive-item small .directive=${this.entity?.directive}></p7t-directive-item>`
	}

	/** A decree-backed executive shows a small decree glyph, so it reads apart from an objective-backed one. */
	protected override get titleSuffix() {
		return isDecreeIncentive(this.entity)
			? html`<p7t-icon class='decree-indicator' ${tooltip('Decree')} icon='decree'></p7t-icon>`
			: html``
	}

	/**
	 * An executive shows its timeframe **affinity** where an incentive would show its Celestron — the Celestron is
	 * replaced outright (PEP100 patch). The affinity chip carries its own empty state ("No Affinity").
	 */
	protected override get info() {
		return html`<p7t-timeframe-item affinity small mode='icon' .timeframe=${this.executive?.affinityTimeframe}></p7t-timeframe-item>`
	}

	protected override async notchAction() {
		if (!this.executive) return
		new ExecutiveModal(this.executive, executive => {
			this.executive = executive
			this.dispatchEvent(new CustomEvent<void>('updateRequest', { bubbles: true, composed: true }))
		}).open()
	}

	protected override get notchTemplate() {
		// The allocation chip owns the whole state read — resolved, no-allocation, time-left, active, overworked —
		// and its progress tooltip.
		const executive = this.executive
		return html`
			<p7t-allocation-item
				?executed=${!!executive?.executed}
				.estimation=${executive?.estimation}
				.minimum=${executive?.minimum}
				.maximum=${executive?.maximum}
				.elapsed=${executive?.elapsed ?? 0}>
			</p7t-allocation-item>
		`
	}

	protected override get highlightInfo() {
		// Both incentive kinds carry a college (Unspecified when unset), so the highlight always draws one.
		return html`
			<div class='college'>
				<p7t-college-item mode='icon' .college=${this.entity?.college}></p7t-college-item>
			</div>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-incentive-item-exec': IncentiveItemExecutive
	}
}
