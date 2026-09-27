import { component, css, CSSResult, nothing, property } from "@a11d/lit"
import { DecreeStatus, DirectiveStatus, FateStatus, LunarDirectiveStatus, ObjectiveStatus } from "@pleiades/sdk"
import { IconName } from ".."
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
	/**
	 * Draws the state glyph alone (its label becomes the tooltip) — the form a notch/knock wants. The attribute is
	 * spelled out because Lit lowercases a property name into its attribute by default (`icononly`), which neither
	 * the `icon-only` attribute set on the tag nor the `:host([icon-only])` style would then match.
	 */
	@property({ type: Boolean, reflect: true, attribute: 'icon-only' }) iconOnly = false

	static override get styles() {
		return css`
			${super.styles}

			.info-bullet {
				user-select: none;
				margin-inline-end: .4ch;
			}

			:host([icon-only]) .info-bullet {
				margin-inline-end: 0;
			}
		`
	}

	private get descriptor() {
		return statusDescriptors[this.status]
	}

	protected override get bulletIcon(): IconName {
		return this.descriptor?.icon ?? 'exec-order'
	}

	protected override get bulletText() {
		return this.descriptor?.label ?? this.status
	}

	/** The icon-only form draws the state glyph alone; its label moves to the tooltip. */
	protected override get textHidden(): boolean {
		return this.iconOnly
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