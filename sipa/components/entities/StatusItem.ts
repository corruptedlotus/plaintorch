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
/**
 * Where a terminal state leaves its item. Both kinds are done with, one way or another, and drawn dimmed wherever the
 * item shows. A `finished` item (done, fulfilled, failed, cancelled) stays in sight; an `archived` one is also put away,
 * so the backlog hides it until a search asks for it.
 */
export type StatusTerminal = 'finished' | 'archived'

type StatusDescriptor = {
	icon: IconName,
	label: string,
	colour?: CSSResult,
	/** Set on a terminal state only; a live state (still to be worked or watched) leaves it unset. */
	terminal?: StatusTerminal,
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

	Done: { icon: 'state-done', label: 'Done', terminal: 'finished' },
	Fulfilled: { icon: 'state-done', label: 'Fulfilled', terminal: 'finished' },

	Archived: { icon: 'state-archived', label: 'Archived', terminal: 'archived' },
	Over: { icon: 'state-archived', label: 'Over', terminal: 'archived' },
	Stale: { icon: 'state-archived', label: 'Stale', terminal: 'archived' },
	OptOut: { icon: 'state-archived', label: 'Opted Out', terminal: 'archived' },
	Abandoned: { icon: 'state-archived', label: 'Abandoned', terminal: 'archived' },

	Failed: { icon: 'state-failed', label: 'Failed', terminal: 'finished' },
	Cancelled: { icon: 'state-failed', label: 'Cancelled', terminal: 'finished' },
}

/** Whether a state is terminal: its item is done with, one way or another, and is drawn dimmed wherever it shows. */
export function isInactiveStatus(status: Status | undefined): boolean {
	return status !== undefined && statusDescriptors[status]?.terminal !== undefined
}

/** Whether a state also puts its item away, so the backlog keeps the item out of sight until a search asks for it. */
export function isArchivedStatus(status: Status | undefined): boolean {
	return status !== undefined && statusDescriptors[status]?.terminal === 'archived'
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