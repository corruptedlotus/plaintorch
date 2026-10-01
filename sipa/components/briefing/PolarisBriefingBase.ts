import { css, eventListener, html, nothing, state, type HTMLTemplateResult } from "@a11d/lit"
import type { PolarisCycle } from "@pleiades/sdk"
import { BriefingCard } from "./BriefingCard"
import {
	polarisActivityKind, polarisActivityKinds, removePolarisActivity, revalidatePolarisCycles, TransferController,
	type CreationRowCreated, type PolarisActivity, type PolarisActivityCreated
} from ".."

/**
 * The shared body of a surface showing one Polaris cycle: its executives as a list each can be dragged out of, the
 * inline creation row at the list's foot, and the recycle bin the footer turns into while one of the cycle's own
 * executives is in the air — a drop there takes the executive out of the cycle.
 *
 * Two derivations draw a cycle with it. The briefing card (`p7t-briefing-polaris`) is the day being worked: the
 * cycle's timer, its reflectives, the progress against the estimate, and Conclude. The forecast column
 * (`p7t-polaris-column`) is a day being planned, one per day: the estimates edited in place, the total planned
 * time, and Delete. Each adds its own chrome — heading and footer — and whatever else it takes by drag.
 */
export abstract class PolarisBriefingBase extends BriefingCard<PolarisCycle> {
	/** Whether the inline creation row is open at the foot of the list. */
	@state() protected creating = false

	/**
	 * Offers the cycle's own activities for dragging. Source-only, and it keeps what it hands out: what a drop means is
	 * the receiver's business — the bin below, or another day's column — and the list is the repository's to redraw.
	 */
	protected readonly activities = new TransferController<PolarisActivity>(this, {
		kind: polarisActivityKind,
		accepts: [],
	})

	/**
	 * The recycle bin. While one of this cycle's activities is in the air the footer becomes a bin, and a drop there
	 * takes the activity out of the cycle — deleting the executive, not the objective or decree behind it. A controller
	 * of its own, since a host's source is never a candidate for its own drag. The footer frame is the drop target
	 * rather than the bin drawn in it: the frame is there before the drag starts.
	 */
	protected readonly bin = new TransferController<PolarisActivity>(this, {
		accepts: polarisActivityKinds,
		canAccept: (_, transaction) => this.editable && transaction.source === this.activities,
		accept: activity => removePolarisActivity(activity),
		dropTarget: () => this.renderRoot.querySelector("[part='footer']"),
	})

	/** Whether the cycle takes changes here: new activities, drops, removals. A derivation closes an ended day. */
	protected get editable() {
		return !!this.data
	}

	/** Whether the executives draw in planning mode — the notch as the estimate, edited in place. */
	protected get planning() {
		return false
	}

	/** The planned time of the whole cycle: its executives' estimates, summed. */
	protected get totalEstimation() {
		return this.data?.executives.reduce((total, executive) => total + (executive.estimation ?? 0), 0) ?? 0
	}

	/** The time worked in the cycle so far: its executives' tracked minutes, summed. */
	protected get totalElapsed() {
		return this.data?.executives.reduce((total, executive) => total + (executive.elapsed ?? 0), 0) ?? 0
	}

	static override get styles() {
		return css`
			${super.styles}

			:host {
				--p7t-flare-accent: var(--p7t-accent-polaris);
				outline: 2px dashed transparent;
				outline-offset: -2px;
				transition: outline-color .3s ease, box-shadow .3s ease;
			}

			/* Something this cycle can take is in the air: show it as a place it can land. */
			:host([transfer-target]) {
				outline-color: color-mix(in srgb, var(--p7t-flare-accent) 55%, transparent);
			}

			/* ...and it is over the cycle. */
			:host([transfer-over]) {
				outline-style: solid;
				outline-color: var(--p7t-flare-accent);
				box-shadow: inset 0 0 0 100vmax color-mix(in srgb, var(--p7t-flare-accent) 8%, transparent);
			}

			/* The footer as a recycle bin, for the life of a drag of one of the cycle's activities. */
			.bin {
				grid-column: 1 / -1;
				display: flex;
				align-items: center;
				justify-content: center;
				gap: .6em;
				padding: .7em 1em;
				border-radius: 12px;
				border: 2px dashed color-mix(in srgb, var(--text-error, crimson) 55%, transparent);
				color: var(--text-error, crimson);
				font-weight: 500;
				text-align: center;
				transition: background-color .2s ease, border-color .2s ease;
				/* The frame takes the drop; the bin's own children must not break the hover into enter/leave pairs. */
				pointer-events: none;
			}

			.bin p7t-icon {
				flex: 0 0 auto;
				width: 1.3em;
				height: 1.3em;
			}

			[part='footer'][transfer-over] .bin {
				border-style: solid;
				border-color: var(--text-error, crimson);
				background-color: color-mix(in srgb, var(--text-error, crimson) 16%, transparent);
			}

			.add-button {
				margin: .4em 1.2em;

				&::part(icon) {
					height: 1.4em;
					width: 1.4em;
				}
			}
		`
	}

	/** The cycle's executives, each draggable out of it — while the cycle takes changes at all. */
	protected get executivesTemplate() {
		const editable = this.editable
		return this.data!.executives.map(executive => html`
			<p7t-incentive-item-exec
				?interactive=${editable}
				?planning=${this.planning}
				.executive=${executive}
				${editable ? this.activities.draggable(executive) : nothing}>
			</p7t-incentive-item-exec>
		`)
	}

	/** The foot of the list: the button opening the creation row, or the row itself — into this cycle. */
	protected get creationTemplate() {
		if (!this.editable) {
			return nothing
		}

		return !this.creating ? html`
			<p7t-button @click=${() => this.addActivity()} icon='lucide:plus' class='add-button'>Add Activity</p7t-button>
		` : html`
			<p7t-polaris-creation-row
				.cycle=${this.data}
				@cancel=${() => this.creating = false}
				@created=${(e: CustomEvent<CreationRowCreated<PolarisActivityCreated>>) => this.onActivityCreated(e)}>
			</p7t-polaris-creation-row>
		`
	}

	protected override get footer() {
		return this.bin.isCandidate ? this.binTemplate : this.footerTemplate
	}

	/** What the footer shows outside a drag — the derivation's summary and actions. */
	protected abstract get footerTemplate(): HTMLTemplateResult

	protected get binTemplate() {
		return html`
			<div class='bin'>
				<p7t-icon icon='lucide:trash-2'></p7t-icon>
				<span>Drop here to remove from this cycle</span>
			</div>
		`
	}

	/** Opens the inline creation row at the foot of the list, in place of the picker modal it replaced. */
	protected addActivity() {
		if (!this.editable) return
		this.creating = true
	}

	/** A committed row: the cycle re-reads itself through the row's own revalidation; the row stays only to make another. */
	private onActivityCreated(e: CustomEvent<CreationRowCreated<PolarisActivityCreated>>) {
		e.stopPropagation()
		if (e.detail.mode !== 'again') {
			this.creating = false
		}
	}

	/**
	 * An executive row committed a change of its own — Done, its allocation, its estimate: the row already draws it,
	 * but the cycle's copy (and what is summed from it) re-reads.
	 */
	@eventListener('updateRequest')
	protected onExecutiveUpdated(e: Event) {
		e.stopPropagation()
		void revalidatePolarisCycles()
	}
}
