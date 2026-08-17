import { component, css, CSSResult, html, nothing, property } from "@a11d/lit"
import { DecreeStatus, DirectiveStatus, FateStatus, LunarDirectiveStatus, ObjectiveStatus } from "@pleiades/sdk"
import { IconName } from "components"
import { InfoItem } from "../design/InfoItem"

export type Status =
	| keyof typeof DirectiveStatus
	| keyof typeof ObjectiveStatus
	| keyof typeof LunarDirectiveStatus
	| keyof typeof FateStatus
	| keyof typeof DecreeStatus
type StatusDescriptor = {
	icon: IconName,
	label: string,
	colour?: CSSResult,
}

export const statusDescriptors: Record<Status, StatusDescriptor> = {
	Standby: { icon: 'state-zero', label: 'Standby' },
	Planned: { icon: 'state-zero', label: 'Planned' },
	OnHold: { icon: 'state-zero', label: 'On Hold' },
	Blocked: { icon: 'state-blocked', label: 'Blocked' },
	Committed: { icon: 'state-commit', label: 'Committed' },

	Active: { icon: 'state-active', label: 'Active' },
	Onrush: { icon: 'state-onrush', label: 'Onrush' },
	Polaris: { icon: 'state-polaris', label: 'Polaris' },

	Done: { icon: 'state-done', label: 'Done' },
	Fulfilled: { icon: 'state-done', label: 'Fulfilled' },

	Archived: { icon: 'state-archived', label: 'Archived' },
	Over: { icon: 'state-archived', label: 'Over' },
	Stale: { icon: 'state-archived', label: 'Stale' },
	OptOut: { icon: 'state-archived', label: 'Opted Out' },
	Abandoned: { icon: 'state-archived', label: 'Abandoned' },

	Failed: { icon: 'state-failed', label: 'Failed' },
	Cancelled: { icon: 'state-failed', label: 'Cancelled' },
}

@component('p7t-status-item')
export class StatusItem extends InfoItem {

	@property() status: Status = 'Standby'
	/** Draws the state glyph alone (its label becomes the tooltip) — the form a notch/knock wants. */
	@property({ type: Boolean, reflect: true }) iconOnly = false

	static override get styles() {
		return css`
			${super.styles}

			.status {
				display: inline-grid;
				grid-template-columns: 2em auto;
				align-items: center;
				gap: .6ch;
				user-select: none;
				margin-inline-end: .4ch;
			}

			:host([icon-only]) .status {
				grid-template-columns: 2em;
				margin-inline-end: 0;
			}

			:host([icon-only]) .label {
				display: none;
			}

			p7t-icon {
				height: 2em;
				width: 2em;
			}
		`
	}

	private get descriptor() {
		return statusDescriptors[this.status]
	}

	protected override get content() {
		const descriptor = this.descriptor
		return html`
			<span class='status'>
				<p7t-icon part='icon' icon="${descriptor?.icon ?? 'exec-order'}"></p7t-icon>
				<span class='label'>${descriptor?.label ?? this.status}</span>
			</span>
		`
	}

	protected override get tooltip() {
		// The label is already on screen unless it is hidden, so only the icon-only form needs a tooltip to name it.
		return this.iconOnly ? (this.descriptor?.label ?? this.status) : nothing
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-status-item': StatusItem
	}
}