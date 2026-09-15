import { component, css, html, HTMLTemplateResult } from "@a11d/lit"
import { EntityItem } from "./EntityItem"
import { Decree, DecreeStatus } from "@pleiades/sdk"
import { core, tooltip } from ".."

/**
 * List-row for a decree, the decree-side twin of {@link ObjectiveItem}: its toplane carries the directive glyph and
 * the decree's active-Celestron reward, its notch shows the decree state, its highlight the college, and its extra
 * action adds the decree to the current Polaris cycle (materializing an attentive).
 */
@component('p7t-decree-item')
export class DecreeItem extends EntityItem<Decree> {

	protected get decree() {
		return this.entity
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

			.attentive-indicator {
				background-color: color-mix(in srgb, var(--p7t-flare-accent) 16%, transparent);
				border-radius: 6px;
				font-size: .96em;
				color: color-mix(in srgb, var(--p7t-flare-accent) 70%, var(--text-normal));
			}

			p7t-celestron-item {
				color: color-mix(in srgb, var(--p7t-flare-accent) 70%, var(--text-normal));
			}
		`
	}

	override get disabled() {
		return this.entity?.status === DecreeStatus.Abandoned
	}

	protected override get preTitle() {
		return html`<p7t-directive-item small .directive=${this.decree!.directive}></p7t-directive-item>`
	}

	protected override get titleSuffix() {
		return html`
			<p7t-icon ${tooltip('Attentive')} class='attentive-indicator' icon='attentive'></p7t-icon>
		`
	}

	protected override get info() {
		// A decree's reward is its per-attentive active Celestron, granted on each execution.
		return html`
			<p7t-celestron-item small .value=${this.decree!.activeCelestron}></p7t-celestron-item>
		`
	}

	protected override get extraActionTemplate(): HTMLTemplateResult | undefined {
		return html`
			<p7t-icon class='notch-icon' icon='lucide:chevron-right'></p7t-icon>
		`
	}

	protected override async extraAction() {
		const decreeId = this.decree!.id
		const attentive = await core.polaris.addAttentive({ decreeId })
		if (attentive) {
			// The new attentive belongs to the owning cycle and shows on the briefing card; nudge both so it appears.
			await core.repos.polaris.revalidateObserved()
			await core.repos.briefing.revalidateIfObserved()
		}
	}

	protected override get notchTemplate() {
		return html`
			<p7t-status-item icon-only .status=${DecreeStatus[this.decree!.status] as keyof typeof DecreeStatus}></p7t-status-item>
		`
	}

	protected override get highlightInfo() {
		return html`
			<div class='college'>
				<p7t-college-item mode='icon' .college=${this.decree!.college}></p7t-college-item>
			</div>
		`
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-decree-item': DecreeItem
	}
}
