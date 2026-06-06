import { Component, component, css, CSSResult, html, property } from "@a11d/lit"
import { DirectiveStatus, ObjectiveStatus } from "@pleiades/sdk"
import { IconName } from "components"

type Status = keyof typeof DirectiveStatus | keyof typeof ObjectiveStatus
type StatusDescriptor = {
	icon: IconName,
	label: string,
	colour?: CSSResult,
}

export const statusDescriptors: Record<Status, StatusDescriptor> = {
	Standby: { icon: 'state-zero', label: 'Standby' },
	Planned: { icon: 'state-zero', label: 'Planned' },
	Blocked: { icon: 'state-blocked', label: 'Blocked' },
	Committed: { icon: 'state-commit', label: 'Committed' },

	Active: { icon: 'state-active', label: 'Active' },
	Onrush: { icon: 'state-onrush', label: 'Onrush' },
	Polaris: { icon: 'state-polaris', label: 'Polaris' },
	
	Done: { icon: 'state-done', label: 'Done' },
	Fulfilled: { icon: 'state-done', label: 'Fulfilled' },

	Archived: { icon: 'state-archived', label: 'Archived' },
	Over: { icon: 'state-archived', label: 'Over' },
}

@component('p7t-status-item')
export class StatusItem extends Component {

	@property() status: Status = 'Standby'

	static override get styles() {
		return css`
			:host {
				display: grid;
				grid-template-columns: 2em auto;
				align-items: center;
				gap: .6ch;
				user-select: none;
				margin-inline-end: .4ch;
			}

			p7t-icon {
				height: 2em;
				width: 2em;
			}
		`
	}

	protected override get template() {
		return html`
			<p7t-icon part='icon' icon="${statusDescriptors[this.status]?.icon ?? 'exec-order'}"></p7t-icon>
			<span>${statusDescriptors[this.status]?.label ?? this.status}</span>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-status-item': StatusItem
	}
}