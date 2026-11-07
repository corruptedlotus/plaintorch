import { component, css, html } from '@a11d/lit'
import type { Decree, Fate, Objective } from '@pleiades/sdk'
import { shiftState } from '..'
import { EntityItem } from '../entities/EntityItem'
import { entityIcon, entityKindLabel, entityKindOf, entityStatusName, isInactiveEntity } from '../grid/entityActions'
import type { GridEntity } from '../grid/entityTree'

/**
 * An incentive — an objective, a fate or a decree — as one tile of a Moonlight section, drawn in the shape of a
 * dependency-canvas node: a bordered card with its state in the notch, its kind above the title, and its reward or
 * next occurrence beside it.
 *
 * Unlike a canvas node it is a plain list item: the title opens the entity's full banner, a right-click its context
 * menu, and an objective's notch shifts its state. The directive it belongs to is the section it sits in, so it is not
 * repeated here.
 */
@component('p7t-moonlight-incentive-item')
export class MoonlightIncentiveItem extends EntityItem<GridEntity> {
	static override get styles() {
		return css`
			${super.styles}

			:host {
				box-sizing: border-box;
				min-width: 0;
				padding-block: 6px;
				background-color: var(--background-primary, var(--background-secondary));
				border: 1px solid color-mix(in srgb, var(--text-normal) 18%, transparent);
				border-radius: 12px;
			}

			:host([interactive]:hover) {
				border-color: color-mix(in srgb, var(--p7t-flare-accent, var(--interactive-accent)) 45%, transparent);
			}

			.grid {
				grid-template-columns: 2.6em minmax(0, 1fr);
				min-width: 0;
			}

			.notch p7t-status-item::part(icon) {
				width: 1.6em;
				height: 1.6em;
			}

			.kind {
				flex: none;
				display: flex;
				align-items: center;
				gap: 4px;
				opacity: .5;
				font-family: var(--font-interface);
				font-size: .85em;
				line-height: .9;

				& p7t-icon {
					flex: 0 0 auto;
					width: 1.1em;
					height: 1.1em;
				}
			}

			/* The date gives way before the kind does: a tile is narrow, and a long weekday would run into the label. */
			.next {
				min-width: 0;
				overflow: hidden;
				opacity: .6;
				font-size: .8em;
				white-space: nowrap;
			}

			.title {
				min-width: 0;
				font-size: 1.05em;

				& span {
					overflow: hidden;
					text-overflow: ellipsis;
					white-space: nowrap;
				}
			}
		`
	}

	/** An incentive done with — in any terminal state — is drawn dimmed. */
	override get disabled() {
		return isInactiveEntity(this.entity)
	}

	protected override get notchTemplate() {
		return !this.entity ? html`` : html`
			<p7t-status-item icon-only .status=${entityStatusName(this.entity)}></p7t-status-item>
		`
	}

	protected override get preTitle() {
		const entity = this.entity
		return !entity ? html`` : html`
			<span class='kind'>
				<p7t-icon icon=${entityIcon(entity)}></p7t-icon>
				${entityKindLabel(entityKindOf(entity))}
			</span>
		`
	}

	/** An objective's Celestron, a decree's per-attentive reward, a fate's next occurrence. */
	protected override get info() {
		const entity = this.entity
		switch (entity && entityKindOf(entity)) {
			case 'objective':
				return html`<p7t-celestron-item small .value=${(entity as Objective).celestronValue}></p7t-celestron-item>`
			case 'decree':
				return html`<p7t-celestron-item small .value=${(entity as Decree).activeCelestron}></p7t-celestron-item>`
			case 'fate': {
				const next = (entity as Fate).nextOccurrence
				return !next ? html`` : html`<p7t-datetime-view class='next' relative .date=${next}></p7t-datetime-view>`
			}
			default:
				return html``
		}
	}

	/** An objective's notch shifts its state, the way it does in every objective list. */
	/** It goes through the state bubble, which saves the shift; every surface showing the objective follows. */
	protected override async notchAction(notch: HTMLElement) {
		const entity = this.entity
		if (entity && entityKindOf(entity) === 'objective') {
			await shiftState(notch, entity as Objective)
		}
	}
}

declare global {
	interface HTMLElementTagNameMap {
		'p7t-moonlight-incentive-item': MoonlightIncentiveItem
	}
}
