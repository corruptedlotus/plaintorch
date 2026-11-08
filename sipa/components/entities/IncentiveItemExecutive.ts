import { component, css, html, property } from "@a11d/lit"
import { Executive, ExecutiveIncentive, isDecreeIncentive } from "@pleiades/sdk"
import { EntityItem } from "./EntityItem"
import { ActiveTimeframesRef, tooltip, type ContextMenuSpec, type EditableTimeUnit } from ".."
import { offerEstimation, openPolarisActivityAllocation, polarisActivityMenu, promptTrackedTime, setPolarisActivityEstimation } from "./polarisActivity"

/**
 * A Polaris executive as a list row — the single successor to the former ObjectiveItemExecutive and
 * DecreeItemAttentive. Since PEP111 an executive backs an objective **or** a decree, so this one component
 * draws both: the incentive's directive glyph and college chrome, an allocation notch, and a timeframe-affinity
 * chip where a bare incentive would show its Celestron. A decree-backed executive carries a small decree glyph
 * so the two kinds still read apart. While its affinity timeframe is active the row wears the item flare, a side
 * glass glow in the flare accent (PEP100 patch 3).
 *
 * The row works on the executive, not the incentive behind it: a press on the title opens the allocation modal, and one
 * on the notch the tracked time in a bubble beside it. With {@link planning} on, the notch is the executive's estimate
 * instead, edited in place — for surfaces that plan days rather than work them. A row that is not interactive (an
 * ended day's record) does neither.
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

	/**
	 * Planning mode: the notch stops reading out the allocation's state and becomes the executive's estimate — its
	 * total planned time — edited in place, since planning a day is setting how long each thing gets. The full
	 * allocation (minimum, maximum, tracked time) stays a press on the title away, or Time Management in its menu.
	 */
	@property({ type: Boolean, reflect: true }) planning = false

	/** The timeframes active right now — the one listing the active-timeframe chips read too. */
	private readonly activeTimeframes = new ActiveTimeframesRef(this)

	override get disabled() {
		return !!this.executive?.executed
	}

	/**
	 * An open executive flares while its affinity timeframe is active (PEP100 patch 3): the row is where its work
	 * belongs right now. A done one never does — there is nothing left to point at — and neither does a row planning a
	 * day or recording an ended one (non-interactive): "right now" is the worked day's business.
	 */
	protected override get flaring() {
		const executive = this.executive
		return !!executive
			&& this.interactive
			&& !this.planning
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

			/* The estimate stands where the allocation chip would, at the chip's scale. */
			.estimate {
				font-size: 1.5em;
				font-weight: 400;
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

		return polarisActivityMenu(this.executive, updated => this.changed(updated))
	}

	/** The title opens the executive's allocation modal — its times, its affinity, its done flag — not the incentive's editor. */
	protected override async titleAction() {
		if (!this.interactive || !this.executive) return
		openPolarisActivityAllocation(this.executive, updated => this.changed(updated))
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

	/**
	 * The notch edits the tracked time in a bubble beside it; a second press closes it. In planning mode the notch is
	 * an editor of its own, so a press there edits the estimate, nothing more.
	 */
	protected override async notchAction() {
		const executive = this.executive
		if (!this.interactive || !executive || this.planning) return

		const updated = await promptTrackedTime(executive, this.notchElement)
		if (updated) {
			this.changed(updated)
		}
	}

	/**
	 * Asks for the executive's estimate in a "Set estimation" bubble at the notch, as a row just added to its cycle does
	 * (see {@link offerEstimation}). A changed estimate is written, and the row draws it at once.
	 */
	async promptEstimation() {
		const executive = this.executive
		if (!executive) return

		await this.updateComplete
		const updated = await offerEstimation(executive, this.notchElement)
		if (updated) {
			this.changed(updated)
		}
	}

	/** Where the row's bubbles point: the notch, or the row while it has not drawn one. */
	private get notchElement(): HTMLElement {
		return this.renderRoot.querySelector<HTMLElement>('.notch') ?? this
	}

	/** Draws a committed change at once, and tells the hosting cycle to re-read itself. */
	private changed(updated: Executive) {
		this.executive = updated
		this.dispatchEvent(new CustomEvent<void>('updateRequest', { bubbles: true, composed: true }))
	}

	/** Commits an estimate edited in the planning notch. An unchanged value writes nothing. */
	private async commitEstimation(e: Event) {
		const executive = this.executive
		const estimation = (e.target as EditableTimeUnit).value ?? undefined
		if (!executive || estimation === (executive.estimation ?? undefined)) return

		const updated = await setPolarisActivityEstimation(executive, estimation)
		if (!updated) {
			// Refused: draw the estimate it still has, not the one typed.
			this.requestUpdate()
			return
		}

		this.changed(updated)
	}

	protected override get notchTemplate() {
		const executive = this.executive
		if (this.planning) {
			return html`
				<p7t-editable-time-unit
					class='estimate'
					nullable
					accent
					${tooltip('Estimate')}
					.value=${executive?.estimation}
					@change=${(e: Event) => this.commitEstimation(e)}>
				</p7t-editable-time-unit>
			`
		}

		// The allocation chip owns the whole state read — resolved, no-allocation, time-left, active, overworked —
		// and its progress tooltip.
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
